using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using MotoControl.Api.Models;

namespace MotoControl.Api.Services;

public sealed class JwtService
{
    private readonly string _key;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _horas;

    public JwtService(IConfiguration configuration)
    {
        _key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Falta 'Jwt:Key' en la configuración.");
        _issuer = configuration["Jwt:Issuer"] ?? "MotoControl";
        _audience = configuration["Jwt:Audience"] ?? "MotoControl.Web";
        _horas = int.TryParse(configuration["Jwt:Hours"], out var h) ? h : 4;

        if (Encoding.UTF8.GetByteCount(_key) < 32)
            throw new InvalidOperationException("'Jwt:Key' debe tener al menos 32 caracteres.");
    }

    public string Crear(UsuarioEntity usuario, Asociacion? asociacion)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.Usuario),
            new(ClaimTypes.Role, usuario.Rol),
            new("nombre", usuario.Nombre),
            new("tenant", usuario.AsociacionId?.ToString() ?? string.Empty),
            new("tenantName", asociacion?.Nombre ?? string.Empty),
            new("accesoHasta",
                usuario.AccesoHasta?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty)
        };

        var credenciales = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(_horas),
            signingCredentials: credenciales);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public static class ClaimsExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var valor = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(valor, out var id)
            ? id
            : throw new UnauthorizedAccessException("Token sin identificador de usuario.");
    }

    public static Guid? GetAsociacionId(this ClaimsPrincipal user)
    {
        var valor = user.FindFirstValue("tenant");
        return Guid.TryParse(valor, out var id) ? id : null;
    }

    /// <summary>Asociación obligatoria: un gestor siempre opera dentro de su asociación.</summary>
    public static Guid GetAsociacionRequerida(this ClaimsPrincipal user)
        => user.GetAsociacionId()
           ?? throw new UnauthorizedAccessException("El usuario no tiene una asociación asignada.");

    public static string GetRol(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    public static string GetNombre(this ClaimsPrincipal user)
        => user.FindFirstValue("nombre") ?? user.Identity?.Name ?? string.Empty;

    public static bool EsAdmin(this ClaimsPrincipal user)
        => user.GetRol() == Roles.Admin;
}
