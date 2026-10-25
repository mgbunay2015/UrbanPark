using System.Globalization;
using UrbanPark.App.Modelos;

namespace UrbanPark.App.Servicios;

/// <summary>Utilidades de interfaz y formateo (cultura es-EC, igual que el proyecto de referencia).</summary>
public static class Ui
{
    private static readonly CultureInfo Ecuador = CultureInfo.GetCultureInfo("es-EC");

    public static Task Aviso(Page pagina, string mensaje, string titulo = "Aviso") =>
        pagina.DisplayAlertAsync(titulo, mensaje, "OK");

    public static Task Exito(Page pagina, string mensaje) =>
        pagina.DisplayAlertAsync("Listo", mensaje, "OK");

    public static Task Error(Page pagina, string mensaje) =>
        pagina.DisplayAlertAsync("Error", mensaje, "OK");

    public static Task Informar(Page pagina, string mensaje) =>
        pagina.DisplayAlertAsync("Información", mensaje, "OK");

    public static Task<bool> Confirmar(Page pagina, string titulo, string mensaje) =>
        pagina.DisplayAlertAsync(titulo, mensaje, "Confirmar", "Cancelar");

    /// <summary>Muestra el error de una excepción con el formato del API.</summary>
    public static Task MostrarExcepcion(Page pagina, Exception ex) =>
        ex is ApiExcepcion ? Error(pagina, ex.Message) : Error(pagina, "Ocurrió un error: " + ex.Message);

    // ------------------------------------------------------------------ formato
    public static string Moneda(double valor) => "$" + valor.ToString("0.00", Ecuador);

    public static string Moneda(double? valor) => valor is null ? "—" : Moneda(valor.Value);

    public static string Tarifa(double? valor) =>
        valor is null ? "—" : "$" + valor.Value.ToString("0.##", Ecuador) + "/h";

    public static string Fecha(DateTime? valor) =>
        valor is null ? "—" : valor.Value.ToString("dd/MM/yyyy HH:mm:ss", Ecuador);

    public static string FechaCorta(DateTime? valor) =>
        valor is null ? "—" : valor.Value.ToString("dd/MM/yyyy", Ecuador);

    public static string Nivel(int? nivel) => nivel is null ? "N-" : $"N{nivel}";

    /// <summary>Calcula el monto estimado de un ticket abierto (misma fórmula del backend).</summary>
    public static double MontoEstimado(DateTime entrada, double? tarifaHora, int descuentoPorcentaje = 0)
    {
        var minutos = Math.Max(1L, (long)(DateTime.Now - entrada).TotalMinutes);
        var horas = Math.Ceiling(minutos / 60.0);
        var monto = horas * (tarifaHora ?? 0);
        var descuento = Math.Clamp(descuentoPorcentaje, 0, 100);
        if (descuento > 0)
            monto *= 1d - descuento / 100d;
        return Math.Round(monto, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Mismos porcentajes que el API: tercera edad y capacidades especiales 50, embarazada y estudiante 25.</summary>
    public static int DescuentoDe(string? perfil)
    {
        var p = (perfil ?? "").ToLowerInvariant();
        if (p.Contains("capacidades") || p.Contains("discapacit") || p.Contains("especial"))
            return 50;
        if (p.Contains("tercera") || p.Contains("anciano") || p.Contains("mayor"))
            return 50;
        if (p.Contains("embaraz"))
            return 25;
        if (p.Contains("estudiante") || p.Contains("estudiate"))
            return 25;
        return 0;
    }

    /// <summary>Placa de hasta 9 caracteres, con guion después del tercero: ABC-1234.</summary>
    public static bool PlacaValida(string? placa) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            (placa ?? "").Trim().ToUpperInvariant().Replace(" ", ""),
            "^[A-Z0-9]{3}-[A-Z0-9]{1,5}$");

    public static string IconoTipo(string? tipo) => (tipo ?? "AUTO").ToUpperInvariant() switch
    {
        "MOTO" => "🏍",
        "CAMIONETA" => "🚙",
        "DISCAPACIDAD" => "♿",
        "EMBARAZADA" => "🤰",
        _ => "🚗"
    };

    // ------------------------------------------------------------------ sistema de diseño
    /// <summary>
    /// Devuelve un color del sistema de diseño (Resources/Styles/Colors.xaml) para usarlo
    /// desde el código. Así ninguna pantalla escribe colores en linea.
    /// Uso: Ui.ColorDe("Exito"), Ui.ColorDe("Peligro"), Ui.ColorDe("Acento")...
    /// </summary>
    public static Color ColorDe(string clave)
    {
        if (Application.Current?.Resources.TryGetValue(clave, out var valor) == true && valor is Color color)
            return color;

        return Colors.Gray;
    }

    /// <summary>Estilo del sistema de diseño (Resources/Styles/Styles.xaml) para aplicarlo desde el código.</summary>
    public static Style? EstiloDe(string clave)
    {
        if (Application.Current?.Resources.TryGetValue(clave, out var valor) == true && valor is Style estilo)
            return estilo;

        return null;
    }

    /// <summary>
    /// Crea la navegación con la barra superior en los colores de la marca.
    /// Se usa en todos los cambios de pantalla raíz para que nunca aparezca el tema por defecto de MAUI.
    /// </summary>
    public static NavigationPage NavegacionConMarca(Page raiz) => new(raiz)
    {
        BarBackgroundColor = ColorDe("Superficie"),
        BarTextColor = ColorDe("TextoClaro")
    };
}
