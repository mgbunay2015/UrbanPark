using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.OpenApi.Models;
using UrbanPark.Api.Data;
using UrbanPark.Api.Modelos;
using UrbanPark.Api.Repositorios;
using UrbanPark.Api.Servicios;

// Se fija el "content root" a la carpeta del ejecutable para que appsettings.json se encuentre
// siempre, aunque se arranque el .exe desde otro directorio de trabajo.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// ------------------------------------------------------------------ validacion temprana
// Si falta la clave JWT, la autenticacion fallaria en cada peticion con un error 500 confuso.
// Es mejor avisar al arrancar con un mensaje claro y no levantar el servidor.
var claveJwt = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(claveJwt) || claveJwt.Length < 32)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("=============================================================");
    Console.Error.WriteLine(" ERROR DE CONFIGURACIÓN: falta Jwt:Key (o tiene menos de 32 caracteres).");
    Console.Error.WriteLine("=============================================================");
    Console.Error.WriteLine($" Archivo revisado: {Path.Combine(AppContext.BaseDirectory, "appsettings.json")}");
    Console.Error.WriteLine(" Edita UrbanPark.Api/appsettings.json y pon una clave larga y secreta, por ejemplo:");
    Console.Error.WriteLine("   \"Jwt\": { \"Key\": \"clave_secreta_de_urban_park_2026_con_mas_de_32_caracteres\" }");
    Console.Error.WriteLine();
    return 1;
}

// ------------------------------------------------------------------ registro de logs
// Solo consola: el proveedor de EventLog de Windows falla sin permisos de administrador
// y al escribir un error puede tumbar toda la aplicación.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
#if DEBUG
builder.Logging.AddDebug();
#endif

// ------------------------------------------------------------------ servicios
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(opciones =>
{
    opciones.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Urban Park API",
        Version = "v1",
        Description = "API REST del sistema de estacionamiento Urban Park (versión móvil .NET MAUI + MySQL + IA DeepSeek)."
    });

    opciones.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Pega el accessToken que devuelve /api/auth/login (sin la palabra Bearer)."
    });

    opciones.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// ------------------------------------------------------------------ autenticacion JWT propia
var tokenService = new TokenService(builder.Configuration);
builder.Services.AddSingleton(tokenService);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opciones =>
    {
        opciones.MapInboundClaims = false;                 // se conservan los nombres de claim originales
        opciones.TokenValidationParameters = tokenService.ParametrosValidacion();

        // Respuestas 401/403 con el mismo formato de error del backend original.
        opciones.Events = new JwtBearerEvents
        {
            OnChallenge = async contexto =>
            {
                contexto.HandleResponse();
                contexto.Response.StatusCode = 401;
                contexto.Response.ContentType = "application/json";
                await contexto.Response.WriteAsync(JsonSerializer.Serialize(new ErrorResponse(
                    DateTime.Now, 401, "Unauthorized",
                    "No autenticado: falta el token o ha expirado.", contexto.Request.Path)));
            },
            OnForbidden = async contexto =>
            {
                contexto.Response.StatusCode = 403;
                contexto.Response.ContentType = "application/json";
                await contexto.Response.WriteAsync(JsonSerializer.Serialize(new ErrorResponse(
                    DateTime.Now, 403, "Forbidden",
                    "No tienes permisos para esta operación con tu perfil.", contexto.Request.Path)));
            }
        };
    });

builder.Services.AddAuthorization();

// ------------------------------------------------------------------ datos y servicios de dominio
builder.Services.AddSingleton<IFabricaConexion, FabricaConexion>();
builder.Services.AddScoped<UsuarioRepositorio>();
builder.Services.AddScoped<SedeRepositorio>();
builder.Services.AddScoped<EspacioRepositorio>();
builder.Services.AddScoped<VehiculoRepositorio>();
builder.Services.AddScoped<TicketRepositorio>();
builder.Services.AddScoped<ReporteBalanceRepositorio>();
builder.Services.AddScoped<BiometriaRepositorio>();
builder.Services.AddScoped<DispositivoRepositorio>();

builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<GoogleAuthService>();
builder.Services.AddSingleton<EstadoDemoService>();
builder.Services.AddSingleton<ResilienciaService>();

builder.Services.AddScoped<ServicioEspacios>();
builder.Services.AddScoped<ServicioTickets>();
builder.Services.AddScoped<ServicioSedes>();
builder.Services.AddScoped<ServicioEstacionamiento>();
builder.Services.AddHttpClient<DeepSeekService>(cliente => cliente.Timeout = TimeSpan.FromSeconds(180));

var app = builder.Build();

// ------------------------------------------------------------------ pipeline
// El manejador global de errores va PRIMERO para poder capturar lo que ocurra despues.
app.UseExceptionHandler(manejador => manejador.Run(async contexto =>
{
    var excepcion = contexto.Features.Get<IExceptionHandlerFeature>()?.Error;
    contexto.Response.StatusCode = 500;
    contexto.Response.ContentType = "application/json";
    await contexto.Response.WriteAsync(JsonSerializer.Serialize(new ErrorResponse(
        DateTime.Now, 500, "Internal Server Error",
        excepcion?.Message ?? "Error inesperado en el servidor.", contexto.Request.Path)));
}));

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Urban Park API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Documento de bienvenida en la raiz (util para comprobar que la API responde).
app.MapGet("/", () => Results.Ok(new
{
    servicio = "Urban Park API",
    version = "1.0 (móvil .NET MAUI + MySQL + DeepSeek)",
    swagger = "/swagger",
    salud = "/api/ia/salud",
    instalar = "/urbanpark.apk",
    nota = "Los endpoints están bajo /api/... y requieren JWT salvo login, registro, google y salud."
}));

app.MapGet("/urbanpark.apk", () =>
{
    var ruta = Path.Combine(AppContext.BaseDirectory, "urbanpark.apk");
    return File.Exists(ruta)
        ? Results.File(ruta, "application/vnd.android.package-archive", "urbanpark.apk")
        : Results.NotFound("Todavía no está el instalador de Android.");
});

// ------------------------------------------------------------------ base de datos: esquema + semillas
// Si MySQL no responde, la API sigue arrancando y lo informa: asi se puede abrir /swagger y ver el mensaje.
using (var alcance = app.Services.CreateScope())
{
    var registro = alcance.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await InicializadorDb.InicializarAsync(app.Configuration, registro);
        registro.LogInformation("Base de datos lista. Revisa /swagger para probar la API.");
    }
    catch (Exception ex)
    {
        registro.LogError("No se pudo preparar MySQL: {Motivo}", ex.Message);
        registro.LogWarning("La API seguirá en marcha, pero los endpoints que usan base de datos fallarán. " +
                            "Revisa ConnectionStrings:UrbanPark en appsettings.json y que el servidor MySQL esté encendido.");
    }
}

app.Run();
return 0;
