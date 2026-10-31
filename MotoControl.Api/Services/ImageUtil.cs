namespace MotoControl.Api.Services;

/// <summary>Utilidades para el logo de la asociación (se envía como Data URL base64).</summary>
public static class ImageUtil
{
    public const int MaxBytes = 2 * 1024 * 1024; // 2 MB

    public static string? ToDataUrl(byte[]? bytes, string? contentType)
    {
        if (bytes is null || bytes.Length == 0) return null;
        var tipo = string.IsNullOrWhiteSpace(contentType) ? "image/png" : contentType;
        return $"data:{tipo};base64,{Convert.ToBase64String(bytes)}";
    }

    /// <summary>Convierte "data:image/png;base64,..." en bytes + tipo. Devuelve null si viene vacío.</summary>
    public static (byte[] Bytes, string ContentType)? ParseDataUrl(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl))
            return null;

        if (!dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("El logo debe enviarse como Data URL base64.");

        var coma = dataUrl.IndexOf(',');
        if (coma < 0) throw new ArgumentException("Data URL inválida.");

        var cabecera = dataUrl[5..coma];          // image/png;base64
        var contenido = dataUrl[(coma + 1)..];

        var tipo = cabecera.Split(';')[0].Trim().ToLowerInvariant();
        if (tipo is not ("image/png" or "image/jpeg" or "image/webp"))
            throw new ArgumentException("Solo se permiten imágenes PNG, JPG o WEBP.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(contenido);
        }
        catch (FormatException)
        {
            throw new ArgumentException("La imagen del logo no es válida.");
        }

        if (bytes.Length > MaxBytes)
            throw new ArgumentException("El logo no debe superar 2 MB.");

        return (bytes, tipo);
    }
}
