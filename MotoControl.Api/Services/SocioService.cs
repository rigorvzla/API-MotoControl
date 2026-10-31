using Dapper;
using Npgsql;
using MotoControl.Api.Data;
using MotoControl.Api.Models;

namespace MotoControl.Api.Services;

public sealed class SocioService
{
    private readonly Db _db;
    private readonly PagoService _pagos;
    private readonly Clock _clock;

    public SocioService(Db db, PagoService pagos, Clock clock)
    {
        _db = db;
        _pagos = pagos;
        _clock = clock;
    }

    // ─────────────────────────────────────────────────────────────
    //  Listado con búsqueda
    // ─────────────────────────────────────────────────────────────
    public async Task<List<SocioResumenDto>> ListarAsync(Guid asociacionId, string? filtro)
    {
        await using var cn = await _db.OpenAsync();

        var filas = await cn.QueryAsync<SocioFila>(@"
            SELECT s.*, sa.fecha_ingreso AS fecha_ingreso, sa.miembro AS miembro
            FROM socios s
            LEFT JOIN socio_asociacion sa ON sa.socio_id = s.id
            WHERE s.asociacion_id = @asociacionId
              AND (@patron = ''
                   OR lower(s.numero_socio)    LIKE @patron
                   OR lower(s.nombre_completo) LIKE @patron
                   OR lower(s.cedula)          LIKE @patron)
            ORDER BY s.numero_socio;",
            new { asociacionId, patron = $"%{(filtro ?? string.Empty).Trim().ToLowerInvariant()}%" });

        return filas.Select(f => new SocioResumenDto(
            f.Id, f.NumeroSocio, f.NombreCompleto, f.Cedula, f.Telefono,
            f.Direccion, f.FechaIngreso, f.Miembro, f.FechaRegistro)).ToList();
    }

    // ─────────────────────────────────────────────────────────────
    //  Registro completo (socio + moto + avance + asociación)
    // ─────────────────────────────────────────────────────────────
    public async Task<RegistroDto?> ObtenerAsync(Guid asociacionId, string numeroSocio)
    {
        await using var cn = await _db.OpenAsync();

        var socio = await cn.QuerySingleOrDefaultAsync<Socio>(@"
            SELECT * FROM socios
            WHERE asociacion_id = @asociacionId AND numero_socio = @numeroSocio;",
            new { asociacionId, numeroSocio });

        if (socio is null) return null;

        return await CargarRegistroAsync(cn, socio);
    }

    public async Task<RegistroDto?> ObtenerPorIdAsync(Guid asociacionId, Guid id)
    {
        await using var cn = await _db.OpenAsync();

        var socio = await cn.QuerySingleOrDefaultAsync<Socio>(@"
            SELECT * FROM socios WHERE id = @id AND asociacion_id = @asociacionId;",
            new { id, asociacionId });

        return socio is null ? null : await CargarRegistroAsync(cn, socio);
    }

    private static async Task<RegistroDto> CargarRegistroAsync(
        Npgsql.NpgsqlConnection cn, Socio socio)
    {
        var moto = await cn.QuerySingleOrDefaultAsync<Moto>(
            "SELECT * FROM motos WHERE socio_id = @id LIMIT 1;", new { id = socio.Id });

        var avance = await cn.QuerySingleOrDefaultAsync<Avance>(
            "SELECT * FROM avances WHERE socio_id = @id LIMIT 1;", new { id = socio.Id });

        var asociacion = await cn.QuerySingleOrDefaultAsync<SocioAsociacion>(
            "SELECT * FROM socio_asociacion WHERE socio_id = @id LIMIT 1;", new { id = socio.Id });

        return new RegistroDto(
            socio.Id,
            socio.NumeroSocio,
            socio.FechaRegistro,
            new SocioDatosDto(
                socio.NombreCompleto, socio.Cedula, socio.FechaNacimiento, socio.Genero,
                socio.Direccion, socio.Alergico, socio.TipoSangre, socio.Telefono,
                socio.Hijos, socio.Profesion, socio.Educacion),
            new MotoDto(
                moto?.Marca ?? "", moto?.Modelo ?? "", moto?.Placa ?? "",
                moto?.Serial ?? "", moto?.Color ?? "", moto?.Anio ?? 0),
            new AvanceDto(
                avance?.NombreCompleto ?? "", avance?.Cedula ?? "",
                avance?.Direccion ?? "", avance?.Telefono ?? ""),
            new AsociacionSocioDto(
                asociacion?.FechaIngreso,
                asociacion?.Miembro ?? ""));
    }

    // ─────────────────────────────────────────────────────────────
    //  Guardar / actualizar
    // ─────────────────────────────────────────────────────────────
    public async Task<Guid> GuardarAsync(Guid asociacionId, RegistroGuardarRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.NumeroSocio))
            throw new ArgumentException("El número de socio es obligatorio.");
        if (string.IsNullOrWhiteSpace(r.Socio.NombreCompleto))
            throw new ArgumentException("El nombre del socio es obligatorio.");
        if (string.IsNullOrWhiteSpace(r.Socio.Cedula))
            throw new ArgumentException("La cédula del socio es obligatoria.");

        await using var cn = await _db.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        var id = Guid.NewGuid();

        try
        {
            await cn.ExecuteAsync(@"
            INSERT INTO socios (id, asociacion_id, numero_socio, nombre_completo, cedula,
                                fecha_nacimiento, genero, direccion, alergico, tipo_sangre,
                                telefono, hijos, profesion, educacion, fecha_registro)
            VALUES (@Id, @AsociacionId, @NumeroSocio, @NombreCompleto, @Cedula,
                    @FechaNacimiento, @Genero, @Direccion, @Alergico, @TipoSangre,
                    @Telefono, @Hijos, @Profesion, @Educacion, now());",
                new
                {
                    Id = id,
                    AsociacionId = asociacionId,
                    r.NumeroSocio,
                    r.Socio.NombreCompleto,
                    r.Socio.Cedula,
                    r.Socio.FechaNacimiento,
                    r.Socio.Genero,
                    r.Socio.Direccion,
                    r.Socio.Alergico,
                    r.Socio.TipoSangre,
                    r.Socio.Telefono,
                    r.Socio.Hijos,
                    r.Socio.Profesion,
                    r.Socio.Educacion
                }, tx);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new InvalidOperationException(
                $"Ya existe un socio con el número '{r.NumeroSocio}'.");
        }

        await InsertarHijosAsync(cn, tx, id, r);
        await tx.CommitAsync();
        return id;
    }

    public async Task ActualizarAsync(Guid asociacionId, Guid id, RegistroGuardarRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.NumeroSocio))
            throw new ArgumentException("El número de socio es obligatorio.");
        if (string.IsNullOrWhiteSpace(r.Socio.NombreCompleto))
            throw new ArgumentException("El nombre del socio es obligatorio.");
        if (string.IsNullOrWhiteSpace(r.Socio.Cedula))
            throw new ArgumentException("La cédula del socio es obligatoria.");

        await using var cn = await _db.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        var anterior = await cn.ExecuteScalarAsync<string?>(@"
            SELECT numero_socio FROM socios
            WHERE id = @Id AND asociacion_id = @AsociacionId
            FOR UPDATE;",
            new { Id = id, AsociacionId = asociacionId }, tx);

        if (anterior is null) throw new KeyNotFoundException("No se encontró el socio.");

        try
        {
            await cn.ExecuteAsync(@"
                UPDATE socios SET
                    numero_socio     = @NumeroSocio,
                    nombre_completo  = @NombreCompleto,
                    cedula           = @Cedula,
                    fecha_nacimiento = @FechaNacimiento,
                    genero           = @Genero,
                    direccion        = @Direccion,
                    alergico         = @Alergico,
                    tipo_sangre      = @TipoSangre,
                    telefono         = @Telefono,
                    hijos            = @Hijos,
                    profesion        = @Profesion,
                    educacion        = @Educacion
                WHERE id = @Id AND asociacion_id = @AsociacionId;",
                new
                {
                    Id = id,
                    AsociacionId = asociacionId,
                    r.NumeroSocio,
                    r.Socio.NombreCompleto,
                    r.Socio.Cedula,
                    r.Socio.FechaNacimiento,
                    r.Socio.Genero,
                    r.Socio.Direccion,
                    r.Socio.Alergico,
                    r.Socio.TipoSangre,
                    r.Socio.Telefono,
                    r.Socio.Hijos,
                    r.Socio.Profesion,
                    r.Socio.Educacion
                }, tx);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new InvalidOperationException(
                $"Ya existe un socio con el número '{r.NumeroSocio}'.");
        }

        // Si cambió el número, los pagos y recaudos deben seguir al socio.
        if (!string.Equals(anterior, r.NumeroSocio, StringComparison.Ordinal))
        {
            await cn.ExecuteAsync(@"
                UPDATE pagos SET numero_socio = @Nuevo
                WHERE asociacion_id = @AsociacionId AND numero_socio = @Anterior;",
                new { Nuevo = r.NumeroSocio, AsociacionId = asociacionId, Anterior = anterior }, tx);

            await cn.ExecuteAsync(@"
                UPDATE recaudos SET numero_socio = @Nuevo
                WHERE asociacion_id = @AsociacionId AND numero_socio = @Anterior;",
                new { Nuevo = r.NumeroSocio, AsociacionId = asociacionId, Anterior = anterior }, tx);
        }

        // Reemplazo completo de los datos relacionados (equivale al upsert del WPF).
        await cn.ExecuteAsync("DELETE FROM motos            WHERE socio_id = @id;", new { id }, tx);
        await cn.ExecuteAsync("DELETE FROM avances          WHERE socio_id = @id;", new { id }, tx);
        await cn.ExecuteAsync("DELETE FROM socio_asociacion WHERE socio_id = @id;", new { id }, tx);

        await InsertarHijosAsync(cn, tx, id, r);
        await tx.CommitAsync();
    }

    private static async Task InsertarHijosAsync(
        Npgsql.NpgsqlConnection cn,
        Npgsql.NpgsqlTransaction tx,
        Guid socioId,
        RegistroGuardarRequest r)
    {
        await cn.ExecuteAsync(@"
            INSERT INTO motos (id, socio_id, marca, modelo, placa, serial, color, anio)
            VALUES (@Id, @SocioId, @Marca, @Modelo, @Placa, @Serial, @Color, @Anio);",
            new
            {
                Id = Guid.NewGuid(),
                SocioId = socioId,
                r.Moto.Marca,
                r.Moto.Modelo,
                r.Moto.Placa,
                r.Moto.Serial,
                r.Moto.Color,
                Anio = r.Moto.Anio
            }, tx);

        await cn.ExecuteAsync(@"
            INSERT INTO avances (id, socio_id, nombre_completo, cedula, direccion, telefono)
            VALUES (@Id, @SocioId, @NombreCompleto, @Cedula, @Direccion, @Telefono);",
            new
            {
                Id = Guid.NewGuid(),
                SocioId = socioId,
                r.Avance.NombreCompleto,
                r.Avance.Cedula,
                r.Avance.Direccion,
                r.Avance.Telefono
            }, tx);

        await cn.ExecuteAsync(@"
            INSERT INTO socio_asociacion (id, socio_id, fecha_ingreso, miembro)
            VALUES (@Id, @SocioId, @FechaIngreso, @Miembro);",
            new
            {
                Id = Guid.NewGuid(),
                SocioId = socioId,
                r.Asociacion.FechaIngreso,
                Miembro = r.Asociacion.Miembro
            }, tx);
    }

    /// <summary>Elimina el socio y en cascada moto, avance, asociación, pagos y recaudos.</summary>
    public async Task EliminarAsync(Guid asociacionId, Guid id, string numeroSocio)
    {
        await using var cn = await _db.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        await cn.ExecuteAsync(@"
            DELETE FROM pagos    WHERE asociacion_id = @asociacionId AND numero_socio = @numeroSocio;",
            new { asociacionId, numeroSocio }, tx);

        await cn.ExecuteAsync(@"
            DELETE FROM recaudos WHERE asociacion_id = @asociacionId AND numero_socio = @numeroSocio;",
            new { asociacionId, numeroSocio }, tx);

        await cn.ExecuteAsync(@"
            DELETE FROM socios WHERE id = @id AND asociacion_id = @asociacionId;",
            new { id, asociacionId }, tx);

        await tx.CommitAsync();
    }

    public async Task<(int Pagos, int Recaudos)> ContarAsociadosAsync(
        Guid asociacionId, string numeroSocio)
    {
        await using var cn = await _db.OpenAsync();
        var fila = await cn.QuerySingleAsync<ConteoFila>(@"
            SELECT
              (SELECT COUNT(1) FROM pagos    WHERE asociacion_id = @asociacionId AND numero_socio = @numeroSocio) AS pagos,
              (SELECT COUNT(1) FROM recaudos WHERE asociacion_id = @asociacionId AND numero_socio = @numeroSocio) AS recaudos;",
            new { asociacionId, numeroSocio });
        return (fila.Pagos, fila.Recaudos);
    }

    public async Task<DateOnly?> ObtenerFechaIngresoAsync(Guid asociacionId, string numeroSocio)
    {
        await using var cn = await _db.OpenAsync();
        return await cn.ExecuteScalarAsync<DateOnly?>(@"
            SELECT sa.fecha_ingreso
            FROM socio_asociacion sa
            INNER JOIN socios s ON s.id = sa.socio_id
            WHERE s.asociacion_id = @asociacionId AND s.numero_socio = @numeroSocio
            LIMIT 1;",
            new { asociacionId, numeroSocio });
    }

    public async Task<List<CumpleanoDto>> CumpleanosDelMesAsync(
        Guid asociacionId, int mes, Clock clock)
    {
        await using var cn = await _db.OpenAsync();

        var filas = await cn.QueryAsync<CumpleFila>(@"
            SELECT numero_socio, nombre_completo, fecha_nacimiento
            FROM socios
            WHERE asociacion_id = @asociacionId
              AND fecha_nacimiento IS NOT NULL
              AND extract(month FROM fecha_nacimiento) = @mes
            ORDER BY extract(day FROM fecha_nacimiento), numero_socio;",
            new { asociacionId, mes });

        var hoy = clock.Today;
        return filas.Select(f =>
        {
            var esHoy = f.FechaNacimiento.Day == hoy.Day && f.FechaNacimiento.Month == hoy.Month;
            var texto = esHoy
                ? $"🎂 Socio #{f.NumeroSocio} cumple años hoy"
                : $"🎂 Socio #{f.NumeroSocio} cumple años el {f.FechaNacimiento.Day:00}/{f.FechaNacimiento.Month:00}";
            return new CumpleanoDto(
                f.NumeroSocio, f.NombreCompleto,
                f.FechaNacimiento.Day, f.FechaNacimiento.Month, esHoy, texto);
        }).ToList();
    }

    public async Task<FichaDto?> FichaAsync(Guid asociacionId, string numeroSocio)
    {
        var registro = await ObtenerAsync(asociacionId, numeroSocio);
        if (registro is null) return null;

        await using var cn = await _db.OpenAsync();
        var pagos = (await cn.QueryAsync<Pago>(@"
            SELECT * FROM pagos
            WHERE asociacion_id = @asociacionId AND numero_socio = @numeroSocio
            ORDER BY anio, mes, quincena;",
            new { asociacionId, numeroSocio })).ToList();

        var cuotas = await _pagos.EstadoCuotasAsync(
            asociacionId, numeroSocio, registro.Asociacion.FechaIngreso, _clock);

        var ultimo = pagos
            .OrderByDescending(p => p.Anio).ThenByDescending(p => p.Mes)
            .ThenByDescending(p => p.Quincena).FirstOrDefault();

        var ultimoTexto = ultimo is null
            ? "Sin pagos registrados"
            : $"{PagoService.Etiqueta(ultimo.Mes, ultimo.Anio, ultimo.Quincena)} · {ultimo.Monto:N2}";

        var atrasadas = cuotas.Where(c => c.Estado == "Atrasada").ToList();
        var cuotaTipica = PagoService.CuotaTipica(pagos);

        return new FichaDto(
            registro,
            pagos.Count,
            pagos.Sum(p => p.Monto),
            ultimoTexto,
            atrasadas.Count,
            cuotaTipica * atrasadas.Count);
    }

    private sealed class SocioFila : Socio
    {
        public DateOnly? FechaIngreso { get; set; }
        public string Miembro { get; set; } = string.Empty;
    }

    private sealed class ConteoFila
    {
        public int Pagos { get; set; }
        public int Recaudos { get; set; }
    }

    private sealed class CumpleFila
    {
        public string NumeroSocio { get; set; } = string.Empty;
        public string NombreCompleto { get; set; } = string.Empty;
        public DateOnly FechaNacimiento { get; set; }
    }
}
