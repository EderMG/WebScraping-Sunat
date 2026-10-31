using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SunatXmlApi;
using SunatXmlApi.Configuration;
using SunatXmlApi.Data;
using SunatXmlApi.Logging;
using SunatXmlApi.Services;

var builder = WebApplication.CreateBuilder(args);

var logDirectory = Path.GetFullPath(
    builder.Configuration["Logging:File:Directory"] ?? "Logs",
    builder.Environment.ContentRootPath);
using var fileLoggerProvider = new FileLoggerProvider(logDirectory);
builder.Logging.AddProvider(fileLoggerProvider);
using var startupLoggerFactory = LoggerFactory.Create(logging => logging.AddProvider(fileLoggerProvider));
var startupLogger = startupLoggerFactory.CreateLogger("Startup");

try
{

var scrapingConnectionString = builder.Configuration.GetConnectionString("Scraping");
if (string.IsNullOrWhiteSpace(scrapingConnectionString))
{
    throw new InvalidOperationException(
        "Falta la cadena de conexión ConnectionStrings:Scraping.");
}

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Falta la configuración JWT.");

if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey debe contener al menos 32 bytes.");
}

builder.Services.Configure<SunatOptions>(
    builder.Configuration.GetSection(SunatOptions.SectionName));
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddDbContext<ScrapingDbContext>(options =>
    options.UseSqlServer(scrapingConnectionString));
builder.Services.AddScoped<IApiCredentialValidator, DatabaseApiCredentialValidator>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<ISunatXmlService, SunatXmlService>();

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                context.HandleResponse();
                return ApiErrors.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    "TOKEN_INVALIDO",
                    "El token de acceso es inválido o ha expirado.");
            },
            OnForbidden = context => ApiErrors.WriteAsync(
                context.HttpContext,
                StatusCodes.Status403Forbidden,
                "ACCESO_DENEGADO",
                "No tiene permiso para acceder a este recurso.")
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();
app.Logger.LogInformation("Los logs se guardan en {LogDirectory} (archivos diarios UTC).", logDirectory);

app.UseMiddleware<ApiExceptionMiddleware>();
app.UseStatusCodePages(async statusContext =>
{
    var context = statusContext.HttpContext;
    var (code, message) = context.Response.StatusCode switch
    {
        StatusCodes.Status404NotFound => ("RECURSO_NO_ENCONTRADO", "El recurso solicitado no existe."),
        StatusCodes.Status405MethodNotAllowed => ("METODO_NO_PERMITIDO", "El método HTTP no está permitido."),
        _ => ("SOLICITUD_NO_PROCESADA", "La solicitud no pudo ser procesada.")
    };

    await ApiErrors.WriteAsync(context, context.Response.StatusCode, code, message);
});
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/auth/token", async (
    TokenRequest? request,
    IApiCredentialValidator credentialValidator,
    JwtOptions jwt,
    CancellationToken cancellationToken) =>
{
    if (request is null ||
        string.IsNullOrWhiteSpace(request.Usuario) ||
        string.IsNullOrWhiteSpace(request.Contrasena))
    {
        throw new ApiException(
            StatusCodes.Status400BadRequest,
            "SOLICITUD_INVALIDA",
            "Debe enviar usuario y contraseña.");
    }

    if (!await credentialValidator.IsValidAsync(
            request.Usuario,
            request.Contrasena,
            cancellationToken))
    {
        throw new ApiException(
            StatusCodes.Status401Unauthorized,
            "CREDENCIALES_INVALIDAS",
            "Usuario o contraseña inválidos.");
    }

    var now = DateTime.UtcNow;
    var expiresAt = now.AddMinutes(jwt.ExpirationMinutes);
    var credentials = new SigningCredentials(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        SecurityAlgorithms.HmacSha256);
    var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
        issuer: jwt.Issuer,
        audience: jwt.Audience,
        claims:
        [
            new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.NameIdentifier,
                request.Usuario)
        ],
        notBefore: now,
        expires: expiresAt,
        signingCredentials: credentials);

    var encodedToken = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new TokenResponse(encodedToken, "Bearer", expiresAt));
})
.AllowAnonymous();

app.MapPost("/api/comprobantes/xml", async (
    XmlRequest? request,
    HttpRequest httpRequest,
    ISunatXmlService sunatXmlService,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        throw new ApiException(
            StatusCodes.Status400BadRequest,
            "SOLICITUD_INVALIDA",
            "Debe enviar los datos del comprobante.");
    }

    var rucEmisor = httpRequest.Headers["X-Ruc-Emisor"].FirstOrDefault();
    var usuarioSol = httpRequest.Headers["X-Usuario-Sol"].FirstOrDefault();
    var claveSol = httpRequest.Headers["X-Clave-Sol"].FirstOrDefault();

    ValidateXmlRequest(request, rucEmisor, usuarioSol, claveSol);

    var result = await sunatXmlService.DownloadAsync(
        new SunatCredentials(rucEmisor!, usuarioSol!, claveSol!),
        request,
        cancellationToken);

    return Results.Ok(result);
})
.RequireAuthorization();

app.Run();
}
catch (Exception exception)
{
    startupLogger.LogCritical(exception, "La aplicación terminó inesperadamente o no pudo iniciar.");
    throw;
}

static void ValidateXmlRequest(
    XmlRequest request,
    string? rucEmisor,
    string? usuarioSol,
    string? claveSol)
{
    if (!IsRuc(rucEmisor) || string.IsNullOrWhiteSpace(usuarioSol) || string.IsNullOrWhiteSpace(claveSol))
    {
        throw new ApiException(
            StatusCodes.Status400BadRequest,
            "CABECERAS_INVALIDAS",
            "Envíe X-Ruc-Emisor (11 dígitos), X-Usuario-Sol y X-Clave-Sol.");
    }

    if (!IsRuc(request.RucComprobante) ||
        string.IsNullOrWhiteSpace(request.Tipo) ||
        request.Tipo.Length != 2 ||
        !request.Tipo.All(char.IsDigit) ||
        string.IsNullOrWhiteSpace(request.Serie) ||
        request.Serie.Length > 20 ||
        string.IsNullOrWhiteSpace(request.Numero) ||
        request.Numero.Length > 20 ||
        !request.Numero.All(char.IsDigit) ||
        request.TipoServicio != "01")
    {
        throw new ApiException(
            StatusCodes.Status400BadRequest,
            "DATOS_COMPROBANTE_INVALIDOS",
            "RUCComprobante debe tener 11 dígitos; Tipo, Serie y Numero deben ser válidos; TipoServicio debe ser 01.");
    }
}

static bool IsRuc(string? value) =>
    value is { Length: 11 } && value.All(char.IsDigit);

public partial class Program;
