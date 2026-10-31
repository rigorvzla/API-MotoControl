using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using MotoControl.Api.Data;
using MotoControl.Api.Endpoints;
using MotoControl.Api.Services;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// QuestPDF: licencia comunitaria (igual que la app de escritorio).
QuestPDF.Settings.License = LicenseType.Community;

// ─────────────────────────────────────────────────────────────
//  Servicios
// ─────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();
builder.Services.AddSingleton<Clock>();
builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddScoped<UsuarioService>();
builder.Services.AddScoped<PagoService>();
builder.Services.AddScoped<SocioService>();
builder.Services.AddScoped<RecaudoService>();
builder.Services.AddScoped<BalanceService>();
builder.Services.AddScoped<ReporteService>();

builder.Services.AddCors(options => options.AddPolicy("web", policy =>
{
    var origenes = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:5190"];

    policy.WithOrigins(origenes)
          .AllowAnyHeader()
          .AllowAnyMethod();
}));

var jwt = builder.Configuration.GetSection("Jwt");
var jwtKey = jwt["Key"] ?? throw new InvalidOperationException("Falta 'Jwt:Key'.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"] ?? "MotoControl",
            ValidateAudience = true,
            ValidAudience = jwt["Audience"] ?? "MotoControl.Web",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// ─────────────────────────────────────────────────────────────
//  Errores de dominio → respuestas JSON con código adecuado
// ─────────────────────────────────────────────────────────────
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;

    var (status, mensaje) = error switch
    {
        ArgumentException => (StatusCodes.Status400BadRequest, error.Message),
        KeyNotFoundException => (StatusCodes.Status404NotFound, error.Message),
        UnauthorizedAccessException => (StatusCodes.Status403Forbidden, error.Message),
        InvalidOperationException => (StatusCodes.Status409Conflict, error.Message),
        _ => (StatusCodes.Status500InternalServerError, "Ocurrió un error interno en el servidor.")
    };

    if (status == StatusCodes.Status500InternalServerError)
        app.Logger.LogError(error, "Error no controlado en la API");

    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { error = mensaje });
}));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("web");
app.UseAuthentication();
app.UseAuthorization();

// ─────────────────────────────────────────────────────────────
//  Endpoints
// ─────────────────────────────────────────────────────────────
app.MapGet("/", () => Results.Ok(new
{
    aplicacion = "MotoControl API",
    estado = "ok",
    version = "1.0",
    hora = DateTimeOffset.Now
}));

app.MapGet("/api/health", async (Db db) =>
{
    await using var cn = await db.OpenAsync();
    var version = await Dapper.SqlMapper.ExecuteScalarAsync<string>(cn, "SELECT version();");
    return Results.Ok(new { estado = "ok", baseDatos = version });
}).AllowAnonymous();

app.MapAuthEndpoints();
app.MapAdminEndpoints();
app.MapGestorEndpoints();
app.MapReportesEndpoints();

// ─────────────────────────────────────────────────────────────
//  Arranque: esquema + usuario administrador inicial
// ─────────────────────────────────────────────────────────────
await app.Services.GetRequiredService<Db>().InitializeAsync();

app.Run();
