using System.Security.Cryptography;
using System.Text;

namespace UrbanPark.App.Servicios;

/// <summary>
/// Credenciales que quedan guardadas en el equipo para poder entrar de dos formas:
///   · solo con el código de Google Authenticator (cuando el equipo ya está vinculado), y
///   · con usuario y contraseña aunque no haya internet.
/// La contraseña nunca se guarda: se guarda un hash PBKDF2-SHA256 con sal.
/// </summary>
public static class CredencialLocal
{
    private const string ClaveUsuario = "urbanpark.credencial.usuario";
    private const string ClaveTotp = "urbanpark.credencial.totp";
    private const string ClaveDispositivo = "urbanpark.credencial.dispositivo";
    private const string ClaveHash = "urbanpark.credencial.hash";
    private const int Iteraciones = 100_000;

    /// <summary>Último usuario que entró en este equipo.</summary>
    public static string Usuario => Preferences.Default.Get(ClaveUsuario, "");

    /// <summary>True si la cuenta de <see cref="Usuario"/> tiene el autenticador activo.</summary>
    public static bool TotpActivo => Preferences.Default.Get(ClaveTotp, false);

    /// <summary>Token del equipo vinculado (lo devuelve el API al entrar con el código).</summary>
    public static string Dispositivo => Preferences.Default.Get(ClaveDispositivo, "");

    /// <summary>El equipo puede entrar solo con el código del autenticador.</summary>
    public static bool PuedeEntrarConCodigo =>
        Usuario.Length > 0 && Dispositivo.Length >= 32 && TotpActivo;

    /// <summary>Hay una contraseña guardada para entrar sin internet.</summary>
    public static async Task<bool> TieneCredencialAsync()
    {
        if (Usuario.Length == 0) return false;
        try
        {
            return !string.IsNullOrEmpty(await SecureStorage.Default.GetAsync(ClaveHash));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Guarda lo necesario tras un login correcto: usuario, hash de la contraseña (para el
    /// ingreso sin internet), si la cuenta usa autenticador y el token del equipo.
    /// </summary>
    public static async Task GuardarAsync(string usuario, string? password, bool totpActivo, string? dispositivo)
    {
        Preferences.Default.Set(ClaveUsuario, usuario);
        Preferences.Default.Set(ClaveTotp, totpActivo);

        if (!string.IsNullOrWhiteSpace(dispositivo))
            Preferences.Default.Set(ClaveDispositivo, dispositivo);

        if (string.IsNullOrEmpty(password))
            return;

        try
        {
            await SecureStorage.Default.SetAsync(ClaveHash, Hashear(password));
        }
        catch (Exception)
        {
            // sin SecureStorage el ingreso sin internet no estará disponible
        }
    }

    /// <summary>Comprueba usuario y contraseña contra lo guardado (para entrar sin internet).</summary>
    public static async Task<bool> VerificarAsync(string usuario, string password)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrEmpty(password)) return false;
        if (!string.Equals(usuario, Usuario, StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            var guardado = await SecureStorage.Default.GetAsync(ClaveHash);
            return !string.IsNullOrWhiteSpace(guardado) && VerificarHash(password, guardado);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Marca si la cuenta usa autenticador (se consulta al API y se cachea).</summary>
    public static void ActualizarTotp(bool activo, string? dispositivo = null)
    {
        Preferences.Default.Set(ClaveTotp, activo);
        if (!string.IsNullOrWhiteSpace(dispositivo))
            Preferences.Default.Set(ClaveDispositivo, dispositivo);
    }

    public static void OlvidarDispositivo() => Preferences.Default.Remove(ClaveDispositivo);

    // ------------------------------------------------------------------ sesión sin internet
    private const string ClaveSesion = "urbanpark.credencial.sesion";

    /// <summary>Guarda una copia de la última sesión para poder abrir la app sin internet.</summary>
    public static void GuardarSesionOffline(UrbanPark.App.Modelos.SesionDto sesion)
    {
        try
        {
            Preferences.Default.Set(ClaveSesion,
                System.Text.Json.JsonSerializer.Serialize(sesion, UrbanPark.App.Modelos.JsonUrbanPark.Opciones));
        }
        catch (Exception) { }
    }

    /// <summary>Reconstruye la sesión guardada (con un token de relleno) para el modo sin conexión.</summary>
    public static UrbanPark.App.Modelos.SesionDto? SesionOffline()
    {
        var json = Preferences.Default.Get(ClaveSesion, "");
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var sesion = System.Text.Json.JsonSerializer.Deserialize<UrbanPark.App.Modelos.SesionDto>(
                json, UrbanPark.App.Modelos.JsonUrbanPark.Opciones);
            if (sesion is null || string.IsNullOrWhiteSpace(sesion.Username)) return null;

            sesion.AccessToken = "sin-conexion";
            sesion.RefreshToken = "";
            sesion.ExpiresAt = 0;
            return sesion;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Limpiar()
    {
        Preferences.Default.Remove(ClaveUsuario);
        Preferences.Default.Remove(ClaveTotp);
        Preferences.Default.Remove(ClaveDispositivo);
        try { SecureStorage.Default.Remove(ClaveHash); } catch (Exception) { }
    }

    // ------------------------------------------------------------------ PBKDF2
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
