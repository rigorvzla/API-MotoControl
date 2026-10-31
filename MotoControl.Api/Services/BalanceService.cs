using MotoControl.Api.Data;
using MotoControl.Api.Models;

namespace MotoControl.Api.Services;

/// <summary>Balance general por año: ingresos por socio, morosidad y datos del gráfico.</summary>
public sealed class BalanceService
{
    private readonly SocioService _socios;
    private readonly PagoService _pagos;
    private readonly RecaudoService _recaudos;
    private readonly Clock _clock;

    public BalanceService(SocioService socios, PagoService pagos, RecaudoService recaudos, Clock clock)
    {
        _socios = socios;
        _pagos = pagos;
        _recaudos = recaudos;
        _clock = clock;
    }

    public async Task<BalanceGeneralDto> ObtenerAsync(Guid asociacionId, int? anioSolicitado)
    {
        var socios = await _socios.ListarAsync(asociacionId, null);
        var todosPagos = await _pagos.ListarTodosAsync(asociacionId);

        var anios = new SortedSet<int> { _clock.Today.Year };
        foreach (var p in todosPagos) anios.Add(p.Anio);
        foreach (var s in socios)
            if (s.FechaIngreso.HasValue) anios.Add(s.FechaIngreso.Value.Year);

        var anio = anioSolicitado is > 2000 and < 2200
            ? anioSolicitado.Value
            : (anios.Contains(_clock.Today.Year) ? _clock.Today.Year : anios.Max);

        var desde = new DateOnly(anio, 1, 1);
        var hasta = new DateOnly(anio, 12, 31);

        var pagosAnio = await _pagos.ListarAsync(asociacionId, null, desde, hasta);
        var recaudosAnio = await _recaudos.ListarAsync(asociacionId, desde, hasta, (int)TipoRecaudo.Ingreso, null);

        var pagosPorSocio = pagosAnio.GroupBy(p => p.NumeroSocio)
            .ToDictionary(g => g.Key, g => g.ToList());
        var recaudosPorSocio = recaudosAnio.Items
            .Where(r => !string.IsNullOrWhiteSpace(r.NumeroSocio))
            .GroupBy(r => r.NumeroSocio)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Monto));

        var items = new List<BalanceSocioDto>();

        foreach (var socio in socios)
        {
            var cuotas = await _pagos.EstadoCuotasAsync(
                asociacionId, socio.NumeroSocio, socio.FechaIngreso, _clock);

            var atrasadas = cuotas.Where(c => c.Estado == "Atrasada").ToList();
            var pendientes = cuotas.Where(c => c.Estado == "Pendiente").ToList();

            var pagosSocio = pagosPorSocio.TryGetValue(socio.NumeroSocio, out var lista)
                ? lista : new List<PagoDto>();

            var cuotaTipica = PagoService.CuotaTipica(pagosSocio.Select(p => new Pago { Monto = p.Monto }));
            if (cuotaTipica == 0)
            {
                var historicos = await _pagos.ObtenerPagosSocioAsync(asociacionId, socio.NumeroSocio);
                cuotaTipica = PagoService.CuotaTipica(historicos);
            }

            var primeraAtrasada = atrasadas.Count > 0 ? atrasadas.Min(c => c.FechaVencimiento) : (DateOnly?)null;
            var ultimoPago = pagosSocio.Count > 0
                ? pagosSocio.Max(p => p.FechaPago) : (DateTime?)null;

            items.Add(new BalanceSocioDto(
                socio.NumeroSocio,
                socio.NombreCompleto,
                socio.Cedula,
                pagosSocio.Count,
                pagosSocio.Sum(p => p.Monto),
                recaudosPorSocio.TryGetValue(socio.NumeroSocio, out var ra) ? ra : 0m,
                0, // TotalAportado se calcula abajo
                atrasadas.Count,
                pendientes.Count,
                primeraAtrasada,
                ultimoPago,
                cuotaTipica * atrasadas.Count,
                atrasadas.Count > 0,
                atrasadas.Count > 0 ? $"⚠️ En mora ({atrasadas.Count})" : "✅ Al día",
                ultimoPago?.ToString("dd/MM/yyyy") ?? "—",
                primeraAtrasada?.ToString("dd/MM/yyyy") ?? "—"));
        }

        items = items
            .Select(i => i with { TotalAportado = i.TotalCuotas + i.RecaudosAportados })
            .OrderByDescending(i => i.TotalAportado)
            .ThenBy(i => i.Numero)
            .ToList();

        var grafico = ConstruirGrafico(pagosAnio, recaudosAnio.Items);

        var morosos = items.Where(i => i.EnMora)
            .OrderByDescending(i => i.MontoMora)
            .ThenBy(i => i.Numero)
            .ToList();

        return new BalanceGeneralDto(
            anio,
            anios.OrderByDescending(a => a).ToList(),
            socios.Count,
            items.Sum(i => i.TotalCuotas),
            items.Sum(i => i.RecaudosAportados),
            items.Sum(i => i.TotalAportado),
            morosos.Count,
            items.Sum(i => i.CuotasAtrasadas),
            items.Sum(i => i.MontoMora),
            items,
            morosos,
            grafico);
    }

    private static GraficoDto ConstruirGrafico(
        IReadOnlyList<PagoDto> pagos, IReadOnlyList<RecaudoDto> recaudos)
    {
        var meses = Enumerable.Range(1, 12)
            .Select(m => PagoService.NombreMes(m)[..3].TrimEnd('.'))
            .ToList();

        var cuotas = Enumerable.Range(1, 12)
            .Select(m => pagos.Where(p => p.FechaPago.Month == m).Sum(p => p.Monto))
            .ToList();

        var extras = Enumerable.Range(1, 12)
            .Select(m => recaudos.Where(r => r.Fecha.Month == m).Sum(r => r.Monto))
            .ToList();

        return new GraficoDto(meses, cuotas, extras);
    }

    /// <summary>Datos del dashboard (resumen del mes + cumpleaños).</summary>
    public async Task<DashboardDto> DashboardAsync(Guid asociacionId)
    {
        var hoy = _clock.Today;
        var primerDia = new DateOnly(hoy.Year, hoy.Month, 1);
        var ultimoDia = new DateOnly(hoy.Year, hoy.Month, DateTime.DaysInMonth(hoy.Year, hoy.Month));

        var socios = await _socios.ListarAsync(asociacionId, null);
        var pagosMes = await _pagos.ListarAsync(asociacionId, null, primerDia, ultimoDia);
        var recaudosMes = await _recaudos.ListarAsync(asociacionId, primerDia, ultimoDia, null, null);
        var cumpleanos = await _socios.CumpleanosDelMesAsync(asociacionId, hoy.Month, _clock);

        return new DashboardDto(
            socios.Count,
            pagosMes.Count,
            pagosMes.Sum(p => p.Monto),
            recaudosMes.TotalIngresos,
            recaudosMes.TotalEgresos,
            recaudosMes.Balance,
            $"{PagoService.NombreMes(hoy.Month)} {hoy.Year}",
            cumpleanos);
    }

    /// <summary>Reporte de morosidad (socios con cuotas atrasadas).</summary>
    public async Task<List<MorosidadDto>> MorosidadAsync(Guid asociacionId)
    {
        var socios = await _socios.ListarAsync(asociacionId, null);
        var resultado = new List<MorosidadDto>();

        foreach (var socio in socios)
        {
            var cuotas = await _pagos.EstadoCuotasAsync(
                asociacionId, socio.NumeroSocio, socio.FechaIngreso, _clock);
            var atrasadas = cuotas.Where(c => c.Estado == "Atrasada").ToList();
            if (atrasadas.Count == 0) continue;

            var pagos = await _pagos.ObtenerPagosSocioAsync(asociacionId, socio.NumeroSocio);
            var tipica = PagoService.CuotaTipica(pagos);

            resultado.Add(new MorosidadDto(
                socio.NumeroSocio,
                socio.NombreCompleto,
                atrasadas.Count,
                atrasadas.Min(c => c.FechaVencimiento),
                tipica * atrasadas.Count));
        }

        return resultado.OrderByDescending(m => m.CuotasAtrasadas).ToList();
    }
}
