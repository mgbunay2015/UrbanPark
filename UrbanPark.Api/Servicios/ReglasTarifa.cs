namespace UrbanPark.Api.Servicios;

/// <summary>
/// Tarifas por zona y tipo. Rango oficial del parqueadero: $0.50 – $0.90 / hora.
/// </summary>
public static class ReglasTarifa
{
    public const double Minima = 0.50;
    public const double Maxima = 0.90;

    public static double Acotar(double valor) =>
        Math.Round(Math.Clamp(valor, Minima, Maxima), 2, MidpointRounding.AwayFromZero);

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
        var b = Acotar(tarifaBase > 0 ? tarifaBase : 0.70);
        var z = char.ToUpperInvariant(zonaLetra);

        var (auto, moto, camioneta, disc) = z switch
        {
            'A' => (b, Minima, Acotar(b * 1.10), Acotar(b * 0.92)),
            'B' => (Acotar(b * 0.95), Minima, Acotar(b * 1.08), Acotar(b * 0.90)),
            'C' => (Acotar(b * 0.88), Minima, Acotar(b * 1.05), Minima),
            'D' => (Acotar(b * 1.06), Minima, Maxima, Acotar(b * 0.94)),
            _ => (b, Minima, Acotar(b * 1.10), b)
        };

        return Acotar(tipo switch
        {
            "MOTO" => moto,
            "CAMIONETA" => camioneta,
            "DISCAPACIDAD" or "EMBARAZADA" => disc,
            _ => auto
        });
    }
}
