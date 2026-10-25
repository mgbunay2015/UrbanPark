using System.Globalization;

namespace UrbanPark.App.Views;

/// <summary>
/// Ruta a pie hasta Urban Park en Google Maps. El estacionamiento se coloca a 500 m de la ubicación actual.
/// </summary>
public partial class vLlegada : ContentPage
{
    private const double DistanciaMetros = 500;
    private Location? _destino;
    private bool _esEjemplo;

    public vLlegada()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CargarAsync();
    }

    private async Task CargarAsync()
    {
        spinner.IsVisible = spinner.IsRunning = true;
        try
        {
            var origen = await UbicacionActualAsync();
            _destino = A500Metros(origen, 40);
            mapa.Source = new HtmlWebViewSource { Html = PaginaMapa(UrlMapa(origen, _destino)) };
            lblDistancia.Text = "500 m";
            lblTiempo.Text = "Unos 6 min caminando";
            lblEstado.Text = _esEjemplo
                ? "Sin GPS: mapa de ejemplo. El parqueadero queda a 500 m."
                : "Google Maps: estás a 500 m del estacionamiento.";
        }
        catch (Exception ex)
        {
            lblEstado.Text = ex.Message;
        }
        finally
        {
            spinner.IsRunning = false;
            spinner.IsVisible = false;
        }
    }

    private async Task<Location> UbicacionActualAsync()
    {
        try
        {
            var estado = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (estado != PermissionStatus.Granted)
                estado = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

            if (estado == PermissionStatus.Granted)
            {
                var pedido = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(12));
                var aqui = await Geolocation.Default.GetLocationAsync(pedido)
                           ?? await Geolocation.Default.GetLastKnownLocationAsync();
                if (aqui is not null)
                    return aqui;
            }
        }
        catch
        {
            // Sin GPS se usa un punto de ejemplo para que la ruta igual se vea.
        }

        _esEjemplo = true;
        return new Location(-0.180653, -78.467834);
    }

    /// <summary>Punto a 500 m del origen, con rumbo en grados (0 = norte).</summary>
    private static Location A500Metros(Location origen, double rumboGrados)
    {
        var rad = rumboGrados * Math.PI / 180d;
        var dLat = DistanciaMetros * Math.Cos(rad) / 111_320d;
        var dLon = DistanciaMetros * Math.Sin(rad) / (111_320d * Math.Cos(origen.Latitude * Math.PI / 180d));
        return new Location(origen.Latitude + dLat, origen.Longitude + dLon);
    }

    private static string UrlMapa(Location origen, Location destino)
    {
        var cultura = CultureInfo.InvariantCulture;
        var desde = string.Create(cultura, $"{origen.Latitude},{origen.Longitude}");
        var hasta = string.Create(cultura, $"{destino.Latitude},{destino.Longitude}");
        return $"https://maps.google.com/maps?saddr={desde}&daddr={hasta}&dirflg=w&hl=es&z=16&output=embed";
    }

    /// <summary>Google Maps Embed solo funciona dentro de un iframe.</summary>
    private static string PaginaMapa(string url) =>
        "<!DOCTYPE html><html><head><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
        "<style>html,body{margin:0;height:100%;background:#E7EEF6;}iframe{position:absolute;inset:0;width:100%;height:100%;border:0;}</style></head>" +
        $"<body><iframe src=\"{url}\" allowfullscreen loading=\"lazy\" referrerpolicy=\"no-referrer-when-downgrade\"></iframe></body></html>";

    private void OnMapaNavegado(object? sender, WebNavigatedEventArgs e)
    {
        spinner.IsRunning = false;
        spinner.IsVisible = false;
        if (e.Result != WebNavigationResult.Success)
            lblEstado.Text = "No se pudo cargar Google Maps. Revisa la conexión.";
    }

    private async void OnNavegarClicked(object? sender, EventArgs e)
    {
        if (_destino is null)
        {
            await CargarAsync();
            return;
        }

        try
        {
            await Map.Default.OpenAsync(_destino, new MapLaunchOptions
            {
                Name = "Urban Park",
                NavigationMode = NavigationMode.Walking
            });
        }
        catch
        {
            var origen = await UbicacionActualAsync();
            var cultura = CultureInfo.InvariantCulture;
            var desde = string.Create(cultura, $"{origen.Latitude},{origen.Longitude}");
            var hasta = string.Create(cultura, $"{_destino.Latitude},{_destino.Longitude}");
            await Launcher.Default.OpenAsync(
                $"https://www.google.com/maps/dir/?api=1&origin={desde}&destination={hasta}&travelmode=walking");
        }
    }
}
