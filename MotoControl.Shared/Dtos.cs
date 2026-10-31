namespace MotoControl.Shared;

/// <summary>Roles disponibles en el sistema.</summary>
public static class Roles
{
    public const string Admin = "admin";
    public const string Gestor = "gestor";
}


// ─────────────────────────────────────────────────────────────
//  Autenticación / usuarios
// ─────────────────────────────────────────────────────────────
public record LoginRequest(string Usuario, string Password);

public record UsuarioDto(
    Guid Id,
    string Usuario,
    string Nombre,
    string? Email,
    string Rol,
    bool Activo,
    Guid? AsociacionId,
    string? AsociacionNombre,
    string? LogoDataUrl,
    decimal? Cuota,
    string? Moneda,
    DateTime? AccesoHasta);

public record LoginResponse(string Token, UsuarioDto Usuario);

public record UsuarioCreateRequest(
    string Usuario,
    string Nombre,
    string? Email,
    string Password,
    string Rol,
    Guid? AsociacionId,
    string? NombreAsociacion,
    decimal? Cuota,
    string? Moneda,
    string? LogoDataUrl,
    int? DiasAcceso);

public record UsuarioUpdateRequest(
    string Nombre,
    string? Email,
    string Rol,
    Guid? AsociacionId,
    string? NombreAsociacion,
    bool Activo,
    string? Password,
    string? LogoDataUrl,
    int? DiasAcceso);

// ─────────────────────────────────────────────────────────────
//  Asociación / configuración
// ─────────────────────────────────────────────────────────────
public record AsociacionDto(
    Guid Id,
    string Nombre,
    decimal Cuota,
    string Moneda,
    string? LogoDataUrl,
    DateTime CreadaEn,
    int Usuarios,
    int Socios);

public record AsociacionRequest(
    string Nombre,
    decimal Cuota,
    string Moneda,
    string? LogoDataUrl);

public record ConfiguracionDto(string Nombre, decimal Cuota, string Moneda, string? LogoDataUrl);

public record ConfiguracionRequest(string Nombre, decimal Cuota, string Moneda);

// ─────────────────────────────────────────────────────────────
//  Socios
// ─────────────────────────────────────────────────────────────
public record SocioDatosDto(
    string NombreCompleto,
    string Cedula,
    DateOnly? FechaNacimiento,
    string Genero,
    string Direccion,
    string Alergico,
    string TipoSangre,
    string Telefono,
    int Hijos,
    string Profesion,
    string Educacion);

public record MotoDto(
    string Marca,
    string Modelo,
    string Placa,
    string Serial,
    string Color,
    int Anio);

public record AvanceDto(
    string NombreCompleto,
    string Cedula,
    string Direccion,
    string Telefono);

public record AsociacionSocioDto(
    DateOnly? FechaIngreso,
    string Miembro);

public record RegistroGuardarRequest(
    string NumeroSocio,
    SocioDatosDto Socio,
    MotoDto Moto,
    AvanceDto Avance,
    AsociacionSocioDto Asociacion);

public record RegistroDto(
    Guid Id,
    string NumeroSocio,
    DateTime FechaRegistro,
    SocioDatosDto Socio,
    MotoDto Moto,
    AvanceDto Avance,
    AsociacionSocioDto Asociacion);

public record SocioResumenDto(
    Guid Id,
    string NumeroSocio,
    string NombreCompleto,
    string Cedula,
    string Telefono,
    string Direccion,
    DateOnly? FechaIngreso,
    string Miembro,
    DateTime FechaRegistro);

public record FichaDto(
    RegistroDto Registro,
    int CuotasPagadas,
    decimal TotalPagado,
    string UltimoPagoTexto,
    int CuotasAtrasadas,
    decimal MontoPendiente);

// ─────────────────────────────────────────────────────────────
//  Pagos
// ─────────────────────────────────────────────────────────────
public record PagoRequest(
    string NumeroSocio,
    int Anio,
    int Mes,
    int Quincena,
    decimal Monto,
    string? Observacion);

public record PagoMesRequest(
    string NumeroSocio,
    int Anio,
    int Mes,
    decimal Monto,
    string? Observacion);

public record PagoDto(
    Guid Id,
    string NumeroSocio,
    string NombreSocio,
    int Anio,
    int Mes,
    int Quincena,
    decimal Monto,
    DateTime FechaPago,
    string Observacion,
    string Etiqueta);

public record CuotaEstadoDto(
    int Anio,
    int Mes,
    int Quincena,
    DateOnly FechaVencimiento,
    string Estado,
    decimal Monto,
    string Observacion,
    Guid? PagoId,
    DateTime? FechaPago,
    string Etiqueta);

// ─────────────────────────────────────────────────────────────
//  Recaudos
// ─────────────────────────────────────────────────────────────
public record RecaudoRequest(
    int Tipo,
    string Concepto,
    string? Descripcion,
    decimal Monto,
    DateOnly Fecha,
    string? NumeroSocio);

public record RecaudoDto(
    Guid Id,
    int Tipo,
    string Concepto,
    string Descripcion,
    decimal Monto,
    DateOnly Fecha,
    string NumeroSocio,
    string NombreSocio);

public record RecaudoListadoDto(
    IReadOnlyList<RecaudoDto> Items,
    decimal TotalIngresos,
    decimal TotalEgresos,
    decimal Balance);

// ─────────────────────────────────────────────────────────────
//  Dashboard / Balance
// ─────────────────────────────────────────────────────────────
public record CumpleanoDto(
    string NumeroSocio,
    string NombreCompleto,
    int Dia,
    int Mes,
    bool EsHoy,
    string Texto);

public record DashboardDto(
    int TotalSocios,
    int CuotasRegistradasMes,
    decimal CuotasCobradasMes,
    decimal IngresosExtras,
    decimal EgresosExtras,
    decimal BalanceExtras,
    string MesActualTexto,
    IReadOnlyList<CumpleanoDto> Cumpleanos);

public record BalanceSocioDto(
    string Numero,
    string Nombre,
    string Cedula,
    int CuotasPagadas,
    decimal TotalCuotas,
    decimal RecaudosAportados,
    decimal TotalAportado,
    int CuotasAtrasadas,
    int CuotasPendientes,
    DateOnly? PrimeraAtrasada,
    DateTime? UltimoPago,
    decimal MontoMora,
    bool EnMora,
    string EstadoTexto,
    string UltimoPagoTexto,
    string PrimeraAtrasadaTexto);

public record GraficoDto(
    IReadOnlyList<string> Meses,
    IReadOnlyList<decimal> Cuotas,
    IReadOnlyList<decimal> Recaudos);

public record BalanceGeneralDto(
    int Anio,
    IReadOnlyList<int> Anios,
    int TotalSocios,
    decimal TotalCuotas,
    decimal TotalRecaudos,
    decimal TotalGeneral,
    int SociosEnMora,
    int CuotasAtrasadasTotal,
    decimal MontoMora,
    IReadOnlyList<BalanceSocioDto> Ingresos,
    IReadOnlyList<BalanceSocioDto> Morosos,
    GraficoDto Grafico);

public record MorosidadDto(
    string Numero,
    string Nombre,
    int CuotasAtrasadas,
    DateOnly? PrimeraAtrasada,
    decimal MontoEstimado);
