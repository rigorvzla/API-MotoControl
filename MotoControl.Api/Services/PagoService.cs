using System.Globalization;
using Dapper;
using Npgsql;
using MotoControl.Api.Data;
using MotoControl.Api.Models;

namespace MotoControl.Api.Services;

public sealed class PagoService
{
    private static readonly CultureInfo Cultura = new("es-ES");
    private readonly Db _db;
    private readonly Clock _clock;

    public PagoService(Db db, Clock clock)
    {
        _db = db;
        _clock = clock;
    }

    public static string NombreMes(int mes) => Cultura.DateTimeFormat.GetMonthName(mes);

    public static string Etiqueta(int mes, int anio, int quincena)
        => $"{NombreMes(mes)} {anio} · Q{quincena}";

    /// <summary>Cuota que más veces ha pagado un socio (se usa para estimar la deuda).</summary>
    public static decimal CuotaTipica(IEnumerable<Pago> pagos)
        => pagos.GroupBy(p => p.Monto)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();

    // ─────────────────────────────────────────────────────────────
    //  Registrar
    // ─────────────────────────────────────────────────────────────
    public async Task RegistrarAsync(Guid asociacionId, PagoRequest r)
    {
        if (r.Monto <= 0)
            throw new ArgumentException("El monto debe ser mayor a cero.");
        if (r.Quincena is < 1 or > 2)
            throw new ArgumentException("La quincena debe ser 1 o 2.");
        if (r.Mes is < 1 or > 12)
            throw new ArgumentException("El mes debe estar entre 1 y 12.");

        await using var cn = await _db.OpenAsync();

        try
        {
            await cn.ExecuteAsync(@"
                INSERT INTO pagos (id, asociacion_id, numero_socio, anio, mes, quincena,
                                   monto, fecha_pago, observacion)
                VALUES (@Id, @AsociacionId, @NumeroSocio, @Anio, @Mes, @Quincena,
                        @Monto, now(), @Observacion);",
                new
                {
                    Id = Guid.NewGuid(),
                    AsociacionId = asociacionId,
                    r.NumeroSocio,
                    r.Anio,
                    r.Mes,
                    r.Quincena,
                    r.Monto,
                    Observacion = r.Observacion ?? string.Empty
                });
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new InvalidOperationException(
                $"Ya existe un pago registrado para {Etiqueta(r.Mes, r.Anio, r.Quincena)}.");
        }
    }

    /// <summary>Registra las dos quincenas del mes e ignora las que ya existan.</summary>
    public async Task<int> RegistrarMesAsync(Guid asociacionId, PagoMesRequest r)
    {
        var insertados = 0;
        for (var q = 1; q <= 2; q++)
        {
            if (await ExisteAsync(asociacionId, r.NumeroSocio, r.Anio, r.Mes, q)) continue;

            await RegistrarAsync(asociacionId, new PagoRequest(
                r.NumeroSocio, r.Anio, r.Mes, q, r.Monto, r.Observacion));
            insertados++;
        }
        return insertados;
    }

    public async Task<bool> ExisteAsync(
        Guid asociacionId, string numeroSocio, int anio, int mes, int quincena)
    {
        await using var cn = await _db.OpenAsync();
        var n = await cn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1) FROM pagos
            WHERE asociacion_id = @asociacionId AND numero_socio = @numeroSocio
              AND anio = @anio AND mes = @mes AND quincena = @quincena;",
            new { asociacionId, numeroSocio, anio, mes, quincena });
        return n > 0;
    }

    // ─────────────────────────────────────────────────────────────
    //  Consultas
    // ─────────────────────────────────────────────────────────────
    public async Task<List<Pago>> ObtenerPagosSocioAsync(Guid asociacionId, string numeroSocio)
    {
        await using var cn = await _db.OpenAsync();
        return (await cn.QueryAsync<Pago>(@"
            SELECT * FROM pagos
            WHERE asociacion_id = @asociacionId AND numero_socio = @numeroSocio
            ORDER BY anio, mes, quincena;",
            new { asociacionId, numeroSocio })).ToList();
    }

    public async Task<List<PagoDto>> ListarAsync(
        Guid asociacionId, string? numeroSocio, DateOnly? desde, DateOnly? hasta)
    {
        await using var cn = await _db.OpenAsync();

        var filas = await cn.QueryAsync<PagoFila>(@"
            SELECT p.*, COALESCE(s.nombre_completo, '') AS nombre_socio
            FROM pagos p
            LEFT JOIN socios s
              ON s.asociacion_id = p.asociacion_id AND s.numero_socio = p.numero_socio
            WHERE p.asociacion_id = @asociacionId
              AND (@numeroSocio = '' OR p.numero_socio = @numeroSocio)
            ORDER BY p.anio, p.mes, p.quincena, p.numero_socio;",
            new { asociacionId, numeroSocio = numeroSocio ?? string.Empty });

        IEnumerable<PagoFila> resultado = filas;
        if (desde.HasValue)
            resultado = resultado.Where(p => DateOnly.FromDateTime(_clock.ToLocal(p.FechaPago)) >= desde.Value);
        if (hasta.HasValue)
            resultado = resultado.Where(p => DateOnly.FromDateTime(_clock.ToLocal(p.FechaPago)) <= hasta.Value);

        return resultado.Select(ToDto).ToList();
    }

    public async Task<List<PagoDto>> ListarTodosAsync(Guid asociacionId)
        => await ListarAsync(asociacionId, null, null, null);

    private PagoDto ToDto(PagoFila p) => new(
        p.Id, p.NumeroSocio, p.NombreSocio, p.Anio, p.Mes, p.Quincena,
        p.Monto, _clock.ToLocal(p.FechaPago), p.Observacion,
        Etiqueta(p.Mes, p.Anio, p.Quincena));

    public async Task<string> NombreSocioAsync(Guid asociacionId, string numeroSocio)
    {
        await using var cn = await _db.OpenAsync();
        return await cn.ExecuteScalarAsync<string>(@"
            SELECT nombre_completo FROM socios
            WHERE asociacion_id = @asociacionId AND numero_socio = @numeroSocio LIMIT 1;",
            new { asociacionId, numeroSocio }) ?? numeroSocio;
    }

    /// <summary>
    /// Línea de tiempo de cuotas desde la fecha de ingreso del socio
    /// (o enero del año actual) hasta diciembre del año siguiente.
    /// </summary>
    public async Task<List<CuotaEstadoDto>> EstadoCuotasAsync(
        Guid asociacionId, string numeroSocio, DateOnly? fechaInicio, Clock clock)
    {
        var pagos = await ObtenerPagosSocioAsync(asociacionId, numeroSocio);
        var mapa = pagos.ToDictionary(p => (p.Anio, p.Mes, p.Quincena));

        var hoy = clock.Today;
        var inicio = fechaInicio ?? new DateOnly(hoy.Year, 1, 1);
        var fin = new DateOnly(hoy.Year + 1, 12, 31);

        var resultado = new List<CuotaEstadoDto>();

        for (var mes = new DateOnly(inicio.Year, inicio.Month, 1); mes <= fin; mes = mes.AddMonths(1))
        {
            for (var q = 1; q <= 2; q++)
            {
                var venc = q == 1
                    ? new DateOnly(mes.Year, mes.Month, 15)
                    : new DateOnly(mes.Year, mes.Month, DateTime.DaysInMonth(mes.Year, mes.Month));

                mapa.TryGetValue((mes.Year, mes.Month, q), out var pago);

                string estado;
                if (pago is not null)
                    estado = venc > hoy ? "Adelantada" : "Pagada";
                else
                    estado = venc < hoy ? "Atrasada" : "Pendiente";

                resultado.Add(new CuotaEstadoDto(
                    mes.Year, mes.Month, q, venc, estado,
                    pago?.Monto ?? 0m,
                    pago?.Observacion ?? string.Empty,
                    pago?.Id,
                    pago is null ? null : _clock.ToLocal(pago.FechaPago),
                    Etiqueta(mes.Month, mes.Year, q)));
            }
        }

        return resultado;
    }

    public async Task EliminarAsync(Guid asociacionId, Guid id)
    {
        await using var cn = await _db.OpenAsync();
        await cn.ExecuteAsync(
            "DELETE FROM pagos WHERE id = @id AND asociacion_id = @asociacionId;",
            new { id, asociacionId });
    }

    public async Task<Pago?> ObtenerPorIdAsync(Guid asociacionId, Guid id)
    {
        await using var cn = await _db.OpenAsync();
        return await cn.QuerySingleOrDefaultAsync<Pago>(
            "SELECT * FROM pagos WHERE id = @id AND asociacion_id = @asociacionId;",
            new { id, asociacionId });
    }

    private sealed class PagoFila : Pago
    {
        public string NombreSocio { get; set; } = string.Empty;
    }
}
