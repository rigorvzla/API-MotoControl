using System.Security.Claims;
using MotoControl.Api.Models;
using MotoControl.Api.Services;

namespace MotoControl.Api.Endpoints;

/// <summary>Endpoints de la asociación (rol gestor). Todos filtran por el tenant del token.</summary>
public static class GestorEndpoints
{
    public static void MapGestorEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Gestor))
            .WithTags("Gestión");

        // ── Configuración de la asociación ────────────────────────
        grupo.MapGet("/configuracion", async (ClaimsPrincipal user, UsuarioService usuarios)
            => Results.Ok(await usuarios.ConfiguracionAsync(user.GetAsociacionRequerida())));

        grupo.MapPut("/configuracion", async (
            ConfiguracionRequest req, ClaimsPrincipal user, UsuarioService usuarios)
            => Results.Ok(await usuarios.ActualizarConfiguracionAsync(
                user.GetAsociacionRequerida(), req)));

        grupo.MapPut("/configuracion/logo", async (
            LogoRequest req, ClaimsPrincipal user, UsuarioService usuarios) =>
        {
            await usuarios.ActualizarLogoAsync(user.GetAsociacionRequerida(), req.LogoDataUrl);
            return Results.Ok(await usuarios.ConfiguracionAsync(user.GetAsociacionRequerida()));
        });

        grupo.MapDelete("/configuracion/logo", async (ClaimsPrincipal user, UsuarioService usuarios) =>
        {
            await usuarios.ActualizarLogoAsync(user.GetAsociacionRequerida(), null);
            return Results.Ok(await usuarios.ConfiguracionAsync(user.GetAsociacionRequerida()));
        });

        // ── Socios ────────────────────────────────────────────────
        grupo.MapGet("/socios", async (string? filtro, ClaimsPrincipal user, SocioService socios)
            => Results.Ok(await socios.ListarAsync(user.GetAsociacionRequerida(), filtro)));

        grupo.MapGet("/socios/{numero}", async (string numero, ClaimsPrincipal user, SocioService socios) =>
        {
            var registro = await socios.ObtenerAsync(user.GetAsociacionRequerida(), numero);
            return registro is null ? Results.NotFound(new { error = "Socio no encontrado." }) : Results.Ok(registro);
        });

        grupo.MapGet("/socios/{numero}/ficha", async (
            string numero, ClaimsPrincipal user, SocioService socios) =>
        {
            var ficha = await socios.FichaAsync(user.GetAsociacionRequerida(), numero);
            return ficha is null ? Results.NotFound(new { error = "Socio no encontrado." }) : Results.Ok(ficha);
        });

        grupo.MapPost("/socios", async (
            RegistroGuardarRequest req, ClaimsPrincipal user, SocioService socios) =>
        {
            var id = await socios.GuardarAsync(user.GetAsociacionRequerida(), req);
            return Results.Ok(new { id, numeroSocio = req.NumeroSocio });
        });

        grupo.MapPut("/socios/{id:guid}", async (
            Guid id, RegistroGuardarRequest req, ClaimsPrincipal user, SocioService socios) =>
        {
            await socios.ActualizarAsync(user.GetAsociacionRequerida(), id, req);
            return Results.NoContent();
        });

        grupo.MapGet("/socios/{numero}/asociados", async (
            string numero, ClaimsPrincipal user, SocioService socios) =>
        {
            var (pagos, recaudos) = await socios.ContarAsociadosAsync(
                user.GetAsociacionRequerida(), numero);
            return Results.Ok(new { pagos, recaudos });
        });

        grupo.MapDelete("/socios/{id:guid}/{numero}", async (
            Guid id, string numero, ClaimsPrincipal user, SocioService socios) =>
        {
            await socios.EliminarAsync(user.GetAsociacionRequerida(), id, numero);
            return Results.NoContent();
        });

        // ── Pagos ─────────────────────────────────────────────────
        grupo.MapGet("/pagos", async (
            string? socio, DateOnly? desde, DateOnly? hasta, ClaimsPrincipal user, PagoService pagos)
            => Results.Ok(await pagos.ListarAsync(user.GetAsociacionRequerida(), socio, desde, hasta)));

        grupo.MapGet("/pagos/cuotas/{numero}", async (
            string numero, ClaimsPrincipal user, SocioService socios, PagoService pagos, Clock clock) =>
        {
            var asociacionId = user.GetAsociacionRequerida();
            var fechaIngreso = await socios.ObtenerFechaIngresoAsync(asociacionId, numero);
            return Results.Ok(await pagos.EstadoCuotasAsync(asociacionId, numero, fechaIngreso, clock));
        });

        grupo.MapPost("/pagos", async (
            PagoRequest req, ClaimsPrincipal user, PagoService pagos) =>
        {
            await pagos.RegistrarAsync(user.GetAsociacionRequerida(), req);
            return Results.Ok(new { mensaje = "Pago registrado." });
        });

        grupo.MapPost("/pagos/mes", async (
            PagoMesRequest req, ClaimsPrincipal user, PagoService pagos) =>
            Results.Ok(new { insertados = await pagos.RegistrarMesAsync(user.GetAsociacionRequerida(), req) }));

        grupo.MapDelete("/pagos/{id:guid}", async (
            Guid id, ClaimsPrincipal user, PagoService pagos) =>
        {
            await pagos.EliminarAsync(user.GetAsociacionRequerida(), id);
            return Results.NoContent();
        });

        // ── Recaudos ──────────────────────────────────────────────
        grupo.MapGet("/recaudos", async (
            DateOnly? desde, DateOnly? hasta, int? tipo, string? socio,
            ClaimsPrincipal user, RecaudoService recaudos) =>
            Results.Ok(await recaudos.ListarAsync(
                user.GetAsociacionRequerida(), desde, hasta, tipo, socio)));

        grupo.MapPost("/recaudos", async (
            RecaudoRequest req, ClaimsPrincipal user, RecaudoService recaudos) =>
        {
            await recaudos.RegistrarAsync(user.GetAsociacionRequerida(), req);
            return Results.Ok(new { mensaje = "Recaudo registrado." });
        });

        grupo.MapDelete("/recaudos/{id:guid}", async (
            Guid id, ClaimsPrincipal user, RecaudoService recaudos) =>
        {
            await recaudos.EliminarAsync(user.GetAsociacionRequerida(), id);
            return Results.NoContent();
        });

        // ── Dashboard y balance ──────────────────────────────────
        grupo.MapGet("/dashboard", async (ClaimsPrincipal user, BalanceService balance)
            => Results.Ok(await balance.DashboardAsync(user.GetAsociacionRequerida())));

        grupo.MapGet("/balance", async (int? anio, ClaimsPrincipal user, BalanceService balance)
            => Results.Ok(await balance.ObtenerAsync(user.GetAsociacionRequerida(), anio)));
    }
}

public record LogoRequest(string? LogoDataUrl);
