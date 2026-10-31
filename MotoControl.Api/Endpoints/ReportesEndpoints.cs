using System.Security.Claims;
using MotoControl.Api.Models;
using MotoControl.Api.Services;

namespace MotoControl.Api.Endpoints;

public static class ReportesEndpoints
{
    public static void MapReportesEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/reportes")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Gestor))
            .WithTags("Reportes");

        // ── Recibo de una cuota ──────────────────────────────────
        grupo.MapGet("/recibo/{pagoId:guid}", async (
            Guid pagoId, ClaimsPrincipal user, PagoService pagos,
            UsuarioService usuarios, ReporteService reportes) =>
        {
            var asociacionId = user.GetAsociacionRequerida();
            var pago = await pagos.ObtenerPorIdAsync(asociacionId, pagoId)
                ?? throw new KeyNotFoundException("Pago no encontrado.");

            var nombre = await pagos.NombreSocioAsync(asociacionId, pago.NumeroSocio);
            var dto = new PagoDto(
                pago.Id, pago.NumeroSocio, nombre, pago.Anio, pago.Mes, pago.Quincena,
                pago.Monto, pago.FechaPago, pago.Observacion,
                PagoService.Etiqueta(pago.Mes, pago.Anio, pago.Quincena));

            var a = await usuarios.AsociacionEntidadAsync(asociacionId);
            var pdf = reportes.Recibo(a, pago.NumeroSocio, nombre, dto);

            return Results.File(pdf, "application/pdf",
                $"Recibo_{ReporteService.NombreArchivoSeguro(pago.NumeroSocio)}_{pago.Anio}{pago.Mes:00}Q{pago.Quincena}.pdf");
        });

        // ── Balance total ────────────────────────────────────────
        grupo.MapGet("/balance-total", async (
            ClaimsPrincipal user, PagoService pagos,
            UsuarioService usuarios, ReporteService reportes) =>
        {
            var asociacionId = user.GetAsociacionRequerida();
            var a = await usuarios.AsociacionEntidadAsync(asociacionId);
            var lista = await pagos.ListarTodosAsync(asociacionId);
            var pdf = reportes.BalanceTotal(a, lista);
            return Results.File(pdf, "application/pdf", $"Balance_Total_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
        });

        // ── Balance de un socio ──────────────────────────────────
        grupo.MapGet("/balance-socio/{numero}", async (
            string numero, ClaimsPrincipal user, PagoService pagos,
            UsuarioService usuarios, ReporteService reportes) =>
        {
            var asociacionId = user.GetAsociacionRequerida();
            var a = await usuarios.AsociacionEntidadAsync(asociacionId);
            var lista = await pagos.ListarAsync(asociacionId, numero, null, null);
            var nombre = await pagos.NombreSocioAsync(asociacionId, numero);
            var pdf = reportes.BalancePorSocio(a, numero, nombre, lista);
            return Results.File(pdf, "application/pdf",
                $"Balance_Socio_{ReporteService.NombreArchivoSeguro(numero)}_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
        });

        // ── Morosidad ────────────────────────────────────────────
        grupo.MapGet("/morosidad", async (
            ClaimsPrincipal user, BalanceService balance,
            UsuarioService usuarios, ReporteService reportes) =>
        {
            var asociacionId = user.GetAsociacionRequerida();
            var a = await usuarios.AsociacionEntidadAsync(asociacionId);
            var items = await balance.MorosidadAsync(asociacionId);
            var pdf = reportes.Morosidad(a, items);
            return Results.File(pdf, "application/pdf", $"Morosidad_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
        });

        // ── Resumen anual ────────────────────────────────────────
        grupo.MapGet("/resumen-anual/{anio:int}", async (
            int anio, ClaimsPrincipal user, PagoService pagos, RecaudoService recaudos,
            UsuarioService usuarios, ReporteService reportes) =>
        {
            var asociacionId = user.GetAsociacionRequerida();
            var a = await usuarios.AsociacionEntidadAsync(asociacionId);

            var meses = new List<ResumenMensualDto>();
            for (var m = 1; m <= 12; m++)
            {
                var desde = new DateOnly(anio, m, 1);
                var hasta = new DateOnly(anio, m, DateTime.DaysInMonth(anio, m));
                var pagosMes = await pagos.ListarAsync(asociacionId, null, desde, hasta);
                var recaudosMes = await recaudos.ListarAsync(asociacionId, desde, hasta, null, null);

                meses.Add(new ResumenMensualDto(
                    m, pagosMes.Count, pagosMes.Sum(p => p.Monto),
                    recaudosMes.TotalIngresos, recaudosMes.TotalEgresos));
            }

            var pdf = reportes.ResumenAnual(a, anio, meses);
            return Results.File(pdf, "application/pdf", $"Resumen_Anual_{anio}_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
        });

        // ── Balance general ──────────────────────────────────────
        grupo.MapGet("/balance-general/{anio:int}", async (
            int anio, ClaimsPrincipal user, BalanceService balance,
            UsuarioService usuarios, ReporteService reportes) =>
        {
            var asociacionId = user.GetAsociacionRequerida();
            var a = await usuarios.AsociacionEntidadAsync(asociacionId);
            var data = await balance.ObtenerAsync(asociacionId, anio);
            var pdf = reportes.BalanceGeneralDoc(a, data);
            return Results.File(pdf, "application/pdf",
                $"Balance_General_{anio}_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
        });

        // ── Recaudos ─────────────────────────────────────────────
        grupo.MapGet("/recaudos", async (
            DateOnly? desde, DateOnly? hasta, ClaimsPrincipal user, RecaudoService recaudos,
            UsuarioService usuarios, ReporteService reportes) =>
        {
            var asociacionId = user.GetAsociacionRequerida();
            var a = await usuarios.AsociacionEntidadAsync(asociacionId);
            var data = await recaudos.ListarAsync(asociacionId, desde, hasta, null, null);
            var pdf = reportes.Recaudos(a, data, desde, hasta);
            return Results.File(pdf, "application/pdf", $"Recaudos_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
        });
    }
}
