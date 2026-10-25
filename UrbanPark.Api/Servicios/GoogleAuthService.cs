using Google.Apis.Auth;

namespace UrbanPark.Api.Servicios;

/// <summary>
/// Validacion del token de Google (Google Sign-In).
/// La app MAUI obtiene un "id_token" con WebAuthenticator y lo envia aqui;
/// el backend comprueba la firma y la audiencia contra Google y devuelve los datos del perfil.
/// </summary>
public sealed class GoogleAuthService
{
    private readonly IConfiguration _cfg;

    public GoogleAuthService(IConfiguration cfg) => _cfg = cfg;

    public string ClientId => _cfg["Google:ClientId"] ?? "";

    /// <summary>True si el usuario ya puso credenciales reales (no los marcadores de posicion).</summary>
    public bool Configurado =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !ClientId.StartsWith("TU_GOOGLE", StringComparison.OrdinalIgnoreCase);

    private string[] Audiencias() =>
        new[] { _cfg["Google:ClientId"], _cfg["Google:AndroidClientId"], _cfg["Google:iOSClientId"] }
            .Where(a => !string.IsNullOrWhiteSpace(a) && !a!.StartsWith("TU_GOOGLE", StringComparison.OrdinalIgnoreCase))
            .Select(a => a!)
            .Distinct()
            .ToArray();

    public sealed record PerfilGoogle(string Sub, string Email, string Nombre, string Apellido, string? Foto);

    /// <summary>Valida el id_token. Lanza InvalidJwtException si es invalido.</summary>
    public async Task<PerfilGoogle> ValidarAsync(string idToken)
    {
        if (!Configurado)
            throw new InvalidOperationException(
                "Google no está configurado. Pon tu Client ID real en appsettings.json (seccion Google) y en la app MAUI.");

        var audiencias = Audiencias();
        var ajustes = new GoogleJsonWebSignature.ValidationSettings();
        if (audiencias.Length > 0)
            ajustes.Audience = audiencias;

        var carga = await GoogleJsonWebSignature.ValidateAsync(idToken, ajustes);

        var nombre = carga.GivenName ?? "";
        var apellido = carga.FamilyName ?? "";
        if (string.IsNullOrWhiteSpace(nombre) && !string.IsNullOrWhiteSpace(carga.Name))
        {
            var partes = carga.Name.Split(' ', 2);
            nombre = partes[0];
            apellido = partes.Length > 1 ? partes[1] : "";
        }

        return new PerfilGoogle(carga.Subject, carga.Email ?? "", nombre, apellido, carga.Picture);
    }
}
