using System.Text.RegularExpressions;

namespace UrbanPark.Api.Modelos;

/// <summary>
/// Reglas de negocio de Urban Park, replicadas literalmente del sistema original
/// (validacion de placa, formula de cobro, resolucion de roles).
/// </summary>
public static class Reglas
{
    /// <summary>Hasta 9 caracteres. El formulario inserta el guion: ABC-1234 o ABC-12345.</summary>
    private static readonly Regex PatronPlaca = new("^[A-Z0-9]{3}-[A-Z0-9]{1,5}$", RegexOptions.Compiled);

    public static readonly string[] RolesValidos = ["CLIENTE", "OPERADOR", "ADMIN"];

    /// <summary>Normaliza la placa: trim + mayusculas + sin espacios (igual que el backend original).</summary>
    public static string NormalizarPlaca(string? placa) =>
        (placa ?? "").Trim().ToUpperInvariant().Replace(" ", "");

    /// <summary>Valida la placa. Devuelve (ok, placaNormalizada, error).</summary>
    public static (bool ok, string normalizada, string? error) ValidarPlaca(string? placa, string mensajeInvalida)
    {
        if (string.IsNullOrWhiteSpace(placa))
            return (false, "", "La placa es obligatoria");

        var p = NormalizarPlaca(placa);
        var valida = p.Length is >= 5 and <= 9 && PatronPlaca.IsMatch(p);
        return valida ? (true, p, null) : (false, p, mensajeInvalida);
    }

    public const string MensajePlacaInvalidaTickets = "Placa inválida. Usa formato tipo ABC-123 o ABC123 (4 a 12 caracteres).";
    public const string MensajePlacaInvalidaVehiculos = "Placa inválida. Usa formato tipo ABC-123";

    /// <summary>
    /// Formula de cobro: minimo 1 minuto, horas hacia arriba, 2 decimales.
    /// descuentoPorcentaje resta ese porcentaje (tercera edad y capacidades especiales 50, embarazada y estudiante 25).
    /// </summary>
    public static double CalcularMonto(DateTime entrada, DateTime? salida, double? tarifaHora, int descuentoPorcentaje = 0)
    {
        var fin = salida ?? DateTime.Now;
        var minutos = Math.Max(1L, (long)(fin - entrada).TotalMinutes);
        var horas = Math.Ceiling(minutos / 60.0);
        var tarifa = tarifaHora ?? 0d;
        var monto = horas * tarifa;
        var descuento = Math.Clamp(descuentoPorcentaje, 0, 100);
        if (descuento > 0)
            monto *= 1d - descuento / 100d;
        return Math.Round(monto, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Porcentaje de descuento según quién conduce.</summary>
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

    /// <summary>Rol efectivo unico y jerarquico: ADMIN &gt; OPERADOR &gt; CLIENTE.</summary>
    public static string RolPrincipal(IEnumerable<string> roles)
    {
        var set = roles.Select(r => r.Trim().ToUpperInvariant()).ToHashSet();
        if (set.Contains("ADMIN")) return "ADMIN";
        if (set.Contains("OPERADOR")) return "OPERADOR";
        if (set.Contains("CLIENTE")) return "CLIENTE";
        return "";
    }

    /// <summary>True solo si el rol principal del usuario es CLIENTE (mismo criterio que AuthSupport.esClienteSolo).</summary>
    public static bool EsClienteSolo(IEnumerable<string> roles) => RolPrincipal(roles) == "CLIENTE";

    /// <summary>Roles ordenados alfabeticamente y limitados a los 3 roles de aplicacion.</summary>
    public static string[] RolesDeApp(IEnumerable<string> roles) =>
        roles.Select(r => r.Trim().ToUpperInvariant())
             .Where(r => RolesValidos.Contains(r))
             .Distinct()
             .OrderBy(r => r, StringComparer.Ordinal)
             .ToArray();

    /// <summary>Codigo de ticket: TK-XXXXXXXX (8 caracteres alfanumericos en mayuscula).</summary>
    public static string NuevoCodigoTicket() =>
        "TK-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
}
