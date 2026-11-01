using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using MotoControl.Api.Data;
using MotoControl.Api.Endpoints;
using MotoControl.Api.Services;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// QuestPDF: licencia comunitaria (igual que la app de escritorio).
QuestPDF.Settings.License = LicenseType.Community;

// Los nombres o textos pueden traer emojis que la fuente Lato no incluye.
// Sin esto, QuestPDF 2026.9 lanza una excepción y el reporte devuelve 500.
QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;

// ─────────────────────────────────────────────────────────────
//  Servicios
// ─────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();
builder.Services.AddSingleton<Clock>();
builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<LoginThrottle>();
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

// Render está delante de la API: recuperar la IP real del cliente.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Límite de intentos de inicio de sesión por IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "anonimo",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Demasiados intentos de inicio de sesión. Espera unos minutos e intenta de nuevo."
        }, token);
    };
});

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

        // Si el tiempo de uso del gestor venció, el token deja de servir
        // aunque se haya emitido antes del vencimiento.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var valor = context.Principal?.FindFirst("accesoHasta")?.Value;
                if (!string.IsNullOrWhiteSpace(valor) &&
                    DateTime.TryParse(valor, CultureInfo.InvariantCulture,
                                      DateTimeStyles.RoundtripKind, out var fecha) &&
                    fecha.ToUniversalTime() < DateTime.UtcNow)
                {
                    context.Fail("El tiempo de uso del sistema venció.");
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsProduction())
    app.UseHsts();

// Cabeceras de seguridad en todas las respuestas de la API.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    await next();
});

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
app.UseRateLimiter();
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
