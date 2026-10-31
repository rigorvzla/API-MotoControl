namespace MotoControl.Api.Services;

/// <summary>
/// Reloj de negocio: la app original usa la hora local del equipo.
/// Como la API puede correr en otro huso horario, se configura la zona
/// de la asociación (por defecto America/Caracas).
/// </summary>
public sealed class Clock
{
    private readonly TimeZoneInfo _tz;

    public Clock(IConfiguration configuration)
    {
        var id = configuration["App:TimeZone"] ?? "America/Caracas";
        _tz = Resolver(id);
    }

    private static TimeZoneInfo Resolver(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("SA Western Standard Time"); }
            catch { return TimeZoneInfo.Utc; }
        }
    }

    public DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);
    public DateOnly Today => DateOnly.FromDateTime(Now);
    public DateTime ToLocal(DateTime utc) =>
        utc.Kind == DateTimeKind.Utc
            ? TimeZoneInfo.ConvertTimeFromUtc(utc, _tz)
            : utc;
}
