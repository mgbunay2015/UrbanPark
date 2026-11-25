using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Rutas a pie hasta cualquier sede Urban Park. Lista todas las sedes, permite filtrar y elegir destino.
/// </summary>
public partial class vLlegada : ContentPage
{
    /// <summary>Cuando se abre la pestaña Llegar desde QR o el mapa, sede a resaltar una vez.</summary>
    public static long? SedeDestacarAlMostrar { get; set; }

    private const string FiltroTodos = "TODOS";

    private List<SedeDto> _sedes = [];
    private List<SedeDto> _visibles = [];
    private readonly Dictionary<Button, string> _chipClaves = new();
    private string _filtroSector = FiltroTodos;
    private string _busqueda = "";
    private bool _pinElegidoEnMapa;
    private Location? _origen;
    private SedeDto? _seleccionada;
    private bool _esEjemplo;
    private long? _sedeInicialId;

    public vLlegada() : this(null) { }

  /// <param name="sedeInicialId">Sede a destacar al abrir (opcional).</param>
    public vLlegada(long? sedeInicialId)
    {
        _sedeInicialId = sedeInicialId;
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (SedeDestacarAlMostrar is > 0)
        {
            _sedeInicialId = SedeDestacarAlMostrar;
            SedeDestacarAlMostrar = null;
        }

        if (_sedes.Count == 0)
            await CargarAsync();
        else if (_sedeInicialId is > 0)
        {
            var inicial = _sedes.FirstOrDefault(s => s.Id == _sedeInicialId);
            if (inicial is not null)
                Seleccionar(inicial);
            _sedeInicialId = null;
        }
    }

    private async Task CargarAsync()
    {
        spinner.IsVisible = spinner.IsRunning = true;
        try
        {
            var (lat, lng, real) = await Ubicacion.ObtenerAsync(pedirPermiso: true);
            _origen = new Location(lat, lng);
            _esEjemplo = !real;
            _sedes = await CargarSedesAsync(_origen);
            if (_sedes.Count == 0)
            {
                lblEstado.Text = "No hay parqueaderos para mostrar.";
                lblListaInfo.Text = "";
                contenedorSedes.Clear();
                return;
            }

            CompletarDistancias(_origen, _sedes);
            _sedes = _sedes.OrderBy(s => s.DistanciaKm ?? double.MaxValue).ToList();

            PintarFiltros();
            AplicarFiltros();

            var inicial = _sedeInicialId is > 0
                ? _sedes.FirstOrDefault(s => s.Id == _sedeInicialId)
                : _visibles.FirstOrDefault();
            if (inicial is not null)
                Seleccionar(inicial);

            lblEstado.Text = _esEjemplo
                ? $"Sin GPS: { _visibles.Count } parqueadero(s) · mapa de ejemplo."
                : $"{_visibles.Count} parqueadero(s) cerca de ti · elige uno para ver la ruta.";
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

    private async Task<List<SedeDto>> CargarSedesAsync(Location origen)
    {
        try
        {
            var lista = await ApiCliente.SedesAsync(origen.Latitude, origen.Longitude) ?? [];
            if (lista.Count > 0)
            {
                BaseLocal.GuardarSedes(lista);
                return lista;
            }
        }
        catch (Exception ex)
        {
            Registro.Aviso("vLlegada", $"No se pudieron cargar sedes en línea: {ex.Message}");
        }

        var locales = BaseLocal.LeerSedes();
        return locales.Count > 0 ? locales : [];
    }

    private static void CompletarDistancias(Location origen, IEnumerable<SedeDto> sedes)
    {
        foreach (var s in sedes)
        {
            if (s.Latitud == 0 && s.Longitud == 0) continue;
            if (s.DistanciaKm is not null && s.TiempoCaminandoMin is not null) continue;

            var km = Location.CalculateDistance(origen, new Location(s.Latitud, s.Longitud), DistanceUnits.Kilometers);
            s.DistanciaKm = Math.Round(km, 2);
            s.TiempoCaminandoMin ??= Math.Max(1, (int)Math.Ceiling(km * 12));
        }
    }

    private void PintarFiltros()
    {
        contenedorFiltros.Clear();
        _chipClaves.Clear();

        var sectores = _sedes
            .Select(s => (s.Sector ?? "").Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AgregarChip(FiltroTodos, "Todos");
        foreach (var sector in sectores)
            AgregarChip(sector, sector);
    }

    private void AgregarChip(string clave, string texto)
    {
        var btn = new Button
        {
            Text = texto,
            Style = EstiloChip(string.Equals(_filtroSector, clave, StringComparison.OrdinalIgnoreCase))
        };
        btn.Clicked += (_, _) =>
        {
            _filtroSector = clave;
            if (string.Equals(clave, FiltroTodos, StringComparison.OrdinalIgnoreCase))
                _pinElegidoEnMapa = false;
            ActualizarChips();
            AplicarFiltros();
        };
        _chipClaves[btn] = clave;
        contenedorFiltros.Add(btn);
    }

    private static Style? EstiloChip(bool activo) =>
        Application.Current?.Resources.TryGetValue(activo ? "FiltroChipActivo" : "FiltroChip", out var s) == true && s is Style st
            ? st
            : Ui.EstiloDe(activo ? "BotonPrimario" : "BotonSecundario");

    private void ActualizarChips()
    {
        foreach (var (btn, clave) in _chipClaves)
        {
            var activo = string.Equals(_filtroSector, clave, StringComparison.OrdinalIgnoreCase);
            btn.Style = EstiloChip(activo);
        }
    }

    private void OnBuscarTextChanged(object? sender, TextChangedEventArgs e)
    {
        _busqueda = (txtBuscar.Text ?? "").Trim();
        AplicarFiltros();
    }

    private void AplicarFiltros()
    {
        var q = _busqueda;
        _visibles = _sedes
            .Where(s => _filtroSector == FiltroTodos
                        || string.Equals((s.Sector ?? "").Trim(), _filtroSector, StringComparison.OrdinalIgnoreCase))
            .Where(s => string.IsNullOrEmpty(q)
                        || (s.Nombre?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (s.Codigo?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (s.Sector?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (s.Direccion?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        lblListaInfo.Text = _visibles.Count == _sedes.Count
            ? $"Mostrando los {_visibles.Count} parqueaderos."
            : $"Mostrando {_visibles.Count} de {_sedes.Count} parqueaderos.";

        PintarLista();

        if (_seleccionada is not null && !_visibles.Any(v => v.Id == _seleccionada.Id))
            Seleccionar(_visibles.FirstOrDefault());
        else if (_seleccionada is null && _visibles.Count > 0)
            Seleccionar(_visibles[0]);
        else
            PintarMapa();
    }

    private void PintarLista()
    {
        contenedorSedes.Clear();
        foreach (var sede in _visibles)
            contenedorSedes.Add(FilaSede(sede));
    }

    private View FilaSede(SedeDto sede)
    {
        var seleccionada = _seleccionada?.Id == sede.Id;
        var tarjeta = new Border
        {
            Style = Ui.EstiloDe("TarjetaClara"),
            Padding = new Thickness(10, 8),
            Stroke = Ui.ColorDe(seleccionada ? "Primario" : "BordeClaro"),
            StrokeThickness = seleccionada ? 2 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = new Grid
            {
                ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
                ColumnSpacing = 8,
                Children =
                {
                    new VerticalStackLayout
                    {
                        Spacing = 2,
                        Children =
                        {
                            new Label
                            {
                                Text = sede.Nombre,
                                FontFamily = "OpenSansSemibold",
                                FontSize = 12,
                                TextColor = Ui.ColorDe("TextoFuerte"),
                                LineBreakMode = LineBreakMode.TailTruncation
                            },
                            new Label
                            {
                                Text = string.IsNullOrWhiteSpace(sede.Direccion) ? sede.Sector : sede.Direccion,
                                FontSize = 10,
                                TextColor = Ui.ColorDe("TextoMedio"),
                                LineBreakMode = LineBreakMode.TailTruncation
                            },
                            new Label
                            {
                                Text = DetalleDistancia(sede),
                                FontSize = 10,
                                TextColor = Ui.ColorDe("TextoSuave")
                            }
                        }
                    },
                    new Label
                    {
                        Text = seleccionada ? "✓" : "›",
                        FontSize = 18,
                        TextColor = Ui.ColorDe(seleccionada ? "Primario" : "TextoMedio"),
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Seleccionar(sede);
        tarjeta.GestureRecognizers.Add(tap);
        return tarjeta;
    }

    private static string DetalleDistancia(SedeDto sede)
    {
        var partes = new List<string>();
        if (!string.IsNullOrWhiteSpace(sede.DistanciaTexto))
            partes.Add(sede.DistanciaTexto);
        if (!string.IsNullOrWhiteSpace(sede.CaminandoTexto))
            partes.Add(sede.CaminandoTexto);
        if (sede.PlazasLibres >= 0)
            partes.Add(sede.LibreTexto);
        return partes.Count == 0 ? "Toca para ver ruta" : string.Join(" · ", partes);
    }

    private void Seleccionar(SedeDto? sede)
    {
        _seleccionada = sede;
        PintarLista();
        if (sede is null || _origen is null)
        {
            lblSedeNombre.Text = "URBAN PARK";
            lblDistancia.Text = "—";
            lblTiempo.Text = "";
            return;
        }

        lblSedeNombre.Text = sede.Nombre;
        lblDistancia.Text = string.IsNullOrWhiteSpace(sede.DistanciaTexto) ? "—" : sede.DistanciaTexto;
        lblTiempo.Text = string.Join(" · ", new[]
        {
            sede.CaminandoTexto,
            sede.LibreTexto,
            string.IsNullOrWhiteSpace(sede.Direccion) ? null : sede.Direccion
        }.Where(t => !string.IsNullOrWhiteSpace(t)));

        PintarMapa();
    }

    private void PintarMapa()
    {
        if (_origen is null) return;

        var conCoords = _visibles.Where(s => s.Latitud != 0 || s.Longitud != 0).ToList();
        if (conCoords.Count == 0) return;

        spinner.IsVisible = spinner.IsRunning = true;

        var sedeMapa = _seleccionada ?? conCoords[0];
        if (sedeMapa.Latitud == 0 && sedeMapa.Longitud == 0)
        {
            lblEstado.Text = "Esta sede no tiene coordenadas; usa Navegar con la dirección.";
            return;
        }

        string html;
        if (string.Equals(_filtroSector, FiltroTodos, StringComparison.OrdinalIgnoreCase) && !_pinElegidoEnMapa)
        {
            html = MapaGoogle.HtmlMarcadoresSinRuta(
                _origen,
                conCoords,
                _seleccionada?.Id,
                Ubicacion.Texto,
                Ubicacion.EsReal,
                Ubicacion.Precision);
            lblEstado.Text = _esEjemplo
                ? $"Sin GPS · {conCoords.Count} parqueaderos en el mapa."
                : $"{conCoords.Count} parqueaderos · toca un pin rojo para ver la ruta.";
        }
        else
        {
            html = MapaGoogle.HtmlEmbebido(
                MapaGoogle.UrlRuta(_origen, new Location(sedeMapa.Latitud, sedeMapa.Longitud)));
            lblEstado.Text = _esEjemplo
                ? "Ruta de ejemplo (activa el GPS para tu ubicación real)."
                : $"Ruta a pie a {sedeMapa.Nombre}.";
        }

        mapa.Source = new HtmlWebViewSource { Html = html };
    }

    private void OnMapaNavegado(object? sender, WebNavigatedEventArgs e)
    {
        spinner.IsRunning = false;
        spinner.IsVisible = false;
        if (e.Result != WebNavigationResult.Success)
            lblEstado.Text = "No se pudo cargar Google Maps. Revisa la conexión.";
    }

    private void OnMapaNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!MapaGoogle.TryParseClicSede(e.Url, out var id))
            return;

        e.Cancel = true;
        var sede = _sedes.FirstOrDefault(s => s.Id == id);
        if (sede is null) return;

        MainThread.BeginInvokeOnMainThread(() => AplicarSeleccionMapa(sede));
    }

    private void AplicarSeleccionMapa(SedeDto sede)
    {
        _pinElegidoEnMapa = true;
        var sector = (sede.Sector ?? "").Trim();
        if (!string.IsNullOrEmpty(sector))
            _filtroSector = sector;

        ActualizarChips();
        _seleccionada = sede;
        AplicarFiltros();
    }

    private async void OnNavegarClicked(object? sender, EventArgs e)
    {
        if (_seleccionada is null)
        {
            await Ui.Informar(this, "Elige un parqueadero de la lista.");
            return;
        }

        if (_origen is null)
        {
            var (lat, lng, _) = await Ubicacion.ObtenerAsync(pedirPermiso: true);
            _origen = new Location(lat, lng);
        }

        try
        {
            if (_seleccionada.Latitud != 0 || _seleccionada.Longitud != 0)
            {
                await Map.Default.OpenAsync(new Location(_seleccionada.Latitud, _seleccionada.Longitud),
                    new MapLaunchOptions
                    {
                        Name = _seleccionada.Nombre,
                        NavigationMode = NavigationMode.Walking
                    });
                return;
            }
        }
        catch
        {
            // se abre URL abajo
        }

        var cultura = CultureInfo.InvariantCulture;
        var desde = string.Create(cultura, $"{_origen.Latitude},{_origen.Longitude}");
        var destino = Uri.EscapeDataString(
            $"{_seleccionada.Nombre} {_seleccionada.Direccion}".Trim());
        await Launcher.Default.OpenAsync(
            $"https://www.google.com/maps/dir/?api=1&origin={desde}&destination={destino}&travelmode=walking");
    }
}