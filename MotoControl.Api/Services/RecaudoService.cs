using Dapper;
using MotoControl.Api.Data;
using MotoControl.Api.Models;

namespace MotoControl.Api.Services;

public sealed class RecaudoService
{
    private readonly Db _db;

    public RecaudoService(Db db) => _db = db;

    public async Task<RecaudoListadoDto> ListarAsync(
        Guid asociacionId, DateOnly? desde, DateOnly? hasta, int? tipo, string? numeroSocio)
    {
        await using var cn = await _db.OpenAsync();

        var filas = await cn.QueryAsync<RecaudoFila>(@"
            SELECT r.*, COALESCE(s.nombre_completo, '') AS nombre_socio
            FROM recaudos r
            LEFT JOIN socios s
              ON s.asociacion_id = r.asociacion_id AND s.numero_socio = r.numero_socio
            WHERE r.asociacion_id = @asociacionId
              AND (@desde::date IS NULL OR r.fecha >= @desde::date)
              AND (@hasta::date IS NULL OR r.fecha <= @hasta::date)
              AND (@tipo::int8 IS NULL OR r.tipo = @tipo::int8)
              AND (@numeroSocio = '' OR r.numero_socio = @numeroSocio)
            ORDER BY r.fecha DESC, r.id DESC;",
            new
            {
                asociacionId,
                desde = desde?.ToDateTime(TimeOnly.MinValue),
                hasta = hasta?.ToDateTime(TimeOnly.MinValue),
                tipo,
                numeroSocio = numeroSocio ?? string.Empty
            });

        var items = filas.Select(f => new RecaudoDto(
            f.Id, (int)f.Tipo, f.Concepto, f.Descripcion, f.Monto,
            f.Fecha, f.NumeroSocio, f.NombreSocio)).ToList();

        var ingresos = items.Where(i => i.Tipo == (int)TipoRecaudo.Ingreso).Sum(i => i.Monto);
        var egresos = items.Where(i => i.Tipo == (int)TipoRecaudo.Egreso).Sum(i => i.Monto);

        return new RecaudoListadoDto(items, ingresos, egresos, ingresos - egresos);
    }

    public async Task RegistrarAsync(Guid asociacionId, RecaudoRequest r)
    {
        if (r.Monto <= 0)
            throw new ArgumentException("El monto debe ser mayor a cero.");
        if (string.IsNullOrWhiteSpace(r.Concepto))
            throw new ArgumentException("El concepto es obligatorio.");
        if (r.Tipo is not ((int)TipoRecaudo.Ingreso) and not ((int)TipoRecaudo.Egreso))
            throw new ArgumentException("El tipo debe ser Ingreso (0) o Egreso (1).");

        await using var cn = await _db.OpenAsync();

        await cn.ExecuteAsync(@"
            INSERT INTO recaudos (id, asociacion_id, tipo, concepto, descripcion,
                                  monto, fecha, numero_socio)
            VALUES (@Id, @AsociacionId, @Tipo, @Concepto, @Descripcion,
                    @Monto, @Fecha, @NumeroSocio);",
            new
            {
                Id = Guid.NewGuid(),
                AsociacionId = asociacionId,
                Tipo = (long)r.Tipo,
                Concepto = r.Concepto.Trim(),
                Descripcion = r.Descripcion?.Trim() ?? string.Empty,
                r.Monto,
                r.Fecha,
                NumeroSocio = r.NumeroSocio ?? string.Empty
            });
    }

    public async Task EliminarAsync(Guid asociacionId, Guid id)
    {
        await using var cn = await _db.OpenAsync();
        await cn.ExecuteAsync(
            "DELETE FROM recaudos WHERE id = @id AND asociacion_id = @asociacionId;",
            new { id, asociacionId });
    }

    private sealed class RecaudoFila
    {
        public Guid Id { get; set; }
        public long Tipo { get; set; }
        public string Concepto { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public DateOnly Fecha { get; set; }
        public string NumeroSocio { get; set; } = string.Empty;
        public string NombreSocio { get; set; } = string.Empty;
    }
}
