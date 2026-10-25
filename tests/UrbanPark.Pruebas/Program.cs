using Microsoft.Extensions.Configuration;
using UrbanPark.Api.Modelos;
using UrbanPark.Api.Servicios;

// ============================================================================
//  Pruebas de las reglas de negocio de Urban Park.
//  No necesitan MySQL: validan la lógica pura (cobro, placas, hash de
//  contraseñas, roles, Circuit Breaker y parseo de la respuesta de la IA).
//
//  Ejecutar:  dotnet run --project tests/UrbanPark.Pruebas
// ============================================================================

var fallos = 0;
var pruebas = 0;

void Comprobar(string nombre, bool condicion, string? detalle = null)
{
    pruebas++;
    if (condicion)
    {
        Console.WriteLine($"  OK   {nombre}");
    }
    else
    {
        fallos++;
        Console.WriteLine($"  FALLA {nombre}" + (detalle is null ? "" : $"  -> {detalle}"));
    }
}

Console.WriteLine("=============================================================");
Console.WriteLine(" PRUEBAS DE REGLAS DE NEGOCIO - URBAN PARK");
Console.WriteLine("=============================================================");

// ---------------------------------------------------------------- 1) cobro
Console.WriteLine("\n[1] Fórmula de cobro (mínimo 1 hora, horas hacia arriba, 2 decimales)");
var t0 = new DateTime(2025, 1, 1, 10, 0, 0);

Comprobar("1 minuto -> 1 hora ($3.50)", Reglas.CalcularMonto(t0, t0.AddMinutes(1), 3.5) == 3.5,
    Reglas.CalcularMonto(t0, t0.AddMinutes(1), 3.5).ToString());
Comprobar("30 minutos -> 1 hora ($3.50)", Reglas.CalcularMonto(t0, t0.AddMinutes(30), 3.5) == 3.5);
Comprobar("60 minutos exactos -> 1 hora ($3.50)", Reglas.CalcularMonto(t0, t0.AddMinutes(60), 3.5) == 3.5);
Comprobar("61 minutos -> 2 horas ($7.00)", Reglas.CalcularMonto(t0, t0.AddMinutes(61), 3.5) == 7.0,
    Reglas.CalcularMonto(t0, t0.AddMinutes(61), 3.5).ToString());
Comprobar("121 minutos -> 3 horas ($10.50)", Reglas.CalcularMonto(t0, t0.AddMinutes(121), 3.5) == 10.5,
    Reglas.CalcularMonto(t0, t0.AddMinutes(121), 3.5).ToString());
Comprobar("Tarifa 1.5 y 4 horas -> $6.00", Reglas.CalcularMonto(t0, t0.AddHours(4), 1.5) == 6.0);
Comprobar("Tarifa nula -> $0.00", Reglas.CalcularMonto(t0, t0.AddHours(2), null) == 0.0);
Comprobar("Sin salida (estimado en vivo) devuelve un valor > 0",
    Reglas.CalcularMonto(DateTime.Now.AddHours(-2), null, 3.0) > 0);

// ---------------------------------------------------------------- 2) placas
Console.WriteLine("\n[2] Validación de placas");
foreach (var valida in new[] { "ABC-123", "ABC-1234", "ABC-12345", "XYZ-789" })
    Comprobar($"acepta '{valida}'", Reglas.ValidarPlaca(valida, "x").ok);

foreach (var invalida in new[] { "", "   ", "A", "AB1", "ABC123", "AB12", "-ABC1", "AB@12", "ABC-123456" })
    Comprobar($"rechaza '{invalida}'", !Reglas.ValidarPlaca(invalida, "x").ok);

Comprobar("normaliza ' abc-123 ' -> 'ABC-123'",
    Reglas.NormalizarPlaca(" abc-123 ") == "ABC-123", Reglas.NormalizarPlaca(" abc-123 "));

// ---------------------------------------------------------------- 3) roles
Console.WriteLine("\n[3] Rol efectivo jerárquico (ADMIN > OPERADOR > CLIENTE)");
Comprobar("ADMIN+OPERADOR -> ADMIN", Reglas.RolPrincipal(["ADMIN", "OPERADOR"]) == "ADMIN");
Comprobar("OPERADOR+CLIENTE -> OPERADOR", Reglas.RolPrincipal(["CLIENTE", "OPERADOR"]) == "OPERADOR");
Comprobar("solo CLIENTE -> CLIENTE", Reglas.RolPrincipal(["CLIENTE"]) == "CLIENTE");
Comprobar("esClienteSolo(CLIENTE) = true", Reglas.EsClienteSolo(["CLIENTE"]));
Comprobar("esClienteSolo(ADMIN,OPERADOR) = false", !Reglas.EsClienteSolo(["ADMIN", "OPERADOR"]));
Comprobar("roles de app ordenados y filtrados",
    Reglas.RolesDeApp(["OPERADOR", "ADMIN", "OTRO"]).SequenceEqual(new[] { "ADMIN", "OPERADOR" }));

// ---------------------------------------------------------------- 4) contraseñas
Console.WriteLine("\n[4] Hash de contraseñas PBKDF2 y usuarios demo del script SQL");
var password = new PasswordService();
var hash = password.Hashear("cliente123");
Comprobar("hash nuevo verifica", password.Verificar("cliente123", hash));
Comprobar("hash nuevo rechaza otra clave", !password.Verificar("otra", hash));
Comprobar("formato pbkdf2$100000$...", hash.StartsWith("pbkdf2$100000$"), hash[..20]);

// Estos hashes son EXACTAMENTE los del script database/urbanpark_mysql.sql:
// si esta prueba pasa, los usuarios demo del script pueden iniciar sesión.
var demo = new (string Usuario, string Clave, string Hash)[]
{
    ("cliente1",  "cliente123",  "pbkdf2$100000$o6hSk1YMFbT2BuTMllvzuQ==$ALd83ChE1GMMVKUnpbvZzsThYYC4g6qfZTtUIy6oSYc="),
    ("cliente2",  "cliente123",  "pbkdf2$100000$v5xmFlu1zXeBH/spHUwiXg==$34+HJ5MGrFD+oljU8VtvFzDFWCb+zNacTGqtfge/RCo="),
    ("cliente3",  "cliente123",  "pbkdf2$100000$Ig2G8NZrBfcP9O+goVkPDw==$dXYlFhnuqtM60MxFnUoxQdHIaD60EZs6zOgOMICIucQ="),
    ("operador1", "operador123", "pbkdf2$100000$kR5h5MoFilXtf5rHS89Zag==$TAk/+Yn/8UWeJNhglWDMX7/t9bvArQjz3KuEwVlHOpQ="),
    ("admin1",    "admin123",    "pbkdf2$100000$a3a0iBbFN8gpz7sklLvvZw==$nXLVpZpIa2yh1U7X0UQa184FWJDz729hD89dZZE2lgA=")
};

foreach (var (usuario, clave, hashSql) in demo)
{
    Comprobar($"{usuario} / {clave} funciona con el hash del script SQL", password.Verificar(clave, hashSql));
    Comprobar($"{usuario} rechaza una clave incorrecta", !password.Verificar(clave + "x", hashSql));
}

// ---------------------------------------------------------------- 5) Resilience
Console.WriteLine("\n[5] Circuit Breaker + Retry (parámetros del original)");
var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Resiliencia:SlidingWindowSize"] = "10",
    ["Resiliencia:FailureRateThreshold"] = "50",
    ["Resiliencia:WaitDurationInOpenStateSegundos"] = "10",
    ["Resiliencia:PermittedCallsInHalfOpen"] = "3",
    ["Resiliencia:RetryMaxAttempts"] = "3",
    ["Resiliencia:RetryBaseMs"] = "10",
    ["Resiliencia:RetryMultiplicador"] = "2"
}).Build();

var resiliencia = new ResilienciaService(configuracion);
Comprobar("estado inicial CLOSED", resiliencia.Estado == "CLOSED");
Comprobar("3 intentos configurados", resiliencia.IntentosMaximos == 3);
Comprobar("ventana de 10", resiliencia.Ventana == 10);

var intentosFallidos = 0;
async Task<string> AccionQueFalla()
{
    intentosFallidos++;
    await Task.Yield();
    throw new EspaciosNoDisponibleException("conexión rechazada");
}
async Task<string> AccionQueFunciona()
{
    await Task.Yield();
    return "ocupacion-ok";
}

var conFallback = await resiliencia.EjecutarAsync(AccionQueFalla, _ => "FALLBACK");
Comprobar("el fallback se devuelve en vez de propagar la excepción", conFallback.Valor == "FALLBACK");
Comprobar("marca Fallback = true y trae mensaje", conFallback.Fallback && !string.IsNullOrWhiteSpace(conFallback.Mensaje));
Comprobar("se reintentó 3 veces (1 + 2 reintentos)", intentosFallidos == 3, $"intentos={intentosFallidos}");
Comprobar("tras 1 fallo el circuito sigue CLOSED", resiliencia.Estado == "CLOSED");

// 10 llamadas fallidas -> supera el 50% de la ventana -> abre
for (var i = 0; i < 10; i++)
    await resiliencia.EjecutarAsync(AccionQueFalla, _ => "FALLBACK");

Comprobar("tras 10 fallos el circuito ABRE", resiliencia.Estado == "OPEN", resiliencia.Estado);
Comprobar("con el circuito abierto no se llama a la acción",
    await LlamarSinTocar(resiliencia) == "FALLBACK");

async Task<string> LlamarSinTocar(ResilienciaService r)
{
    var antes = intentosFallidos;
    var res = await r.EjecutarAsync(AccionQueFalla, _ => "FALLBACK");
    Comprobar("  (la acción no se ejecutó: se rechazó de inmediato)", intentosFallidos == antes);
    return res.Valor;
}

resiliencia.Reiniciar();
Comprobar("reiniciar deja el circuito CLOSED y la ventana vacía",
    resiliencia.Estado == "CLOSED" && resiliencia.LlamadasEnVentana == 0);

var exito = await resiliencia.EjecutarAsync(AccionQueFunciona, _ => "FALLBACK");
Comprobar("una llamada correcta no usa fallback", !exito.Fallback && exito.Valor == "ocupacion-ok");

// ---------------------------------------------------------------- 6) IA
Console.WriteLine("\n[6] Parseo de la respuesta de la IA (formato JSON del prompt)");
Comprobar("extrae el objeto JSON de un texto con rodeos",
    DeepSeekService.ExtraerObjetoJson("claro: {\"codigo\":\"A-01\",\"motivo\":\"ok\"} listo")
        == "{\"codigo\":\"A-01\",\"motivo\":\"ok\"}");
Comprobar("acepta un JSON limpio", DeepSeekService.ExtraerObjetoJson("{\"codigo\":\"B-02\"}") == "{\"codigo\":\"B-02\"}");
Comprobar("devuelve null si no hay JSON", DeepSeekService.ExtraerObjetoJson("sin json") is null);
Comprobar("lee el campo codigo",
    DeepSeekService.LeerTexto("{\"codigo\":\"A-01\",\"motivo\":\"cerca del ascensor\"}", "codigo") == "A-01");
Comprobar("lee el campo motivo",
    DeepSeekService.LeerTexto("{\"codigo\":\"A-01\",\"motivo\":\"cerca del ascensor\"}", "motivo") == "cerca del ascensor");
Comprobar("devuelve null si el campo no existe", DeepSeekService.LeerTexto("{\"codigo\":\"A-01\"}", "motivo") is null);
Comprobar("ignora campos extra y el orden",
    DeepSeekService.LeerTexto("{\"motivo\":\"m\",\"codigo\":\"C-03\",\"extra\":1}", "codigo") == "C-03");

// ---------------------------------------------------------------- 7) autenticador de Google
Console.WriteLine("\n[7] Autenticador de Google (TOTP)");
// Vectores oficiales del RFC 6238 (SHA1, secreto ASCII "12345678901234567890").
var secretoRfc = ServicioTotp.Base32(System.Text.Encoding.ASCII.GetBytes("12345678901234567890"));
Comprobar("el secreto del RFC se codifica en base32",
    secretoRfc == "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", secretoRfc);
Comprobar("T=59 s -> 287082",
    ServicioTotp.Codigo(secretoRfc, DateTimeOffset.FromUnixTimeSeconds(59).UtcDateTime) == "287082",
    ServicioTotp.Codigo(secretoRfc, DateTimeOffset.FromUnixTimeSeconds(59).UtcDateTime));
Comprobar("T=1111111109 s -> 081804",
    ServicioTotp.Codigo(secretoRfc, DateTimeOffset.FromUnixTimeSeconds(1111111109).UtcDateTime) == "081804");
Comprobar("T=1234567890 s -> 005924",
    ServicioTotp.Codigo(secretoRfc, DateTimeOffset.FromUnixTimeSeconds(1234567890).UtcDateTime) == "005924");
Comprobar("T=2000000000 s -> 279037",
    ServicioTotp.Codigo(secretoRfc, DateTimeOffset.FromUnixTimeSeconds(2000000000).UtcDateTime) == "279037");

var momentoPrueba = DateTime.UtcNow;
var codigoPrueba = ServicioTotp.Codigo(secretoRfc, momentoPrueba);
Comprobar("acepta el código del momento", ServicioTotp.Verificar(secretoRfc, codigoPrueba, momentoPrueba));
Comprobar("acepta el código del paso anterior (reloj atrasado)",
    ServicioTotp.Verificar(secretoRfc, ServicioTotp.Codigo(secretoRfc, momentoPrueba.AddSeconds(-30)), momentoPrueba));
Comprobar("rechaza un código de hace 5 minutos",
    !ServicioTotp.Verificar(secretoRfc, ServicioTotp.Codigo(secretoRfc, momentoPrueba.AddMinutes(-5)), momentoPrueba));
Comprobar("rechaza códigos que no son de 6 dígitos", !ServicioTotp.Verificar(secretoRfc, "12345", momentoPrueba));
Comprobar("rechaza un secreto vacío", !ServicioTotp.Verificar(null, codigoPrueba, momentoPrueba));

var secretoNuevo = ServicioTotp.NuevoSecreto();
Comprobar("el secreto nuevo tiene 32 caracteres base32", secretoNuevo.Length == 32, secretoNuevo);
Comprobar("dos secretos nuevos son distintos", secretoNuevo != ServicioTotp.NuevoSecreto());
Comprobar("el secreto nuevo sirve para generar códigos",
    ServicioTotp.Verificar(secretoNuevo, ServicioTotp.Codigo(secretoNuevo, DateTime.UtcNow)));
Comprobar("la URI de alta la entiende Google Authenticator",
    ServicioTotp.UriProvisioning("cliente1", secretoNuevo).StartsWith("otpauth://totp/") &&
    ServicioTotp.UriProvisioning("cliente1", secretoNuevo).Contains($"secret={secretoNuevo}") &&
    ServicioTotp.UriProvisioning("cliente1", secretoNuevo).Contains("issuer="));
Comprobar("decodifica el base32 con espacios y minúsculas",
    ServicioTotp.Base32ADecodificar("gezd gnbv gy3t qojq").SequenceEqual(
        System.Text.Encoding.ASCII.GetBytes("1234567890")));
Console.WriteLine("\n=============================================================");
Console.WriteLine($" RESULTADO: {pruebas - fallos}/{pruebas} pruebas correctas" + (fallos == 0 ? "  ✓ TODO OK" : $"  ({fallos} FALLOS)"));
Console.WriteLine("=============================================================");

return fallos == 0 ? 0 : 1;
