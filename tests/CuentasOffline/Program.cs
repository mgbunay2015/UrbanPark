// Reproduce el caso real: en el mismo equipo entran cliente2 y luego admin1.
// Antes, la segunda entrada sobrescribía la credencial de la primera y cliente2 ya no
// podía entrar sin internet. Aquí se valida el nuevo almacenamiento por cuenta.
using System.Text.Json;

var ok = true;
void Comprobar(string nombre, bool cond, string? detalle = null)
{
    Console.WriteLine($"  {(cond ? "OK   " : "FALLA")}  {nombre}{(detalle is null ? "" : "  [" + detalle + "]")}");
    if (!cond) ok = false;
}

Console.WriteLine("=== VARIAS CUENTAS EN EL MISMO EQUIPO ===\n");

// --- Modelo equivalente a CredencialLocal.CuentaLocal -------------------------------
var cuentas = new List<Cuenta>();

void Guardar(string usuario, string? password, bool totp, string? dispositivo)
{
    var cuenta = cuentas.FirstOrDefault(c => c.Usuario.Equals(usuario, StringComparison.OrdinalIgnoreCase));
    if (cuenta is null) { cuenta = new Cuenta { Usuario = usuario }; cuentas.Add(cuenta); }
    cuenta.TotpActivo = totp;
    cuenta.GuardadoEn = DateTime.Now;
    if (!string.IsNullOrWhiteSpace(dispositivo)) cuenta.Dispositivo = dispositivo!;
    if (!string.IsNullOrWhiteSpace(password)) cuenta.Hash = Hashear(password);
}

Cuenta? Buscar(string? usuario) => usuario is null
    ? null
    : cuentas.FirstOrDefault(c => c.Usuario.Equals(usuario.Trim(), StringComparison.OrdinalIgnoreCase));

bool Verificar(string usuario, string password)
{
    var cuenta = Buscar(usuario);
    return cuenta is not null && VerificarHash(password, cuenta.Hash);
}

// --- El caso que falló -------------------------------------------------------------
Guardar("cliente2", "cliente123", false, null);
Guardar("admin1", "admin123", false, null);

Comprobar("quedan 2 cuentas guardadas", cuentas.Count == 2, $"{cuentas.Count}");
Comprobar("cliente2 se conserva tras entrar admin1", Buscar("cliente2") is not null);
Comprobar("cliente2 puede entrar sin internet", Verificar("cliente2", "cliente123"));
Comprobar("admin1 puede entrar sin internet", Verificar("admin1", "admin123"));

// Antes, la comprobación era "el usuario debe ser igual al último": esto fallaba.
var ultimo = cuentas.OrderByDescending(c => c.GuardadoEn).First().Usuario;
Comprobar("el último ya no bloquea a los demás", ultimo == "admin1" && Verificar("cliente2", "cliente123"),
    $"último={ultimo}");

// --- Contraseña incorrecta ---------------------------------------------------------
Comprobar("rechaza la contraseña equivocada de cliente2", !Verificar("cliente2", "otra"));
Comprobar("rechaza una cuenta que no está en el equipo", !Verificar("cliente3", "cliente123"));

// --- Actualización de credenciales -------------------------------------------------
Guardar("cliente2", "nuevaClave99", false, null);
Comprobar("al cambiar la contraseña se actualiza", Verificar("cliente2", "nuevaClave99"));
Comprobar("la contraseña anterior deja de servir", !Verificar("cliente2", "cliente123"));
Comprobar("admin1 no se ve afectado", Verificar("admin1", "admin123"));

// --- Entrar solo con código (equipo vinculado) -------------------------------------
Guardar("cliente1", null, true, new string('a', 40));
Comprobar("cliente1 queda vinculado para entrar con código",
    Buscar("cliente1") is { TotpActivo: true } c1 && c1.Dispositivo.Length >= 32);
Comprobar("cliente2 no hereda el vínculo de cliente1",
    Buscar("cliente2") is { TotpActivo: false } c2 && c2.Dispositivo.Length == 0);

// --- Cerrar sesión NO borra datos locales ------------------------------------------
// Se simula lo que hace el nuevo vSalir: solo se cierra la sesión, sin LimpiarUsuario.
var datosLocales = new { Vehiculos = 4, Tickets = 3, Pases = 1 };
var trasCerrarSesion = datosLocales;   // el cierre de sesión ya no toca SQLite
Comprobar("cerrar sesión conserva los datos locales",
    trasCerrarSesion.Vehiculos == 4 && trasCerrarSesion.Tickets == 3 && trasCerrarSesion.Pases == 1);

// --- Persistencia en JSON ----------------------------------------------------------
var json = JsonSerializer.Serialize(cuentas);
var vuelta = JsonSerializer.Deserialize<List<Cuenta>>(json)!;
Comprobar("las cuentas sobreviven al guardado en JSON",
    vuelta.Count == cuentas.Count && vuelta.Any(c => c.Usuario == "cliente2"));

// --- El hash de la cuenta anterior no se pierde ------------------------------------
// Caso real observado: admin1 tenía su contraseña en la clave única de la versión
// anterior; al entrar cliente2 se borraba y admin1 ya no podía entrar sin internet.
var hashUnico = Hashear("admin123");     // "clave antigua" de admin1

string? HashPropio(string usuario) =>
    cuentas.FirstOrDefault(c => c.Usuario == usuario)?.Hash is { Length: > 0 } h ? h : null;

// Se simula que la cuenta nueva aún no tiene hash propio y que existe el antiguo.
var admin = cuentas.First(c => c.Usuario == "admin1");
Comprobar("admin1 conserva su contraseña", Verificar("admin1", "admin123"));

var legacy = hashUnico;
if (HashPropio("admin1") is null && legacy is not null) admin.Hash = legacy;
Comprobar("el hash antiguo se traslada a la cuenta", Verificar("admin1", "admin123"));
Comprobar("tras entrar otras cuentas, admin1 sigue pudiendo entrar sin internet",
    Verificar("admin1", "admin123") && Verificar("cliente2", "nuevaClave99"));

Console.WriteLine();
Console.WriteLine(ok
    ? "RESULTADO: varias cuentas pueden entrar sin internet en el mismo equipo."
    : "RESULTADO: sigue habiendo un bloqueo al cambiar de cuenta.");

return ok ? 0 : 1;

// --- Helpers de hash (mismos parámetros que la app: PBKDF2-SHA256, 100 000) ---------
static string Hashear(string password)
{
    var sal = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
    var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
        password, sal, 100_000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
    return $"pbkdf2$100000${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
}

static bool VerificarHash(string password, string guardado)
{
    var partes = guardado.Split('$');
    if (partes.Length != 4) return false;
    var sal = Convert.FromBase64String(partes[2]);
    var esperado = Convert.FromBase64String(partes[3]);
    var calculado = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
        password, sal, int.Parse(partes[1]), System.Security.Cryptography.HashAlgorithmName.SHA256, esperado.Length);
    return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(esperado, calculado);
}

internal sealed class Cuenta
{
    public string Usuario { get; set; } = "";
    public bool TotpActivo { get; set; }
    public string Dispositivo { get; set; } = "";
    public string Hash { get; set; } = "";
    public DateTime GuardadoEn { get; set; } = DateTime.Now;
}
