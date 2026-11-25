using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UrbanPark.App.Modelos;

namespace UrbanPark.App.Servicios;

/// <summary>
/// Credenciales guardadas en el equipo para poder entrar sin internet.
///
/// Guarda <b>una entrada por cuenta</b>, no solo la última: antes, al entrar con otra cuenta
/// se sobrescribía la anterior y esa persona ya no podía entrar sin conexión (la app solo
/// recordaba a quien había entrado por última vez).
///
/// La contraseña nunca se guarda: se guarda un hash PBKDF2-SHA256 con sal, en SecureStorage.
/// </summary>
public static class CredencialLocal
{
    private const string ClaveCuentas = "urbanpark.credencial.cuentas";
    private const string ClaveUltimo = "urbanpark.credencial.usuario";
    private const string ClaveSesionVieja = "urbanpark.credencial.sesion";
    private const string ClaveTotpVieja = "urbanpark.credencial.totp";
    private const string ClaveDispositivoViejo = "urbanpark.credencial.dispositivo";
    private const string ClaveHashVieja = "urbanpark.credencial.hash";
    private const int Iteraciones = 100_000;

    /// <summary>Cuenta recordada en este equipo.</summary>
    public sealed class CuentaLocal
    {
        public string Usuario { get; set; } = "";
        public bool TotpActivo { get; set; }
        public string Dispositivo { get; set; } = "";
        public DateTime GuardadoEn { get; set; } = DateTime.Now;
    }

    // ================================================================ listado de cuentas
    /// <summary>Todas las cuentas recordadas, de la más reciente a la más antigua.</summary>
    public static IReadOnlyList<CuentaLocal> Cuentas
    {
        get
        {
            var lista = LeerCuentas();
            if (lista.Count > 0) return lista;

            // Equipos que venían de la versión de una sola cuenta: se migra esa entrada.
            var migrada = MigrarCuentaUnica();
            return migrada.Count > 0 ? migrada : [];
        }
    }

    /// <summary>Última cuenta que entró (la que se propone en el login).</summary>
    public static string Usuario => Preferences.Default.Get(ClaveUltimo, "");

    /// <summary>Datos guardados de una cuenta concreta.</summary>
    public static CuentaLocal? Cuenta(string? usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return null;
        return Cuentas.FirstOrDefault(c =>
            string.Equals(c.Usuario, usuario.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Hay al menos una cuenta guardada: se puede entrar sin internet.</summary>
    public static bool HayCuentas => Cuentas.Count > 0;

    /// <summary>True si esa cuenta tiene el autenticador de Google activo.</summary>
    public static bool TotpActivo => Cuenta(Usuario)?.TotpActivo ?? false;

    /// <summary>Token del equipo vinculado de la última cuenta.</summary>
    public static string Dispositivo => Cuenta(Usuario)?.Dispositivo ?? "";

    /// <summary>Token del equipo vinculado de una cuenta concreta.</summary>
    public static string DispositivoDe(string? usuario) => Cuenta(usuario)?.Dispositivo ?? "";

    /// <summary>La última cuenta puede entrar solo con el código del autenticador.</summary>
    public static bool PuedeEntrarConCodigo => PuedeEntrarConCodigoDe(Usuario);

    /// <summary>Esa cuenta puede entrar solo con el código del autenticador.</summary>
    public static bool PuedeEntrarConCodigoDe(string? usuario)
    {
        var cuenta = Cuenta(usuario);
        return cuenta is not null && cuenta.TotpActivo && cuenta.Dispositivo.Length >= 32;
    }

    // ================================================================ credencial guardada
    /// <summary>Hay una contraseña guardada para la última cuenta.</summary>
    public static Task<bool> TieneCredencialAsync() => TieneCredencialAsync(Usuario);

    /// <summary>Hay una contraseña guardada para esa cuenta.</summary>
    public static async Task<bool> TieneCredencialAsync(string? usuario)
    {
        var cuenta = Cuenta(usuario);
        if (cuenta is null) return false;
        return !string.IsNullOrEmpty(await HashGuardadoAsync(cuenta.Usuario));
    }

    /// <summary>
    /// Guarda lo necesario tras un login correcto: la cuenta pasa a ser la más reciente y,
    /// si se conoce la contraseña, queda el hash para poder entrar sin internet.
    /// </summary>
    public static async Task GuardarAsync(string usuario, string? password, bool totpActivo, string? dispositivo)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return;
        usuario = usuario.Trim();

        // Antes de cambiar la cuenta recordada se rescata el hash de la versión anterior,
        // que estaba en una sola clave y pertenecía a la última cuenta que entró.
        var ultimoAnterior = Preferences.Default.Get(ClaveUltimo, "");

        var lista = LeerCuentas();
        var cuenta = lista.FirstOrDefault(c => string.Equals(c.Usuario, usuario, StringComparison.OrdinalIgnoreCase));

        if (cuenta is null)
        {
            cuenta = new CuentaLocal { Usuario = usuario };
            lista.Add(cuenta);
        }

        cuenta.TotpActivo = totpActivo;
        cuenta.GuardadoEn = DateTime.Now;
        if (!string.IsNullOrWhiteSpace(dispositivo))
            cuenta.Dispositivo = dispositivo!;

        GuardarCuentas(lista);
        Preferences.Default.Set(ClaveUltimo, cuenta.Usuario);

        // Se conserva la contraseña de la cuenta anterior en su propia clave.
        await AsegurarHashPropioAsync(ultimoAnterior);

        if (string.IsNullOrEmpty(password)) return;

        try
        {
            await SecureStorage.Default.SetAsync(ClaveHash(cuenta.Usuario), Hashear(password));
        }
        catch (Exception)
        {
            // sin SecureStorage el ingreso sin internet no estará disponible para esa cuenta
            Registro.Aviso("Credencial", $"No se pudo guardar la contraseña de {cuenta.Usuario} en SecureStorage.");
        }
    }

    /// <summary>Comprueba usuario y contraseña contra lo guardado (para entrar sin internet).</summary>
    public static async Task<bool> VerificarAsync(string usuario, string password)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrEmpty(password)) return false;

        var cuenta = Cuenta(usuario);
        if (cuenta is null) return false;

        var guardado = await HashGuardadoAsync(cuenta.Usuario);
        return !string.IsNullOrWhiteSpace(guardado) && VerificarHash(password, guardado!);
    }

    /// <summary>Marca si la última cuenta usa autenticador (se consulta al API y se cachea).</summary>
    public static void ActualizarTotp(bool activo, string? dispositivo = null) =>
        ActualizarTotp(Usuario, activo, dispositivo);

    /// <summary>Marca si esa cuenta usa autenticador.</summary>
    public static void ActualizarTotp(string? usuario, bool activo, string? dispositivo = null)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return;

        var lista = LeerCuentas();
        var cuenta = lista.FirstOrDefault(c => string.Equals(c.Usuario, usuario, StringComparison.OrdinalIgnoreCase));
        if (cuenta is null)
        {
            cuenta = new CuentaLocal { Usuario = usuario.Trim() };
            lista.Add(cuenta);
        }

        cuenta.TotpActivo = activo;
        if (!string.IsNullOrWhiteSpace(dispositivo))
            cuenta.Dispositivo = dispositivo!;

        GuardarCuentas(lista);
    }

    /// <summary>El equipo ya no está vinculado: se olvida el token de la última cuenta.</summary>
    public static void OlvidarDispositivo() => OlvidarDispositivo(Usuario);

    /// <summary>El equipo ya no está vinculado: se olvida el token de esa cuenta.</summary>
    public static void OlvidarDispositivo(string? usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return;

        var lista = LeerCuentas();
        var cuenta = lista.FirstOrDefault(c => string.Equals(c.Usuario, usuario, StringComparison.OrdinalIgnoreCase));
        if (cuenta is null) return;

        cuenta.Dispositivo = "";
        GuardarCuentas(lista);
    }

    // ================================================================ sesión sin internet
    /// <summary>Guarda una copia de la sesión de esa cuenta para poder abrir la app sin internet.</summary>
    public static void GuardarSesionOffline(SesionDto sesion)
    {
        if (sesion is null || string.IsNullOrWhiteSpace(sesion.Username)) return;
        try
        {
            Preferences.Default.Set(ClaveSesion(sesion.Username),
                JsonSerializer.Serialize(sesion, JsonUrbanPark.Opciones));
        }
        catch (Exception ex)
        {
            Registro.Aviso("Credencial", $"No se pudo guardar la sesión sin conexión de {sesion.Username}: {ex.Message}");
        }
    }

    /// <summary>Sesión guardada de la última cuenta.</summary>
    public static SesionDto? SesionOffline() => SesionOffline(Usuario);

    /// <summary>
    /// Reconstruye la sesión guardada de una cuenta. El token de acceso se sustituye por uno
    /// de relleno, pero el <b>refresh token se conserva</b>: al volver la red con él se obtiene
    /// una sesión real sin pedir la contraseña otra vez.
    /// </summary>
    public static SesionDto? SesionOffline(string? usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return null;

        var json = Preferences.Default.Get(ClaveSesion(usuario), "");

        // Compatibilidad: la versión anterior guardaba una sola sesión sin distinguir cuenta.
        if (string.IsNullOrWhiteSpace(json) &&
            string.Equals(usuario, Usuario, StringComparison.OrdinalIgnoreCase))
            json = Preferences.Default.Get(ClaveSesionVieja, "");

        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var sesion = JsonSerializer.Deserialize<SesionDto>(json, JsonUrbanPark.Opciones);
            if (sesion is null || string.IsNullOrWhiteSpace(sesion.Username)) return null;

            // Se conserva RefreshToken y ExpiresAt originales.
            sesion.AccessToken = "sin-conexion";
            if (string.IsNullOrWhiteSpace(sesion.RefreshToken)) sesion.RefreshToken = "";
            return sesion;
        }
        catch (Exception ex)
        {
            Registro.Aviso("Credencial", $"La sesión guardada de {usuario} estaba corrupta: {ex.Message}");
            return null;
        }
    }

    // ================================================================ borrado
    /// <summary>Olvida una cuenta concreta (su contraseña, su token y su sesión guardada).</summary>
    public static void Limpiar(string? usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return;

        var lista = LeerCuentas();
        var cuenta = lista.FirstOrDefault(c => string.Equals(c.Usuario, usuario, StringComparison.OrdinalIgnoreCase));
        if (cuenta is not null)
        {
            lista.Remove(cuenta);
            GuardarCuentas(lista);
        }

        try { SecureStorage.Default.Remove(ClaveHash(usuario)); } catch (Exception) { }
        Preferences.Default.Remove(ClaveSesion(usuario));

        if (string.Equals(usuario, Usuario, StringComparison.OrdinalIgnoreCase))
        {
            var siguiente = lista.FirstOrDefault();
            if (siguiente is null) Preferences.Default.Remove(ClaveUltimo);
            else Preferences.Default.Set(ClaveUltimo, siguiente.Usuario);
        }
    }

    /// <summary>Olvida todas las cuentas del equipo.</summary>
    public static void Limpiar()
    {
        foreach (var cuenta in LeerCuentas())
        {
            try { SecureStorage.Default.Remove(ClaveHash(cuenta.Usuario)); } catch (Exception) { }
            Preferences.Default.Remove(ClaveSesion(cuenta.Usuario));
        }

        Preferences.Default.Remove(ClaveCuentas);
        Preferences.Default.Remove(ClaveUltimo);
        Preferences.Default.Remove(ClaveSesionVieja);
        Preferences.Default.Remove(ClaveTotpVieja);
        Preferences.Default.Remove(ClaveDispositivoViejo);
        try { SecureStorage.Default.Remove(ClaveHashVieja); } catch (Exception) { }
    }

    // ================================================================ almacenamiento
    private static string ClaveHash(string usuario) =>
        "urbanpark.credencial.hash." + usuario.Trim().ToLowerInvariant();

    private static string ClaveSesion(string usuario) =>
        "urbanpark.credencial.sesion." + usuario.Trim().ToLowerInvariant();

    private static List<CuentaLocal> LeerCuentas()
    {
        var json = Preferences.Default.Get(ClaveCuentas, "");
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            var lista = JsonSerializer.Deserialize<List<CuentaLocal>>(json, JsonUrbanPark.Opciones) ?? [];
            return lista.Where(c => !string.IsNullOrWhiteSpace(c.Usuario))
                .OrderByDescending(c => c.GuardadoEn)
                .ToList();
        }
        catch (Exception ex)
        {
            Registro.Aviso("Credencial", $"Las cuentas guardadas estaban corruptas: {ex.Message}");
            return [];
        }
    }

    private static void GuardarCuentas(List<CuentaLocal> lista)
    {
        try
        {
            Preferences.Default.Set(ClaveCuentas, JsonSerializer.Serialize(lista, JsonUrbanPark.Opciones));
        }
        catch (Exception ex)
        {
            Registro.Error("Credencial", "No se pudieron guardar las cuentas del equipo.", ex);
        }
    }

    /// <summary>Pasa la credencial única de versiones anteriores al formato por cuenta.</summary>
    private static List<CuentaLocal> MigrarCuentaUnica()
    {
        var anterior = Preferences.Default.Get(ClaveUltimo, "");
        if (string.IsNullOrWhiteSpace(anterior)) return [];

        var cuenta = new CuentaLocal
        {
            Usuario = anterior,
            TotpActivo = Preferences.Default.Get(ClaveTotpVieja, false),
            Dispositivo = Preferences.Default.Get(ClaveDispositivoViejo, ""),
            GuardadoEn = DateTime.Now
        };

        var lista = new List<CuentaLocal> { cuenta };
        GuardarCuentas(lista);
        Registro.Info("Credencial", $"Se migró la cuenta {anterior} al formato por cuenta.");
        return lista;
    }

    /// <summary>Hash de la contraseña de esa cuenta (con respaldo en la clave antigua).</summary>
    private static async Task<string?> HashGuardadoAsync(string usuario)
    {
        try
        {
            var propio = await SecureStorage.Default.GetAsync(ClaveHash(usuario));
            if (!string.IsNullOrEmpty(propio)) return propio;

            // Versión anterior: un único hash para la última cuenta que entró.
            if (string.Equals(usuario, Usuario, StringComparison.OrdinalIgnoreCase))
                return await SecureStorage.Default.GetAsync(ClaveHashVieja);

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Copia el hash antiguo (de la época de una sola cuenta) a la clave propia de esa
    /// cuenta, para que no se pierda cuando otra cuenta pase a ser la más reciente.
    /// Solo copia: la clave antigua se deja intacta hasta que el usuario limpie el equipo.
    /// </summary>
    private static async Task AsegurarHashPropioAsync(string? usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return;

        try
        {
            var propio = await SecureStorage.Default.GetAsync(ClaveHash(usuario));
            if (!string.IsNullOrEmpty(propio)) return;

            var antiguo = await SecureStorage.Default.GetAsync(ClaveHashVieja);
            if (string.IsNullOrEmpty(antiguo)) return;

            await SecureStorage.Default.SetAsync(ClaveHash(usuario), antiguo);
            Registro.Info("Credencial", $"Se conservó la contraseña guardada de {usuario}.");
        }
        catch (Exception ex)
        {
            Registro.Aviso("Credencial", $"No se pudo conservar la contraseña de {usuario}: {ex.Message}");
        }
    }

    // ================================================================ PBKDF2
    private static string Hashear(string password)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, sal, Iteraciones, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerificarHash(string password, string guardado)
    {
        var partes = guardado.Split('$');
        if (partes.Length != 4 || !partes[0].Equals("pbkdf2", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!int.TryParse(partes[1], out var iteraciones) || iteraciones <= 0)
            return false;

        try
        {
            var sal = Convert.FromBase64String(partes[2]);
            var esperado = Convert.FromBase64String(partes[3]);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(password, sal, iteraciones, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(esperado, calculado);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
