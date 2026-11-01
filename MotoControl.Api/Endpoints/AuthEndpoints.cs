using System.Security.Claims;
using MotoControl.Api.Models;
using MotoControl.Api.Services;

namespace MotoControl.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/auth").WithTags("Autenticación");

        grupo.MapPost("/login", async (LoginRequest req, UsuarioService usuarios) =>
        {
            if (string.IsNullOrWhiteSpace(req.Usuario) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { error = "Usuario y contraseña son obligatorios." });

            var resultado = await usuarios.LoginAsync(req.Usuario.Trim(), req.Password);
            return resultado is null
                ? Results.Json(new { error = "Usuario o contraseña incorrectos." }, statusCode: 401)
                : Results.Ok(new LoginResponse(resultado.Value.Token, resultado.Value.Usuario));
        }).AllowAnonymous().RequireRateLimiting("login");

        grupo.MapGet("/me", async (ClaimsPrincipal user, UsuarioService usuarios) =>
        {
            var dto = await usuarios.PorIdAsync(user.GetUserId());
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        }).RequireAuthorization();

        grupo.MapPost("/change-password", async (
            CambiarPasswordRequest req, ClaimsPrincipal user, UsuarioService usuarios) =>
        {
            await usuarios.CambiarPasswordAsync(user.GetUserId(), req.Actual, req.Nueva);
            return Results.Ok(new { mensaje = "Contraseña actualizada." });
        }).RequireAuthorization();
    }
}

public record CambiarPasswordRequest(string Actual, string Nueva);
