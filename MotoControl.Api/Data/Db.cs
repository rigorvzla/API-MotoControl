using Dapper;
using Npgsql;

namespace MotoControl.Api.Data;

/// <summary>
/// Acceso a CockroachDB (protocolo PostgreSQL) y creación idempotente del esquema.
/// </summary>
public sealed class Db
{
    private readonly string _connectionString;
    private readonly ILogger<Db> _log;

    private readonly (string Usuario, string Password) _seed;

    public Db(IConfiguration configuration, ILogger<Db> log)
    {
        _connectionString = configuration.GetConnectionString("Cockroach")
            ?? throw new InvalidOperationException(
                "Falta la cadena de conexión 'ConnectionStrings:Cockroach' en appsettings.json.");

        var seedUser = configuration["Seed:AdminUser"];
        var seedPassword = configuration["Seed:AdminPassword"];

        _seed = (
            string.IsNullOrWhiteSpace(seedUser) ? "admin" : seedUser,
            string.IsNullOrWhiteSpace(seedPassword) ? "Admin123!" : seedPassword);

        _log = log;
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var cn = new NpgsqlConnection(_connectionString);
        await cn.OpenAsync(ct);
        return cn;
    }

    /// <summary>Espera a que CockroachDB responda, crea la base si no existe y aplica el esquema.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(ct);
        await RetryAsync(async () =>
        {
            await using var cn = await OpenAsync(ct);
            await cn.ExecuteAsync(Esquema, commandTimeout: 120);
        }, "aplicar esquema", ct);

        await SeedAdminAsync(ct);
        _log.LogInformation("CockroachDB lista: esquema aplicado.");
    }

    private async Task EnsureDatabaseAsync(CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder(_connectionString);
        var targetDb = builder.Database ?? "motocontrol";
        builder.Database = "defaultdb";

        try
        {
            await using var cn = new NpgsqlConnection(builder.ConnectionString);
            await cn.OpenAsync(ct);
            await cn.ExecuteAsync($"CREATE DATABASE IF NOT EXISTS \"{targetDb.Replace("\"", "\"\"")}\"");
            _log.LogInformation("Base de datos '{Db}' verificada/creada.", targetDb);
        }
        catch (PostgresException ex) when (ex.SqlState is "42501" or "3D000")
        {
            // Sin permisos para defaultdb: se asume que la base ya existe.
            _log.LogWarning("No se pudo verificar la base '{Db}': {Msg}", targetDb, ex.MessageText);
        }
    }

    private async Task SeedAdminAsync(CancellationToken ct)
    {
        await using var cn = await OpenAsync(ct);

        var existeAdmin = await cn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM usuarios WHERE rol = 'admin'");

        if (existeAdmin > 0) return;

        var id = Guid.NewGuid();
        await cn.ExecuteAsync(@"
            INSERT INTO usuarios (id, usuario, nombre, email, password_hash, rol, asociacion_id, activo, creado_en)
            VALUES (@Id, @Usuario, @Nombre, @Email, @Hash, 'admin', NULL, true, now());",
            new
            {
                Id = id,
                Usuario = _seed.Usuario,
                Nombre = "Administrador del sistema",
                Email = (string?)null,
                Hash = BCrypt.Net.BCrypt.HashPassword(_seed.Password)
            });

        _log.LogWarning(
            "Usuario administrador creado: '{Usuario}'. Cambia la contraseña al iniciar sesión.",
            _seed.Usuario);
    }

    private static async Task RetryAsync(Func<Task> action, string accion, CancellationToken ct)
    {
        const int intentos = 12;
        for (var i = 1; i <= intentos; i++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (i < intentos)
            {
                Console.WriteLine($"CockroachDB no disponible al {accion} (intento {i}/{intentos}): {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }

        await action(); // último intento: deja propagar el error real
    }

    // ─────────────────────────────────────────────────────────────
    //  Esquema (CockroachDB)
    // ─────────────────────────────────────────────────────────────
    private const string Esquema = """
        CREATE TABLE IF NOT EXISTS asociaciones (
            id                UUID PRIMARY KEY,
            nombre            STRING NOT NULL,
            cuota             DECIMAL(18,2) NOT NULL DEFAULT 0,
            moneda            STRING NOT NULL DEFAULT '$',
            logo              BYTES,
            logo_content_type STRING,
            creada_en         TIMESTAMPTZ NOT NULL DEFAULT now()
        );

        CREATE TABLE IF NOT EXISTS usuarios (
            id            UUID PRIMARY KEY,
            usuario       STRING NOT NULL UNIQUE,
            nombre        STRING NOT NULL DEFAULT '',
            email         STRING,
            password_hash STRING NOT NULL,
            rol           STRING NOT NULL,
            asociacion_id UUID REFERENCES asociaciones(id) ON DELETE SET NULL,
            activo        BOOL NOT NULL DEFAULT true,
            creado_en     TIMESTAMPTZ NOT NULL DEFAULT now()
        );

        -- Fecha límite de uso para gestores (NULL = sin límite).
        ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS acceso_hasta TIMESTAMPTZ;

        CREATE TABLE IF NOT EXISTS socios (
            id               UUID PRIMARY KEY,
            asociacion_id    UUID NOT NULL REFERENCES asociaciones(id) ON DELETE CASCADE,
            numero_socio     STRING NOT NULL,
            nombre_completo  STRING NOT NULL DEFAULT '',
            cedula           STRING NOT NULL DEFAULT '',
            fecha_nacimiento DATE,
            genero           STRING NOT NULL DEFAULT '',
            direccion        STRING NOT NULL DEFAULT '',
            alergico         STRING NOT NULL DEFAULT '',
            tipo_sangre      STRING NOT NULL DEFAULT '',
            telefono         STRING NOT NULL DEFAULT '',
            hijos            INT8 NOT NULL DEFAULT 0,
            profesion        STRING NOT NULL DEFAULT '',
            educacion        STRING NOT NULL DEFAULT '',
            fecha_registro   TIMESTAMPTZ NOT NULL DEFAULT now(),
            CONSTRAINT ux_socios_numero UNIQUE (asociacion_id, numero_socio)
        );

        CREATE TABLE IF NOT EXISTS motos (
            id       UUID PRIMARY KEY,
            socio_id UUID NOT NULL REFERENCES socios(id) ON DELETE CASCADE,
            marca    STRING NOT NULL DEFAULT '',
            modelo   STRING NOT NULL DEFAULT '',
            placa    STRING NOT NULL DEFAULT '',
            serial   STRING NOT NULL DEFAULT '',
            color    STRING NOT NULL DEFAULT '',
            anio     INT8 NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS avances (
            id              UUID PRIMARY KEY,
            socio_id        UUID NOT NULL REFERENCES socios(id) ON DELETE CASCADE,
            nombre_completo STRING NOT NULL DEFAULT '',
            cedula          STRING NOT NULL DEFAULT '',
            direccion       STRING NOT NULL DEFAULT '',
            telefono        STRING NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS socio_asociacion (
            id            UUID PRIMARY KEY,
            socio_id      UUID NOT NULL REFERENCES socios(id) ON DELETE CASCADE,
            fecha_ingreso DATE,
            miembro       STRING NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS pagos (
            id            UUID PRIMARY KEY,
            asociacion_id UUID NOT NULL REFERENCES asociaciones(id) ON DELETE CASCADE,
            numero_socio  STRING NOT NULL,
            anio          INT8 NOT NULL,
            mes           INT8 NOT NULL,
            quincena      INT8 NOT NULL,
            monto         DECIMAL(18,2) NOT NULL,
            fecha_pago    TIMESTAMPTZ NOT NULL DEFAULT now(),
            observacion   STRING NOT NULL DEFAULT '',
            CONSTRAINT ux_pagos_cuota UNIQUE (asociacion_id, numero_socio, anio, mes, quincena)
        );

        CREATE TABLE IF NOT EXISTS recaudos (
            id            UUID PRIMARY KEY,
            asociacion_id UUID NOT NULL REFERENCES asociaciones(id) ON DELETE CASCADE,
            tipo          INT8 NOT NULL,
            concepto      STRING NOT NULL,
            descripcion   STRING NOT NULL DEFAULT '',
            monto         DECIMAL(18,2) NOT NULL,
            fecha         DATE NOT NULL,
            numero_socio  STRING NOT NULL DEFAULT ''
        );

        CREATE INDEX IF NOT EXISTS ix_socios_asoc   ON socios (asociacion_id, numero_socio);
        CREATE INDEX IF NOT EXISTS ix_pagos_asoc    ON pagos (asociacion_id, numero_socio, anio, mes);
        CREATE INDEX IF NOT EXISTS ix_recaudos_asoc ON recaudos (asociacion_id, fecha);
        CREATE INDEX IF NOT EXISTS ix_motos_socio   ON motos (socio_id);
        CREATE INDEX IF NOT EXISTS ix_avances_socio ON avances (socio_id);
        """;
}
