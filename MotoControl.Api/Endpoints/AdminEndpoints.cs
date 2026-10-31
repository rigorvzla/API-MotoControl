using MotoControl.Api.Models;
using MotoControl.Api.Services;
using System.Security.Claims;

namespace MotoControl.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/admin")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin))
            .WithTags("Administración");

        // ── Usuarios ──────────────────────────────────────────────
        grupo.MapGet("/usuarios", async (UsuarioService usuarios)
            => Results.Ok(await usuarios.ListarUsuariosAsync()));

        grupo.MapPost("/usuarios", async (UsuarioCreateRequest req, UsuarioService usuarios)
            => Results.Ok(await usuarios.CrearUsuarioAsync(req)));

        grupo.MapPut("/usuarios/{id:guid}", async (
            Guid id, UsuarioUpdateRequest req, ClaimsPrincipal user, UsuarioService usuarios)
            => Results.Ok(await usuarios.ActualizarUsuarioAsync(id, req, user.GetUserId())));

        grupo.MapDelete("/usuarios/{id:guid}", async (
            Guid id, ClaimsPrincipal user, UsuarioService usuarios) =>
        {
            await usuarios.EliminarUsuarioAsync(id, user.GetUserId());
            return Results.NoContent();
        });

        // ── Asociaciones ──────────────────────────────────────────
        grupo.MapGet("/asociaciones", async (UsuarioService usuarios)
            => Results.Ok(await usuarios.ListarAsociacionesAsync()));

        grupo.MapPost("/asociaciones", async (AsociacionRequest req, UsuarioService usuarios)
            => Results.Ok(await usuarios.CrearAsociacionAsync(req)));

        grupo.MapPut("/asociaciones/{id:guid}", async (
            Guid id, AsociacionRequest req, UsuarioService usuarios)
            => Results.Ok(await usuarios.ActualizarAsociacionAsync(id, req)));

        grupo.MapDelete("/asociaciones/{id:guid}", async (Guid id, UsuarioService usuarios) =>
        {
            await usuarios.EliminarAsociacionAsync(id);
            return Results.NoContent();
        });

        // ── Resumen global ────────────────────────────────────────
        grupo.MapGet("/resumen", async (UsuarioService usuarios) =>
        {
            var usuariosLista = await usuarios.ListarUsuariosAsync();
            var asociaciones = await usuarios.ListarAsociacionesAsync();

            return Results.Ok(new
            {
                usuarios = usuariosLista.Count,
                admins = usuariosLista.Count(u => u.Rol == Roles.Admin),
                gestores = usuariosLista.Count(u => u.Rol == Roles.Gestor),
                usuariosActivos = usuariosLista.Count(u => u.Activo),
                asociaciones = asociaciones.Count,
                socios = asociaciones.Sum(a => a.Socios)
            });
        });
    }
}
