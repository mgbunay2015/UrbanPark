using System.Text.Json;
using UrbanPark.App.Modelos;

namespace UrbanPark.App.Servicios;

/// <summary>
/// Estado de la sesion en el dispositivo. Se guarda cifrado con SecureStorage
/// (equivalente al localStorage del frontend web, pero seguro).
/// </summary>
public static class Sesion
{
    private const string Clave = "urbanpark.sesion";

    public static SesionDto? Actual { get; private set; }

    /// <summary>Se dispara cuando cambia la sesion (login/logout/refresh).</summary>
    public static event Action? Cambio;

    public static bool Activa => Actual is not null && !string.IsNullOrWhiteSpace(Actual.AccessToken);
    public static string Username => Actual?.Username ?? "";
    public static string[] Roles => Actual?.Roles ?? [];

    /// <summary>Rol efectivo unico y jerarquico: ADMIN &gt; OPERADOR &gt; CLIENTE.</summary>
    public static string RolPrincipal
    {
        get
        {
            var roles = Roles;
            if (roles.Contains("ADMIN")) return "ADMIN";
            if (roles.Contains("OPERADOR")) return "OPERADOR";
            if (roles.Contains("CLIENTE")) return "CLIENTE";
            return "";
        }
    }

    public static bool EsCliente => RolPrincipal == "CLIENTE";
    public static bool EsAdmin => RolPrincipal == "ADMIN";
    public static bool EsOperador => RolPrincipal is "OPERADOR" or "ADMIN";

    public static string DescripcionRoles => Roles.Length == 0 ? "—" : string.Join(", ", Roles);
    public static string Etiqueta => Activa ? $"{Username} · {RolPrincipal}" : "Sin sesión";

    /// <summary>Segundos que le quedan al token de acceso.</summary>
    public static long SegundosRestantes =>
        Actual is null ? 0 : Actual.ExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Hay que refrescar cuando faltan menos de 15 segundos (margen del frontend original).</summary>
    public static bool PorExpirar => Activa && SegundosRestantes < 15;

    public static async Task GuardarAsync(SesionDto sesion)
    {
        Actual = sesion;
        if (!string.IsNullOrWhiteSpace(sesion.Username))
            Preferences.Default.Set(BiometriaServicio.ClaveUltimoUsuario, sesion.Username);
        try
        {
            await SecureStorage.Default.SetAsync(Clave, JsonSerializer.Serialize(sesion, JsonUrbanPark.Opciones));
        }
        catch (Exception)
        {
            // SecureStorage puede no estar disponible en algunos equipos de escritorio:
            // la sesion sigue viva en memoria mientras la app este abierta.
        }
        Cambio?.Invoke();
    }

    public static async Task CargarAsync()
    {
        try
        {
            var guardado = await SecureStorage.Default.GetAsync(Clave);
            if (!string.IsNullOrWhiteSpace(guardado))
            {
                Actual = JsonSerializer.Deserialize<SesionDto>(guardado, JsonUrbanPark.Opciones);
                Cambio?.Invoke();
            }
        }
        catch (Exception)
        {
            Actual = null;
        }
    }

    public static void Cerrar()
    {
        Actual = null;
        try { SecureStorage.Default.Remove(Clave); } catch (Exception) { }
        Cambio?.Invoke();
    }

    /// <summary>Decodifica el payload del JWT (para la pantalla "Sesión y JWT").</summary>
    public static Dictionary<string, object?>? PayloadDelToken()
    {
        if (!Activa) return null;
        try
        {
            var partes = Actual!.AccessToken.Split('.');
            if (partes.Length < 2) return null;

            var json = Base64Url(partes[1]);
            using var documento = JsonDocument.Parse(json);
            var resultado = new Dictionary<string, object?>();
            foreach (var propiedad in documento.RootElement.EnumerateObject())
                resultado[propiedad.Name] = propiedad.Value.ValueKind switch
                {
                    JsonValueKind.String => propiedad.Value.GetString(),
                    JsonValueKind.Number => propiedad.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => null,
                    _ => propiedad.Value.GetRawText()
                };
            return resultado;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Base64Url(string valor)
    {
        var texto = valor.Replace('-', '+').Replace('_', '/');
        texto = texto.PadRight(texto.Length + (4 - texto.Length % 4) % 4, '=');
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(texto));
    }
}
