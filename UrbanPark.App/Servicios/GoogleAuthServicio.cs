using Microsoft.Maui.Authentication;
using UrbanPark.App.Modelos;

namespace UrbanPark.App.Servicios;

/// <summary>
/// Inicio de sesión con Google usando WebAuthenticator (flujo implícito id_token, sin secreto).
///
/// PARA ACTIVARLO:
///   1) Crea un cliente OAuth en https://console.cloud.google.com/apis/credentials
///      - Android: tipo "Android", con el nombre de paquete com.urbanpark.app y la huella SHA-1 de tu firma.
///      - iOS:     tipo "iOS", con el bundle id.
///   2) Copia el Client ID en la constante ClientId de esta clase Y en appsettings.json del API (sección Google).
///   3) En Google Cloud añade el URI de redirección:  com.urbanpark.app:/oauth2redirect
///   4) En Android ya está declarado el esquema en Platforms/Android/CallbackActivity.cs.
///      En iOS agrega el esquema en Info.plist (ver docs/GUIA_EJECUCION.md).
/// Mientras ClientId siga siendo el marcador de posición, la app avisa y sigue funcionando
/// perfectamente con los usuarios demo (cliente1, operador1, admin1).
/// </summary>
public static class GoogleAuthServicio
{
    /// <summary>Pon aquí tu Client ID real de Google.</summary>
    public const string ClientId = "TU_GOOGLE_CLIENT_ID.apps.googleusercontent.com";

    /// <summary>Esquema de redirección propio de la app (debe coincidir con el manifest y con Google).</summary>
    public const string Esquema = "com.urbanpark.app";

    public static string RedirectUri => Esquema + ":/oauth2redirect";

    public static bool Configurado =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !ClientId.StartsWith("TU_GOOGLE", StringComparison.OrdinalIgnoreCase);

    /// <summary>Devuelve el id_token de Google, o null si el usuario cancela.</summary>
    public static async Task<string?> ObtenerIdTokenAsync()
    {
        if (!Configurado)
            throw new ApiExcepcion(0,
                "Google no está configurado todavía. Abre Servicios/GoogleAuthServicio.cs y pon tu Client ID real " +
                "(y hazlo también en appsettings.json del API). Mientras tanto puedes entrar con los usuarios demo.");

        var url = "https://accounts.google.com/o/oauth2/v2/auth" +
                  "?client_id=" + Uri.EscapeDataString(ClientId) +
                  "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) +
                  "&response_type=id_token" +
                  "&scope=" + Uri.EscapeDataString("openid email profile") +
                  "&nonce=" + Guid.NewGuid().ToString("N") +
                  "&prompt=select_account";

        var opciones = new WebAuthenticatorOptions
        {
            Url = new Uri(url),
            CallbackUrl = new Uri(RedirectUri),
            PrefersEphemeralWebBrowserSession = true
        };

        var resultado = await WebAuthenticator.Default.AuthenticateAsync(opciones);
        return resultado.Properties.TryGetValue("id_token", out var token) ? token : null;
    }
}
