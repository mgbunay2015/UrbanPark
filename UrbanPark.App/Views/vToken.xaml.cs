using System.Globalization;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// «Sesión y JWT» (todos los roles): claims del token actual y el JWT completo
/// en texto plano. Se vuelve a pintar en OnAppearing porque el token puede
/// haberse refrescado mientras el usuario navegaba.
/// </summary>
public partial class vToken : ContentPage
{
    private const string Vacio = "—";   // sin sesión activa
    private const string Guion = "-";   // claim ausente en el token

    public vToken()
    {
        InitializeComponent();
        lblPie.Text = $"API: {ApiCliente.BaseUrl}";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Refrescar();
    }

    // ================================================================ pintado
    private void OnRecargarClicked(object? sender, EventArgs e) => Refrescar();

    private void Refrescar()
    {
        try
        {
            lblToken.Text = Sesion.Actual?.AccessToken ?? "";

            var payload = Sesion.PayloadDelToken();

            // Sin sesión activa (o token ilegible) nada se puede mostrar.
            if (!Sesion.Activa || payload is null)
            {
                lblUsuario.Text = "Usuario: " + Vacio;
                lblRoles.Text = "Roles: " + Vacio;
                lblExpira.Text = "Expira: " + Vacio;
                lblIssuer.Text = "Issuer: " + Vacio;
                return;
            }

            lblUsuario.Text = "Usuario: " + Claim(payload, "preferred_username", Guion);
            lblRoles.Text = "Roles: " + Roles(payload, Guion);
            lblExpira.Text = "Expira: " + Expiracion(payload, Guion);
            lblIssuer.Text = "Issuer: " + Claim(payload, "iss", Guion);
        }
        catch (Exception ex)
        {
            _ = Ui.MostrarExcepcion(this, ex);
        }
    }

    // ================================================================ claims
    /// <summary>Valor de un claim convertido a texto; devuelve el respaldo si falta.</summary>
    private static string Claim(Dictionary<string, object?> payload, string clave, string respaldo)
    {
        if (!payload.TryGetValue(clave, out var valor) || valor is null) return respaldo;

        var texto = valor as string ?? valor.ToString() ?? "";
        return string.IsNullOrWhiteSpace(texto) ? respaldo : texto.Trim();
    }

    /// <summary>«roles» (o «realm_access» si no viene la lista directa), sin filtrar.</summary>
    private static string Roles(Dictionary<string, object?> payload, string respaldo)
    {
        var crudo = Claim(payload, "roles", "");
        if (crudo.Length == 0) crudo = Claim(payload, "realm_access", "");
        if (crudo.Length == 0) return respaldo;

        var texto = AplanarJson(crudo);
        return texto.Length == 0 ? respaldo : texto;
    }

    /// <summary>
    /// El payload guarda números y arreglos como texto JSON: aquí se convierten
    /// ["ADMIN","CLIENTE"] o {"roles":[...]} en «ADMIN, CLIENTE».
    /// </summary>
    private static string AplanarJson(string crudo)
    {
        var limpio = crudo.Trim();
        if (!limpio.StartsWith('[') && !limpio.StartsWith('{')) return limpio;

        try
        {
            using var documento = JsonDocument.Parse(limpio);

            if (documento.RootElement.ValueKind == JsonValueKind.Array)
                return Unir(documento.RootElement);

            if (documento.RootElement.ValueKind == JsonValueKind.Object &&
                documento.RootElement.TryGetProperty("roles", out var roles) &&
                roles.ValueKind == JsonValueKind.Array)
                return Unir(roles);
        }
        catch (JsonException)
        {
            // Si no es JSON válido se muestra el texto tal cual.
        }

        return limpio;
    }

    private static string Unir(JsonElement arreglo)
    {
        var partes = new List<string>();
        foreach (var item in arreglo.EnumerateArray())
        {
            var texto = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
            if (!string.IsNullOrWhiteSpace(texto)) partes.Add(texto!.Trim());
        }
        return string.Join(", ", partes);
    }

    /// <summary>«exp» viene en segundos Unix (a veces como texto): fecha local dd/MM/yyyy HH:mm:ss.</summary>
    private static string Expiracion(Dictionary<string, object?> payload, string respaldo)
    {
        var crudo = Claim(payload, "exp", "");
        if (crudo.Length == 0) return respaldo;

        double segundos;
        if (!long.TryParse(crudo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entero))
        {
            if (!double.TryParse(crudo, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimales))
                return respaldo;
            segundos = decimales;
        }
        else
        {
            segundos = entero;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds((long)segundos)
                .ToLocalTime()
                .ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return respaldo;
        }
    }

    // ================================================================ portapapeles
    private async void OnCopiarTokenClicked(object? sender, EventArgs e)
    {
        try
        {
            var token = Sesion.Actual?.AccessToken ?? "";
            if (string.IsNullOrWhiteSpace(token))
            {
                await Ui.Aviso(this, "No hay un token para copiar.");
                return;
            }

            await Clipboard.Default.SetTextAsync(token);
            await Ui.Aviso(this, "Token copiado");
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
    }
}
