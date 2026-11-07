using System.Globalization;

namespace UrbanPark.Api.Servicios;

/// <summary>
/// Tarifas por zona y tipo, alineadas con la semilla de <c>InicializadorDb</c>.
/// </summary>
public static class ReglasTarifa
{
    private static double R(double v) => Math.Round(v, 1, MidpointRounding.AwayFromZero);

    /// <summary>Letra de zona en códigos tipo CEN-A-01.</summary>
    public static char LetraZona(string codigoPlaza)
    {
        var partes = (codigoPlaza ?? "").Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (partes.Length >= 2 && partes[1].Length == 1 && char.IsLetter(partes[1][0]))
            return char.ToUpperInvariant(partes[1][0]);

        return 'A';
    }

    public static double TarifaHora(string sedeCodigo, char zonaLetra, string tipoVehiculo, double tarifaBase)
    {
        var tipo = (tipoVehiculo ?? "AUTO").Trim().ToUpperInvariant();
        var sede = (sedeCodigo ?? "").Trim().ToUpperInvariant();
        var z = char.ToUpperInvariant(zonaLetra);

        if (sede == "CEN")
            return TarifaCen(z, tipo);

        var b = tarifaBase > 0 ? tarifaBase : 2.5;
        var (auto, moto, camioneta, disc) = z switch
        {
            'A' => (R(b), 1.5, R(b * 1.15), 2.0),
            'B' => (R(b * 0.9), 1.5, R(b * 1.10), 2.0),
            'C' => (R(b * 0.8), 1.5, R(b * 1.10), 2.0),
            'D' => (R(b * 1.05), 1.5, R(b * 1.20), 2.0),
            _ => (R(b), 1.5, R(b * 1.15), 2.0)
        };

        return tipo switch
        {
            "MOTO" => moto,
            "CAMIONETA" => camioneta,
            "DISCAPACIDAD" or "EMBARAZADA" => disc,
            _ => auto
        };
    }

    private static double TarifaCen(char z, string tipo) => tipo switch
    {
        "MOTO" => 1.5,
        "CAMIONETA" => z switch { 'A' => 4.0, 'B' => 4.0, 'C' => 3.8, 'D' => 4.2, _ => 4.0 },
        "DISCAPACIDAD" or "EMBARAZADA" => 2.0,
        _ => z switch { 'A' => 3.5, 'B' => 2.8, 'C' => 2.5, 'D' => 3.0, _ => 3.0 }
    };
}
