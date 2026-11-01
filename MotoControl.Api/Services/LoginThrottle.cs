using System.Collections.Concurrent;

namespace MotoControl.Api.Services;

/// <summary>
/// Freno a la fuerza bruta por nombre de usuario. Funciona aunque la IP
/// que ve la API cambie (Render/Cloudflare), porque la clave es el usuario.
/// Es en memoria: se reinicia cuando el servicio se reinicia.
/// </summary>
public sealed class LoginThrottle
{
    private const int MaxFallos = 8;
    private static readonly TimeSpan Ventana = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Bloqueo = TimeSpan.FromMinutes(10);

    private sealed class Estado
    {
        public int Fallos;
        public DateTime Inicio = DateTime.UtcNow;
        public DateTime? BloqueadoHasta;
    }

    private readonly ConcurrentDictionary<string, Estado> _estados = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _candado = new();

    public static string Clave(string usuario) => usuario.Trim().ToLowerInvariant();

    public bool Bloqueado(string usuario)
    {
        var clave = Clave(usuario);
        if (!_estados.TryGetValue(clave, out var estado)) return false;

        lock (_candado)
        {
            if (estado.BloqueadoHasta is not null)
            {
                if (estado.BloqueadoHasta > DateTime.UtcNow) return true;
                _estados.TryRemove(clave, out _);
                return false;
            }

            if (DateTime.UtcNow - estado.Inicio > Ventana)
                _estados.TryRemove(clave, out _);
        }

        return false;
    }

    public void Fallo(string usuario)
    {
        var clave = Clave(usuario);
        var estado = _estados.GetOrAdd(clave, _ => new Estado());

        lock (_candado)
        {
            if (DateTime.UtcNow - estado.Inicio > Ventana)
            {
                estado.Fallos = 0;
                estado.Inicio = DateTime.UtcNow;
            }

            estado.Fallos++;

            if (estado.Fallos >= MaxFallos)
            {
                estado.BloqueadoHasta = DateTime.UtcNow.Add(Bloqueo);
                estado.Fallos = 0;
            }
        }
    }

    public void Exito(string usuario) => _estados.TryRemove(Clave(usuario), out _);
}
