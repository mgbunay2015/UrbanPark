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
    /// Lee la ubicación. pedirPermiso debe ser true cuando lo dispara el usuario (Android solo
    /// muestra el diálogo si se pide desde una acción explícita).
    ///
    /// Con <paramref name="refrescar"/> se ignora a propósito la posición guardada y la caché:
    /// se pide una lectura nueva al GPS. Antes se consultaba primero la última posición conocida
    /// del sistema, que en Windows queda congelada durante horas, así que «actualizar» devolvía
    /// el mismo punto viejo y el mapa seguía calculando desde ahí.
    /// </summary>
    public static async Task<(double Latitud, double Longitud, bool Real)> ObtenerAsync(bool pedirPermiso = false, bool refrescar = false)
    {
        if (!refrescar && Intentado && EsReal)
            return (Latitud, Longitud, EsReal);

        if (!refrescar && Intentado && !EsReal && !pedirPermiso)
            return (Latitud, Longitud, EsReal);

        Intentado = true;
        try
        {
            var estado = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (estado != PermissionStatus.Granted && pedirPermiso)
                estado = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

            if (estado == PermissionStatus.Granted)
            {
                Location? punto = null;

                if (refrescar)
                {
                    // Lectura nueva, sin aceptar caché: es lo que espera el botón de actualizar.
                    punto = await LeerGpsAsync(TimeSpan.FromSeconds(12), altaPrecision: true);
                }

                if (punto is null)
                {
                    // Acepta la última posición solo si es reciente; si no, pide una lectura real.
                    var ultima = await ConLimiteAsync(Geolocation.Default.GetLastKnownLocationAsync(), TimeSpan.FromSeconds(3));
                    if (ultima is not null && ultima.Timestamp > DateTimeOffset.Now.AddMinutes(-2))
                        punto = ultima;
                }

                if (punto is null)
                    punto = await LeerGpsAsync(TimeSpan.FromSeconds(10), altaPrecision: false);

                if (punto is not null)
                {
                    Latitud = punto.Latitude;
                    Longitud = punto.Longitude;
                    EsReal = true;
                    Precision = punto.Accuracy;
                    LeidaEn = DateTimeOffset.Now;
                    Texto = await NombreDelLugarAsync(punto);
                }
                else
                {
                    EsReal = false;
                    Precision = null;
                    Texto = "Sin GPS: usamos el centro-norte de Quito para calcular distancias";
                }
            }
            else
            {
                EsReal = false;
                Precision = null;
                Texto = "Sin permiso de ubicación: usamos el centro-norte de Quito";
            }
        }
        catch (Exception)
        {
            // sin GPS ni permiso se trabaja con el punto de referencia de Quito
            EsReal = false;
            Precision = null;
        }

        Cambio?.Invoke(null, EventArgs.Empty);
        return (Latitud, Longitud, EsReal);
    }

    /// <summary>Precisión en metros de la última lectura real, si el equipo la informa.</summary>
    public static double? Precision { get; private set; }

    /// <summary>Momento en que se obtuvo la última posición real.</summary>
    public static DateTimeOffset? LeidaEn { get; private set; }

    /// <summary>
    /// Una sola lectura del GPS con límite de tiempo. Devuelve null si no llega a responder,
    /// para que la pantalla nunca se quede esperando al sensor.
    /// </summary>
    private static async Task<Location?> LeerGpsAsync(TimeSpan limite, bool altaPrecision)
    {
        var pedido = new GeolocationRequest(
            altaPrecision ? GeolocationAccuracy.High : GeolocationAccuracy.Medium,
            limite);

        return await ConLimiteAsync(
            Geolocation.Default.GetLocationAsync(pedido),
            limite + TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// En Windows el GPS a veces no responde y deja la rueda girando sin fin. Con esto la
    /// lectura se corta en <paramref name="limite"/> y se cae al punto de referencia.
    /// </summary>
    private static async Task<Location?> ConLimiteAsync(Task<Location?> tarea, TimeSpan limite)
    {
        try
        {
            var primera = await Task.WhenAny(tarea, Task.Delay(limite));
            return primera == tarea ? await tarea : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<string> NombreDelLugarAsync(Location punto)
    {
        try
        {
            var lugar = (await Geocoding.Default.GetPlacemarksAsync(punto))?.FirstOrDefault();
            if (lugar is null)
                return $"{punto.Latitude:0.0000}, {punto.Longitude:0.0000}";

            var lineas = new List<string>();

            if (!string.IsNullOrWhiteSpace(lugar.Thoroughfare))
            {
                lineas.Add(string.IsNullOrWhiteSpace(lugar.SubThoroughfare)
                    ? lugar.Thoroughfare
                    : $"{lugar.Thoroughfare} {lugar.SubThoroughfare}");
            }
            else if (!string.IsNullOrWhiteSpace(lugar.FeatureName))
                lineas.Add(lugar.FeatureName);

            foreach (var parte in new[] { lugar.SubLocality, lugar.Locality, lugar.SubAdminArea, lugar.AdminArea, lugar.CountryName })
            {
                if (string.IsNullOrWhiteSpace(parte)) continue;
                if (lineas.Contains(parte, StringComparer.OrdinalIgnoreCase)) continue;
                lineas.Add(parte);
            }

            var texto = string.Join(", ", lineas);
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
