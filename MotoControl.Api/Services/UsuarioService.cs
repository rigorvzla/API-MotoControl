using Dapper;
using Npgsql;
using MotoControl.Api.Data;
using MotoControl.Api.Models;

namespace MotoControl.Api.Services;

/// <summary>Usuarios, asociaciones (tenants) y configuración de la asociación.</summary>
public sealed class UsuarioService
{
    private readonly Db _db;
    private readonly JwtService _jwt;
    private readonly Clock _clock;

    // Hash de relleno: iguala el tiempo de respuesta cuando el usuario no existe
    // y evita que se puedan enumerar cuentas por el tiempo de respuesta.
    private static readonly string DummyHash =
        BCrypt.Net.BCrypt.HashPassword("motocontrol-usuario-inexistente");

    public UsuarioService(Db db, JwtService jwt, Clock clock)
    {
        _db = db;
        _jwt = jwt;
        _clock = clock;
    }

    public JwtService Jwt => _jwt;

    // ─────────────────────────────────────────────────────────────
    //  Login
    // ─────────────────────────────────────────────────────────────
    public async Task<(string Token, UsuarioDto Usuario)?> LoginAsync(string usuario, string password)
    {
        await using var cn = await _db.OpenAsync();

        var u = await cn.QuerySingleOrDefaultAsync<UsuarioEntity>(
            "SELECT * FROM usuarios WHERE lower(usuario) = lower(@usuario);", new { usuario });

        if (u is null)
        {
            BCrypt.Net.BCrypt.Verify(password, DummyHash);
            return null;
        }

        if (!u.Activo) return null;
        if (!BCrypt.Net.BCrypt.Verify(password, u.PasswordHash)) return null;

        // Control del tiempo de uso contratado (solo aplica a gestores).
        if (u.AccesoHasta is not null && u.AccesoHasta.Value <= DateTime.UtcNow)
        {
            var vence = _clock.ToLocal(u.AccesoHasta.Value);
            throw new UnauthorizedAccessException(
                $"Tu tiempo de uso del sistema venció el {vence:dd/MM/yyyy}. " +
                "Contacta al administrador para renovarlo.");
        }

        var asociacion = u.AsociacionId is null ? null : await CargarAsociacionAsync(cn, u.AsociacionId.Value);
        return (_jwt.Crear(u, asociacion), ToDto(u, asociacion));
    }

    public async Task<UsuarioDto?> PorIdAsync(Guid id)
    {
        await using var cn = await _db.OpenAsync();
        var u = await cn.QuerySingleOrDefaultAsync<UsuarioEntity>(
            "SELECT * FROM usuarios WHERE id = @id;", new { id });
        if (u is null) return null;

        var a = u.AsociacionId is null ? null : await CargarAsociacionAsync(cn, u.AsociacionId.Value);
        return ToDto(u, a);
    }

    public async Task<UsuarioEntity?> BuscarEntidadAsync(Guid id)
    {
        await using var cn = await _db.OpenAsync();
        return await cn.QuerySingleOrDefaultAsync<UsuarioEntity>(
            "SELECT * FROM usuarios WHERE id = @id;", new { id });
    }

    public async Task CambiarPasswordAsync(Guid id, string actual, string nueva)
    {
        if (string.IsNullOrWhiteSpace(nueva) || nueva.Length < 8)
            throw new ArgumentException("La nueva contraseña debe tener al menos 8 caracteres.");

        await using var cn = await _db.OpenAsync();
        var u = await cn.QuerySingleOrDefaultAsync<UsuarioEntity>(
            "SELECT * FROM usuarios WHERE id = @id;", new { id });
        if (u is null) throw new KeyNotFoundException("Usuario no encontrado.");
        if (!BCrypt.Net.BCrypt.Verify(actual, u.PasswordHash))
            throw new ArgumentException("La contraseña actual no es correcta.");

        await cn.ExecuteAsync("UPDATE usuarios SET password_hash = @hash WHERE id = @id;",
            new { hash = BCrypt.Net.BCrypt.HashPassword(nueva), id });
    }

    // ─────────────────────────────────────────────────────────────
    //  Usuarios (solo admin)
    // ─────────────────────────────────────────────────────────────
    public async Task<List<UsuarioDto>> ListarUsuariosAsync()
    {
        await using var cn = await _db.OpenAsync();
        var filas = await cn.QueryAsync<UsuarioFila>(@"
            SELECT u.*, a.nombre AS asociacion_nombre, a.cuota AS asociacion_cuota,
                   a.moneda AS asociacion_moneda, a.logo AS asociacion_logo,
                   a.logo_content_type AS asociacion_logo_content_type
            FROM usuarios u
            LEFT JOIN asociaciones a ON a.id = u.asociacion_id
            ORDER BY u.rol, u.usuario;");

        return filas.Select(f => new UsuarioDto(
            f.Id, f.Usuario, f.Nombre, f.Email, f.Rol, f.Activo,
            f.AsociacionId, f.AsociacionNombre,
            ImageUtil.ToDataUrl(f.AsociacionLogo, f.AsociacionLogoContentType),
            f.AsociacionCuota, f.AsociacionMoneda, f.AccesoHasta)).ToList();
    }

    public async Task<UsuarioDto> CrearUsuarioAsync(UsuarioCreateRequest r)
    {
        ValidarUsuario(r.Usuario, r.Password, r.Rol);

        await using var cn = await _db.OpenAsync();

        // El usuario es único sin distinguir mayúsculas/minúsculas.
        var yaExiste = await cn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM usuarios WHERE lower(usuario) = lower(@usuario);",
            new { usuario = r.Usuario.Trim() });
        if (yaExiste > 0)
            throw new InvalidOperationException($"Ya existe el usuario '{r.Usuario.Trim()}'.");

        Guid? asociacionId = r.AsociacionId;

        if (r.Rol == Roles.Gestor)
        {
            if (asociacionId is null)
            {
                if (string.IsNullOrWhiteSpace(r.NombreAsociacion))
                    throw new ArgumentException(
                        "Para crear un gestor debes indicar el nombre de la asociación.");

                var nueva = await InsertarAsociacionAsync(cn, new AsociacionRequest(
                    r.NombreAsociacion.Trim(),
                    r.Cuota ?? 0m,
                    string.IsNullOrWhiteSpace(r.Moneda) ? "$" : r.Moneda.Trim(),
                    r.LogoDataUrl));

                asociacionId = nueva.Id;
            }
            else
            {
                var existe = await cn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM asociaciones WHERE id = @id;", new { id = asociacionId });
                if (existe == 0) throw new KeyNotFoundException("La asociación indicada no existe.");

                if (!string.IsNullOrWhiteSpace(r.LogoDataUrl))
                    await ActualizarLogoAsync(asociacionId.Value, r.LogoDataUrl);
            }
        }
        else
        {
            asociacionId = null; // el admin es global
        }

        var accesoHasta = r.Rol == Roles.Gestor && r.DiasAcceso is > 0
            ? DateTime.UtcNow.AddDays(r.DiasAcceso.Value)
            : (DateTime?)null;

        var u = new UsuarioEntity
        {
            Id = Guid.NewGuid(),
            Usuario = r.Usuario.Trim(),
            Nombre = r.Nombre?.Trim() ?? string.Empty,
            Email = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(r.Password),
            Rol = r.Rol,
            AsociacionId = asociacionId,
            Activo = true,
            AccesoHasta = accesoHasta
        };

        try
        {
            await cn.ExecuteAsync(@"
                INSERT INTO usuarios (id, usuario, nombre, email, password_hash, rol,
                                      asociacion_id, activo, creado_en, acceso_hasta)
                VALUES (@Id, @Usuario, @Nombre, @Email, @PasswordHash, @Rol,
                        @AsociacionId, true, now(), @AccesoHasta);", u);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new InvalidOperationException($"Ya existe el usuario '{r.Usuario}'.");
        }

        var asociacion = asociacionId is null ? null : await CargarAsociacionAsync(cn, asociacionId.Value);
        return ToDto(u, asociacion);
    }

    public async Task<UsuarioDto> ActualizarUsuarioAsync(Guid id, UsuarioUpdateRequest r, Guid actorId)
    {
        if (r.Rol is not (Roles.Admin or Roles.Gestor))
            throw new ArgumentException("El rol debe ser 'admin' o 'gestor'.");

        await using var cn = await _db.OpenAsync();

        var actual = await cn.QuerySingleOrDefaultAsync<UsuarioEntity>(
            "SELECT * FROM usuarios WHERE id = @id;", new { id })
            ?? throw new KeyNotFoundException("Usuario no encontrado.");

        await ProtegerUltimoAdminAsync(cn, actual, r.Rol, r.Activo);

        if (id == actorId && !r.Activo)
            throw new ArgumentException("No puedes desactivar tu propio usuario.");

        var asociacionId = r.Rol == Roles.Gestor
            ? r.AsociacionId ?? actual.AsociacionId
            : null;

        if (r.Rol == Roles.Gestor && asociacionId is null)
        {
            if (string.IsNullOrWhiteSpace(r.NombreAsociacion))
                throw new ArgumentException("El gestor debe tener una asociación asignada.");

            var nueva = await InsertarAsociacionAsync(cn, new AsociacionRequest(
                r.NombreAsociacion.Trim(), 0m, "$", null));
            asociacionId = nueva.Id;
        }

        var hash = string.IsNullOrWhiteSpace(r.Password)
            ? actual.PasswordHash
            : BCrypt.Net.BCrypt.HashPassword(r.Password);

        if (!string.IsNullOrWhiteSpace(r.Password) && r.Password.Length < 8)
            throw new ArgumentException("La contraseña debe tener al menos 8 caracteres.");

        // null = conservar el vencimiento actual; 0 = sin límite; > 0 = días desde hoy.
        var accesoHasta = r.Rol == Roles.Admin
            ? null
            : r.DiasAcceso switch
            {
                null => actual.AccesoHasta,
                0 => null,
                > 0 => DateTime.UtcNow.AddDays(r.DiasAcceso.Value),
                _ => actual.AccesoHasta
            };

        await cn.ExecuteAsync(@"
            UPDATE usuarios SET
                nombre = @Nombre,
                email = @Email,
                rol = @Rol,
                asociacion_id = @AsociacionId,
                activo = @Activo,
                password_hash = @Hash,
                acceso_hasta = @AccesoHasta
            WHERE id = @Id;",
            new
            {
                Id = id,
                Nombre = r.Nombre?.Trim() ?? string.Empty,
                Email = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email.Trim(),
                r.Rol,
                AsociacionId = asociacionId,
                r.Activo,
                Hash = hash,
                AccesoHasta = accesoHasta
            });

        if (asociacionId is not null && !string.IsNullOrWhiteSpace(r.LogoDataUrl))
            await ActualizarLogoAsync(asociacionId.Value, r.LogoDataUrl);

        var u = await cn.QuerySingleAsync<UsuarioEntity>(
            "SELECT * FROM usuarios WHERE id = @id;", new { id });
        var a = u.AsociacionId is null ? null : await CargarAsociacionAsync(cn, u.AsociacionId.Value);
        return ToDto(u, a);
    }

    public async Task EliminarUsuarioAsync(Guid id, Guid actorId)
    {
        if (id == actorId)
            throw new ArgumentException("No puedes eliminar tu propio usuario.");

        await using var cn = await _db.OpenAsync();
        var u = await cn.QuerySingleOrDefaultAsync<UsuarioEntity>(
            "SELECT * FROM usuarios WHERE id = @id;", new { id })
            ?? throw new KeyNotFoundException("Usuario no encontrado.");

        await ProtegerUltimoAdminAsync(cn, u, null, false);

        await cn.ExecuteAsync("DELETE FROM usuarios WHERE id = @id;", new { id });
    }

    private static async Task ProtegerUltimoAdminAsync(
        NpgsqlConnection cn, UsuarioEntity actual, string? nuevoRol, bool activo)
    {
        if (actual.Rol != Roles.Admin) return;

        var dejaDeSerAdmin = nuevoRol is not null && nuevoRol != Roles.Admin;
        if (!dejaDeSerAdmin && activo) return;

        var admins = await cn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM usuarios WHERE rol = 'admin' AND activo = true;");

        if (admins <= 1)
            throw new InvalidOperationException("Debe existir al menos un administrador activo.");
    }

    private static void ValidarUsuario(string usuario, string password, string rol)
    {
        if (string.IsNullOrWhiteSpace(usuario) || usuario.Trim().Length < 3)
            throw new ArgumentException("El usuario debe tener al menos 3 caracteres.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ArgumentException("La contraseña debe tener al menos 8 caracteres.");
        if (rol is not (Roles.Admin or Roles.Gestor))
            throw new ArgumentException("El rol debe ser 'admin' o 'gestor'.");
    }

    // ─────────────────────────────────────────────────────────────
    //  Asociaciones
    // ─────────────────────────────────────────────────────────────
    public async Task<List<AsociacionDto>> ListarAsociacionesAsync()
    {
        await using var cn = await _db.OpenAsync();

        var filas = await cn.QueryAsync<AsociacionFila>(@"
            SELECT a.*,
                   (SELECT COUNT(1) FROM usuarios u WHERE u.asociacion_id = a.id) AS usuarios,
                   (SELECT COUNT(1) FROM socios  s WHERE s.asociacion_id = a.id) AS socios
            FROM asociaciones a
            ORDER BY a.nombre;");

        return filas.Select(ToAsociacionDto).ToList();
    }

    public async Task<AsociacionDto> CrearAsociacionAsync(AsociacionRequest r)
    {
        ValidarAsociacion(r);
        await using var cn = await _db.OpenAsync();
        var a = await InsertarAsociacionAsync(cn, r);
        return new AsociacionDto(a.Id, a.Nombre, a.Cuota, a.Moneda,
            ImageUtil.ToDataUrl(a.Logo, a.LogoContentType), a.CreadaEn, 0, 0);
    }

    public async Task<AsociacionDto> ActualizarAsociacionAsync(Guid id, AsociacionRequest r)
    {
        ValidarAsociacion(r);
        await using var cn = await _db.OpenAsync();

        var filas = await cn.ExecuteAsync(@"
            UPDATE asociaciones SET nombre = @Nombre, cuota = @Cuota, moneda = @Moneda
            WHERE id = @Id;",
            new { Id = id, Nombre = r.Nombre.Trim(), r.Cuota, Moneda = r.Moneda.Trim() });

        if (filas == 0) throw new KeyNotFoundException("Asociación no encontrada.");

        if (r.LogoDataUrl is not null)
            await ActualizarLogoAsync(id, r.LogoDataUrl);

        var lista = await ListarAsociacionesAsync();
        return lista.Single(a => a.Id == id);
    }

    public async Task EliminarAsociacionAsync(Guid id)
    {
        await using var cn = await _db.OpenAsync();

        var usuarios = await cn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM usuarios WHERE asociacion_id = @id;", new { id });
        if (usuarios > 0)
            throw new InvalidOperationException(
                "No se puede eliminar: la asociación tiene usuarios asignados.");

        var socios = await cn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM socios WHERE asociacion_id = @id;", new { id });
        if (socios > 0)
            throw new InvalidOperationException(
                "No se puede eliminar: la asociación tiene socios registrados.");

        await cn.ExecuteAsync("DELETE FROM asociaciones WHERE id = @id;", new { id });
    }

    /// <summary>Cuenta lo que se borraría si se elimina la asociación por completo.</summary>
    public async Task<ImpactoAsociacionDto> ImpactoAsociacionAsync(Guid id)
    {
        await using var cn = await _db.OpenAsync();

        var a = await cn.QuerySingleOrDefaultAsync<Asociacion>(
            "SELECT * FROM asociaciones WHERE id = @id;", new { id })
            ?? throw new KeyNotFoundException("Asociación no encontrada.");

        return new ImpactoAsociacionDto(
            a.Id, a.Nombre,
            await cn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM socios WHERE asociacion_id = @id;", new { id }),
            await cn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM pagos WHERE asociacion_id = @id;", new { id }),
            await cn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM recaudos WHERE asociacion_id = @id;", new { id }),
            await cn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM usuarios WHERE asociacion_id = @id;", new { id }));
    }

    /// <summary>
    /// Elimina la asociación y TODO su contenido: socios (con motos y avances),
    /// pagos, recaudos y usuarios gestores. Es irreversible.
    /// Exige escribir el nombre exacto de la asociación como confirmación.
    /// </summary>
    public async Task<ImpactoAsociacionDto> EliminarAsociacionCompletaAsync(Guid id, string confirmacion)
    {
        await using var cn = await _db.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        var a = await cn.QuerySingleOrDefaultAsync<Asociacion>(
            "SELECT * FROM asociaciones WHERE id = @id;", new { id }, tx)
            ?? throw new KeyNotFoundException("Asociación no encontrada.");

        if (!string.Equals((confirmacion ?? string.Empty).Trim(), a.Nombre.Trim(),
                           StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "El nombre escrito no coincide con el de la asociación. No se eliminó nada.");

        var impacto = new ImpactoAsociacionDto(
            a.Id, a.Nombre,
            await cn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM socios WHERE asociacion_id = @id;", new { id }, tx),
            await cn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM pagos WHERE asociacion_id = @id;", new { id }, tx),
            await cn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM recaudos WHERE asociacion_id = @id;", new { id }, tx),
            await cn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM usuarios WHERE asociacion_id = @id;", new { id }, tx));

        await cn.ExecuteAsync("DELETE FROM pagos WHERE asociacion_id = @id;", new { id }, tx);
        await cn.ExecuteAsync("DELETE FROM recaudos WHERE asociacion_id = @id;", new { id }, tx);
        await cn.ExecuteAsync("DELETE FROM socios WHERE asociacion_id = @id;", new { id }, tx);
        await cn.ExecuteAsync("DELETE FROM usuarios WHERE asociacion_id = @id;", new { id }, tx);
        await cn.ExecuteAsync("DELETE FROM asociaciones WHERE id = @id;", new { id }, tx);

        await tx.CommitAsync();
        return impacto;
    }

    public async Task<ConfiguracionDto> ConfiguracionAsync(Guid asociacionId)
    {
        var a = await AsociacionEntidadAsync(asociacionId);
        return new ConfiguracionDto(a.Nombre, a.Cuota, a.Moneda,
            ImageUtil.ToDataUrl(a.Logo, a.LogoContentType));
    }

    /// <summary>Asociación completa (incluye el logo en bytes) para los reportes PDF.</summary>
    public async Task<Asociacion> AsociacionEntidadAsync(Guid asociacionId)
    {
        await using var cn = await _db.OpenAsync();
        return await CargarAsociacionAsync(cn, asociacionId)
            ?? throw new KeyNotFoundException("Asociación no encontrada.");
    }

    public async Task<ConfiguracionDto> ActualizarConfiguracionAsync(
        Guid asociacionId, ConfiguracionRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Nombre))
            throw new ArgumentException("El nombre de la asociación es obligatorio.");

        await using var cn = await _db.OpenAsync();
        await cn.ExecuteAsync(@"
            UPDATE asociaciones SET cuota = @Cuota, moneda = @Moneda
            WHERE id = @Id;",
            new
            {
                Id = asociacionId,
                Cuota = r.Cuota < 0 ? 0 : r.Cuota,
                Moneda = string.IsNullOrWhiteSpace(r.Moneda) ? "$" : r.Moneda.Trim()
            });

        return await ConfiguracionAsync(asociacionId);
    }

    public async Task ActualizarLogoAsync(Guid asociacionId, string? dataUrl)
    {
        var logo = ImageUtil.ParseDataUrl(dataUrl); // null = quitar logo

        await using var cn = await _db.OpenAsync();
        await cn.ExecuteAsync(@"
            UPDATE asociaciones SET logo = @logo, logo_content_type = @ct WHERE id = @id;",
            new
            {
                id = asociacionId,
                logo = logo?.Bytes,
                ct = logo?.ContentType
            });
    }

    // ─────────────────────────────────────────────────────────────
    //  Internos
    // ─────────────────────────────────────────────────────────────
    private static void ValidarAsociacion(AsociacionRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Nombre))
            throw new ArgumentException("El nombre de la asociación es obligatorio.");
        if (r.Cuota < 0)
            throw new ArgumentException("La cuota no puede ser negativa.");
    }

    private static async Task<Asociacion> InsertarAsociacionAsync(
        NpgsqlConnection cn, AsociacionRequest r)
    {
        var logo = ImageUtil.ParseDataUrl(r.LogoDataUrl);

        var a = new Asociacion
        {
            Id = Guid.NewGuid(),
            Nombre = r.Nombre.Trim(),
            Cuota = r.Cuota < 0 ? 0 : r.Cuota,
            Moneda = string.IsNullOrWhiteSpace(r.Moneda) ? "$" : r.Moneda.Trim(),
            Logo = logo?.Bytes,
            LogoContentType = logo?.ContentType
        };

        await cn.ExecuteAsync(@"
            INSERT INTO asociaciones (id, nombre, cuota, moneda, logo, logo_content_type, creada_en)
            VALUES (@Id, @Nombre, @Cuota, @Moneda, @Logo, @LogoContentType, now());", a);

        a.CreadaEn = DateTime.UtcNow;
        return a;
    }

    private static Task<Asociacion?> CargarAsociacionAsync(NpgsqlConnection cn, Guid id)
        => cn.QuerySingleOrDefaultAsync<Asociacion>(
            "SELECT * FROM asociaciones WHERE id = @id;", new { id });

    private static UsuarioDto ToDto(UsuarioEntity u, Asociacion? a) => new(
        u.Id, u.Usuario, u.Nombre, u.Email, u.Rol, u.Activo,
        u.AsociacionId, a?.Nombre,
        ImageUtil.ToDataUrl(a?.Logo, a?.LogoContentType),
        a?.Cuota, a?.Moneda, u.AccesoHasta);

    private static AsociacionDto ToAsociacionDto(AsociacionFila a) => new(
        a.Id, a.Nombre, a.Cuota, a.Moneda,
        ImageUtil.ToDataUrl(a.Logo, a.LogoContentType),
        a.CreadaEn, a.Usuarios, a.Socios);

    private sealed class UsuarioFila : UsuarioEntity
    {
        public string? AsociacionNombre { get; set; }
        public decimal? AsociacionCuota { get; set; }
        public string? AsociacionMoneda { get; set; }
        public byte[]? AsociacionLogo { get; set; }
        public string? AsociacionLogoContentType { get; set; }
    }

    private sealed class AsociacionFila : Asociacion
    {
        public int Usuarios { get; set; }
        public int Socios { get; set; }
    }
}
