namespace MotoControl.Api.Models;

/// <summary>Asociación (tenant). Cada gestor administra una asociación con sus propios socios.</summary>
public class Asociacion
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public decimal Cuota { get; set; }
    public string Moneda { get; set; } = "$";
    public byte[]? Logo { get; set; }
    public string? LogoContentType { get; set; }
    public DateTime CreadaEn { get; set; }
}

/// <summary>Usuario del sistema. rol = "admin" | "gestor".</summary>
public class UsuarioEntity
{
    public Guid Id { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string Rol { get; set; } = Roles.Gestor;
    public Guid? AsociacionId { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime CreadoEn { get; set; }
}

public class Socio
{
    public Guid Id { get; set; }
    public Guid AsociacionId { get; set; }
    public string NumeroSocio { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string Cedula { get; set; } = string.Empty;
    public DateOnly? FechaNacimiento { get; set; }
    public string Genero { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string Alergico { get; set; } = string.Empty;
    public string TipoSangre { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public int Hijos { get; set; }
    public string Profesion { get; set; } = string.Empty;
    public string Educacion { get; set; } = string.Empty;
    public DateTime FechaRegistro { get; set; }
}

public class Moto
{
    public Guid Id { get; set; }
    public Guid SocioId { get; set; }
    public string Marca { get; set; } = string.Empty;
    public string Modelo { get; set; } = string.Empty;
    public string Placa { get; set; } = string.Empty;
    public string Serial { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int Anio { get; set; }
}

public class Avance
{
    public Guid Id { get; set; }
    public Guid SocioId { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
    public string Cedula { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
}

public class SocioAsociacion
{
    public Guid Id { get; set; }
    public Guid SocioId { get; set; }
    public DateOnly? FechaIngreso { get; set; }
    public string Miembro { get; set; } = string.Empty; // Socio | Alquilado
}

public class Pago
{
    public Guid Id { get; set; }
    public Guid AsociacionId { get; set; }
    public string NumeroSocio { get; set; } = string.Empty;
    public int Anio { get; set; }
    public int Mes { get; set; }
    public int Quincena { get; set; }
    public decimal Monto { get; set; }
    public DateTime FechaPago { get; set; }
    public string Observacion { get; set; } = string.Empty;
}

public enum TipoRecaudo
{
    Ingreso = 0,
    Egreso = 1
}

public class Recaudo
{
    public Guid Id { get; set; }
    public Guid AsociacionId { get; set; }
    public TipoRecaudo Tipo { get; set; }
    public string Concepto { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public DateOnly Fecha { get; set; }
    public string NumeroSocio { get; set; } = string.Empty;
}
