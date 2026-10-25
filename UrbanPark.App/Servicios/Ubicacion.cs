namespace UrbanPark.App.Servicios;

/// <summary>
/// Ubicación del celular para el mapa de parqueaderos. Si el usuario no da permiso (o el
/// equipo no tiene GPS) se usa el centro de Quito, para que las distancias siempre se puedan
/// calcular y la recomendación de la IA tenga sentido.
/// </summary>
public static class Ubicacion
{
    /// <summary>Punto de referencia por defecto: centro-norte de Quito.</summary>
    public const double LatitudQuito = -0.2045;
    public const double LongitudQuito = -78.4900;

    public static double Latitud { get; private set; } = LatitudQuito;
    public static double Longitud { get; private set; } = LongitudQuito;

    /// <summary>True si las coordenadas son del GPS y no el punto por defecto.</summary>
    public static bool EsReal { get; private set; }

    public static bool Intentado { get; private set; }

    public static string Texto { get; private set; } = "Quito, centro-norte";

    public static event EventHandler? Cambio;

    /// <summary>
    /// Lee la ubicación una sola vez por sesión. pedirPermiso debe ser true cuando lo dispara
    /// el usuario (Android solo muestra el diálogo si se pide desde una acción explícita).
    /// </summary>
    public static async Task<(double Latitud, double Longitud, bool Real)> ObtenerAsync(bool pedirPermiso = false)
    {
        if (Intentado)
            return (Latitud, Longitud, EsReal);

        Intentado = true;
        try
        {
            var estado = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (estado != PermissionStatus.Granted && pedirPermiso)
                estado = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

            if (estado == PermissionStatus.Granted)
            {
                var punto = await Geolocation.Default.GetLastKnownLocationAsync();
                if (punto is null || punto.Timestamp < DateTimeOffset.Now.AddMinutes(-10))
                {
                    punto = await Geolocation.Default.GetLocationAsync(
                        new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(8)));
                }

                if (punto is not null)
                {
                    Latitud = punto.Latitude;
                    Longitud = punto.Longitude;
                    EsReal = true;
                    Texto = await NombreDelLugarAsync(punto);
                }
            }
        }
        catch (Exception)
        {
            // sin GPS ni permiso se trabaja con el punto de referencia de Quito
            EsReal = false;
        }

        Cambio?.Invoke(null, EventArgs.Empty);
        return (Latitud, Longitud, EsReal);
    }

    private static async Task<string> NombreDelLugarAsync(Location punto)
    {
        try
        {
            var lugar = (await Geocoding.Default.GetPlacemarksAsync(punto))?.FirstOrDefault();
            var partes = new[] { lugar?.SubLocality, lugar?.Locality, lugar?.SubAdminArea }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .Take(2);
            var texto = string.Join(", ", partes);
            return string.IsNullOrWhiteSpace(texto) ? $"{punto.Latitude:0.0000}, {punto.Longitude:0.0000}" : texto;
        }
        catch (Exception)
        {
            return $"{punto.Latitude:0.0000}, {punto.Longitude:0.0000}";
        }
    }

    /// <summary>Distancia en km (Haversine), igual que el API, para poder estimar sin conexión.</summary>
    public static double DistanciaKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double radio = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLng = (lng2 - lng1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return radio * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public static int MinutosCaminando(double km) => Math.Max(1, (int)Math.Ceiling(km / 4.8 * 60.0));
}
