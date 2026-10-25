using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Panel principal de la aplicación.
/// Replica la regla TABS_POR_ROL del sistema original: el rol efectivo es único y
/// jerárquico (ADMIN &gt; OPERADOR &gt; CLIENTE) y determina qué pantallas se muestran.
/// Las pantallas se agrupan por área para que el panel se lea como un producto real.
/// </summary>
public partial class vMenu : ContentPage
{
    private static readonly Dictionary<string, string[]> TabsPorRol = new()
    {
        ["CLIENTE"] = ["plazas", "vehiculos", "tickets", "ia", "perfil", "token"],
        ["OPERADOR"] = ["plazas", "vehiculos", "tickets", "perfil", "token"],
        ["ADMIN"] = ["plazas", "vehiculos", "tickets", "ia", "reporte", "usuarios", "perfil", "token"]
    };

    /// <summary>Clave -> (título corto, icono, grupo, token de color del icono).</summary>
    private static readonly Dictionary<string, (string Titulo, string Icono, string Grupo, string Color)> Info = new()
    {
        ["plazas"] = ("Plazas", "🅿", "OPERACIÓN", "Acento"),
        ["vehiculos"] = ("Vehículos", "🚗", "OPERACIÓN", "Acento"),
        ["tickets"] = ("Tickets", "🎫", "OPERACIÓN", "Acento"),

        ["ia"] = ("IA", "🤖", "INTELIGENCIA ARTIFICIAL", "Acento"),
        ["reporte"] = ("Reporte", "📊", "INTELIGENCIA ARTIFICIAL", "Acento"),

        ["resilience"] = ("Resilience", "🛡", "ADMINISTRACIÓN", "Advertencia"),
        ["usuarios"] = ("Usuarios", "👥", "ADMINISTRACIÓN", "Advertencia"),

        ["perfil"] = ("Perfil", "👤", "MI CUENTA", "Info"),
        ["token"] = ("Sesión", "🔑", "MI CUENTA", "Info")
    };

    private static readonly string[] OrdenGrupos =
        ["OPERACIÓN", "INTELIGENCIA ARTIFICIAL", "ADMINISTRACIÓN", "MI CUENTA"];

    private IDispatcherTimer? _reloj;
    private Label? _lblQr;

    public vMenu()
    {
        InitializeComponent();
        Construir();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Construir();
        IniciarReloj();
        await CargarEstacionadoAsync();
    }

    private TicketDto? _estacionado;

    private int _descuentoEstacionado;

    private async Task CargarEstacionadoAsync()
    {
        try
        {
            var tareaTickets = ApiCliente.ListarTicketsAsync();
            var tareaEspacios = ApiCliente.ListarEspaciosAsync();
            var tareaVehiculos = ApiCliente.ListarVehiculosAsync();
            await Task.WhenAll(tareaTickets, tareaEspacios, tareaVehiculos);

            var abiertos = (await tareaTickets ?? []).Where(t => t.Abierto).ToList();
            _estacionado = Sesion.EsCliente
                ? abiertos.FirstOrDefault(t => string.IsNullOrWhiteSpace(t.Usuario) || t.Usuario == Sesion.Username)
                : abiertos.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Usuario) && t.Usuario == Sesion.Username);

            if (_estacionado is null)
            {
                panelEstacionado.IsVisible = false;
                PintarOcupacion(await tareaEspacios ?? []);
                return;
            }

            var plaza = (await tareaEspacios ?? []).FirstOrDefault(e =>
                e.Id == _estacionado.EspacioId || e.Codigo == _estacionado.EspacioCodigo);

            var vehiculo = (await tareaVehiculos ?? []).FirstOrDefault(v =>
                string.Equals(v.Placa, _estacionado.Placa, StringComparison.OrdinalIgnoreCase));
            _descuentoEstacionado = vehiculo is null
                ? 0
                : Math.Max(vehiculo.Descuento, Ui.DescuentoDe(vehiculo.Propietario));
            var descuentoTexto = _descuentoEstacionado > 0 ? $" · {_descuentoEstacionado}% de descuento" : "";

            lblEstPlaza.Text = $"Plaza {_estacionado.EspacioCodigo ?? _estacionado.EspacioId.ToString()}";
            var zona = _estacionado.Zona ?? plaza?.Zona ?? "—";
            var nivel = plaza is null ? "" : $" · Nivel {Ui.Nivel(plaza.Nivel)}";
            lblEstDetalle.Text =
                $"{zona}{nivel}\nPlaca {_estacionado.Placa} · {Ui.Tarifa(_estacionado.TarifaHora)}{descuentoTexto}\n" +
                $"Entró {_estacionado.Entrada:HH:mm} · toca para pagar";
            panelEstacionado.IsVisible = true;
            RefrescarValor();
            IniciarReloj();
            PintarOcupacion(await tareaEspacios ?? []);
        }
        catch (Exception)
        {
            panelEstacionado.IsVisible = false;
        }
    }

    private void PintarOcupacion(IReadOnlyList<EspacioDto> espacios)
    {
        _espacios = espacios.ToList();
        PintarTipos();
        PintarLugares();

        var total = espacios.Count;
        var ocupados = espacios.Count(e => e.Ocupado);
        var libres = Math.Max(0, total - ocupados);
        var pct = total == 0 ? 0 : (int)Math.Round(ocupados * 100d / total);
        lblPorcentaje.Text = $"{pct}%";
        barraOcupacion.Progress = pct / 100d;
        lblFrase.Text = _estacionado is null
            ? $"Tienes {libres} plazas libres hoy"
            : $"Estás en la plaza {_estacionado.EspacioCodigo ?? _estacionado.EspacioId.ToString()}";
    }

    private void RefrescarValor()
    {
        if (_estacionado is null) return;
        var transcurrido = DateTime.Now - _estacionado.Entrada;
        if (transcurrido < TimeSpan.Zero) transcurrido = TimeSpan.Zero;
        lblEstValor.Text = Ui.Moneda(Ui.MontoEstimado(_estacionado.Entrada, _estacionado.TarifaHora, _descuentoEstacionado));
        lblEstTiempo.Text = $"{(int)transcurrido.TotalHours:00}:{transcurrido.Minutes:00}:{transcurrido.Seconds:00} dentro";
    }

    private async void OnEstacionadoTapped(object? sender, TappedEventArgs e) =>
        await Navigation.PushAsync(new vTickets());

    protected override void OnDisappearing()
    {
        _reloj?.Stop();
        base.OnDisappearing();
    }

    private static readonly Color Azul = Color.FromArgb("#2D4E8E");
    private static readonly Color AzulOscuro = Color.FromArgb("#1F3566");
    private static readonly Color Gris = Color.FromArgb("#6B7A99");

    private List<EspacioDto> _espacios = [];
    private bool _ubicacionLista;

    private void Construir()
    {
        var rol = Sesion.RolPrincipal;

        var nombre = Sesion.Actual?.Usuario?.NombreCompleto;
        if (string.IsNullOrWhiteSpace(nombre)) nombre = Sesion.Username;
        lblAvatar.Text = Iniciales(nombre);
        var saludo = string.IsNullOrWhiteSpace(nombre) ? "Hola" : $"Hola, {nombre}";
        lblSesion.Text = string.IsNullOrWhiteSpace(rol) ? saludo : $"{saludo} · {rol}";
        lblPie.Text = ApiCliente.BaseUrl;

        PintarTipos();
        PintarLugares();
        if (!_ubicacionLista) _ = CargarUbicacionAsync();
    }

    // ================================================================ ubicación
    private async Task CargarUbicacionAsync()
    {
        _ubicacionLista = true;
        try
        {
            if (await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>() != PermissionStatus.Granted)
            {
                lblUbicacion.Text = "Quito, Ecuador";
                return;
            }

            var punto = await Geolocation.Default.GetLastKnownLocationAsync()
                        ?? await Geolocation.Default.GetLocationAsync(
                            new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(8)));
            if (punto is null)
            {
                lblUbicacion.Text = "Quito, Ecuador";
                return;
            }

            lblUbicacion.Text = $"{punto.Latitude:0.0000}, {punto.Longitude:0.0000}";
            try
            {
                var lugar = (await Geocoding.Default.GetPlacemarksAsync(punto))?.FirstOrDefault();
                var texto = string.Join(", ", new[] { lugar?.Locality ?? lugar?.SubAdminArea, lugar?.CountryName }
                    .Where(t => !string.IsNullOrWhiteSpace(t)));
                if (!string.IsNullOrWhiteSpace(texto)) lblUbicacion.Text = texto;
            }
            catch (Exception)
            {
                // sin geocodificador se quedan las coordenadas
            }
        }
        catch (Exception)
        {
            lblUbicacion.Text = "Quito, Ecuador";
        }
    }

    // ================================================================ tipos de vehículo
    private void PintarTipos()
    {
        contenedorTipos.Children.Clear();
        var tipos = new[] { ("MOTO", "Moto"), ("AUTO", "Auto"), ("CAMIONETA", "Camioneta") };
        for (var i = 0; i < tipos.Length; i++)
        {
            var (tipo, nombre) = tipos[i];
            var libres = _espacios.Count(e => !e.Ocupado && string.Equals(e.TipoVehiculo, tipo, StringComparison.OrdinalIgnoreCase));
            var tarjeta = new Border
            {
                BackgroundColor = i == 1 ? Azul : Colors.White,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 18 },
                Padding = new Thickness(6, 8),
                Content = new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new Label { Text = Ui.IconoTipo(tipo), FontSize = 18, HorizontalTextAlignment = TextAlignment.Center },
                        new Label
                        {
                            Text = nombre,
                            FontFamily = "OpenSansSemibold",
                            FontSize = 13,
                            TextColor = i == 1 ? Colors.White : AzulOscuro,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = _espacios.Count == 0 ? "—" : $"{libres} libres",
                            FontSize = 11,
                            TextColor = i == 1 ? Color.FromArgb("#C9D6F0") : Gris,
                            HorizontalTextAlignment = TextAlignment.Center
                        }
                    }
                }
            };

            var toque = new TapGestureRecognizer();
            toque.Tapped += async (_, _) => await Navigation.PushAsync(new vPlazas(tipo, null));
            tarjeta.GestureRecognizers.Add(toque);
            contenedorTipos.Add(tarjeta, i, 0);
        }
    }

    // ================================================================ lugares recientes
    private void PintarLugares()
    {
        contenedorLugares.Clear();
        if (_espacios.Count == 0)
        {
            contenedorLugares.Add(TarjetaLugar("Urban Park", "500 m · Unos 6 min caminando", "", "Cómo llegar",
                () => Navigation.PushAsync(new vLlegada())));
            return;
        }

        foreach (var zona in _espacios.GroupBy(e => e.Zona ?? "").OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var libres = zona.Count(e => !e.Ocupado);
            var nivel = Ui.Nivel(zona.First().Nivel);
            var tarifa = Ui.Tarifa(zona.Min(e => e.TarifaHora));
            var nombre = zona.Key;
            contenedorLugares.Add(TarjetaLugar(
                $"Urban Park · {nombre}",
                $"📍 500 m · Nivel {nivel} · {(libres == 0 ? "Lleno" : $"{libres} libres")}",
                tarifa,
                "Elegir",
                () => Navigation.PushAsync(new vPlazas(null, nombre))));
        }
    }

    private static Border TarjetaLugar(string titulo, string detalle, string precio, string accion, Func<Task> alPulsar)
    {
        var tarjeta = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(12),
            Content = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                ColumnSpacing = 12,
                Children =
                {
                    new Border
                    {
                        BackgroundColor = Color.FromArgb("#E3EBFA"),
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 14 },
                        WidthRequest = 36,
                        HeightRequest = 36,
                        Content = new Label
                        {
                            Text = "P",
                            FontFamily = "OpenSansSemibold",
                            FontSize = 16,
                            TextColor = Azul,
                            HorizontalOptions = LayoutOptions.Center,
                            VerticalOptions = LayoutOptions.Center
                        }
                    }
                }
            }
        };

        var grid = (Grid)tarjeta.Content;
        var textos = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = titulo, FontFamily = "OpenSansSemibold", FontSize = 14, TextColor = AzulOscuro, LineBreakMode = LineBreakMode.TailTruncation },
                new Label { Text = detalle, FontSize = 12, TextColor = Gris }
            }
        };
        grid.Add(textos, 1, 0);

        var derecha = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = precio, FontFamily = "OpenSansSemibold", FontSize = 14, TextColor = Azul, HorizontalTextAlignment = TextAlignment.End },
                new Label { Text = accion + " ›", FontSize = 11, TextColor = Gris, HorizontalTextAlignment = TextAlignment.End }
            }
        };
        grid.Add(derecha, 2, 0);

        var toque = new TapGestureRecognizer();
        toque.Tapped += async (_, _) => await alPulsar();
        tarjeta.GestureRecognizers.Add(toque);
        return tarjeta;
    }

    // ================================================================ búsqueda
    private async void OnBuscarCompleted(object? sender, EventArgs e)
    {
        var texto = txtBuscar.Text?.Trim();
        if (string.IsNullOrWhiteSpace(texto))
        {
            await Navigation.PushAsync(new vPlazas());
            return;
        }

        var plaza = _espacios.FirstOrDefault(p =>
                        string.Equals(p.Codigo, texto, StringComparison.OrdinalIgnoreCase))
                    ?? _espacios.FirstOrDefault(p =>
                        p.Codigo.Contains(texto, StringComparison.OrdinalIgnoreCase) ||
                        (p.Zona ?? "").Contains(texto, StringComparison.OrdinalIgnoreCase) ||
                        (p.TipoVehiculo ?? "").Contains(texto, StringComparison.OrdinalIgnoreCase));

        if (plaza is null)
        {
            await Ui.Informar(this, $"No encontré plazas ni zonas con \"{texto}\".");
            return;
        }

        var porTipo = (plaza.TipoVehiculo ?? "").Contains(texto, StringComparison.OrdinalIgnoreCase);
        await Navigation.PushAsync(new vPlazas(porTipo ? plaza.TipoVehiculo : null, plaza.Zona));
    }

    private async void OnVerTodoTapped(object? sender, TappedEventArgs e) =>
        await Navigation.PushAsync(new vPlazas());

    /// <summary>
    /// Icono QR de salida. Apagado hasta que hay un pago vigente;
    /// encendido, abre el código y muestra la cuenta atrás.
    /// </summary>
    private Border BotonQr()
    {
        var pase = PaseSalida.Actual;
        var activo = pase is not null;

        _lblQr = new Label
        {
            Text = activo ? PaseSalida.TextoRestante(pase!) : "QR",
            FontFamily = "OpenSansSemibold",
            FontSize = 13,
            TextColor = Ui.ColorDe(activo ? "Acento" : "TextoClaroSuave"),
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var circulo = new Border
        {
            BackgroundColor = Ui.ColorDe(activo ? "Acento" : "SuperficieBorde").WithAlpha(activo ? 0.22f : 0.4f),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 28 },
            WidthRequest = 36,
            HeightRequest = 36,
            HorizontalOptions = LayoutOptions.Center,
            Padding = 0,
            Content = new Label
            {
                Text = "QR",
                FontFamily = "OpenSansSemibold",
                FontSize = 12,
                TextColor = Ui.ColorDe(activo ? "Acento" : "TextoClaroSuave"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        var boton = new Border
        {
            BackgroundColor = Ui.ColorDe("SuperficieAlta"),
            Stroke = Ui.ColorDe(activo ? "Acento" : "SuperficieBorde"),
            StrokeThickness = activo ? 2 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            WidthRequest = 88,
            HeightRequest = 88,
            Padding = new Thickness(6, 8, 6, 6),
            Margin = new Thickness(0, 0, 8, 8),
            Opacity = activo ? 1 : 0.45,
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children = { circulo, _lblQr }
            }
        };

        var toque = new TapGestureRecognizer();
        toque.Tapped += async (_, _) => await AbrirQrAsync();
        boton.GestureRecognizers.Add(toque);
        return boton;
    }

    private void IniciarReloj()
    {
        _reloj?.Stop();
        var conPase = PaseSalida.Actual is not null;
        if (!conPase && _estacionado is null) return;

        _reloj = Dispatcher.CreateTimer();
        _reloj.Interval = TimeSpan.FromSeconds(1);
        _reloj.Tick += (_, _) =>
        {
            RefrescarValor();

            if (!conPase) return;
            var pase = PaseSalida.Actual;
            if (pase is null)
            {
                conPase = false;
                Construir();
                if (_estacionado is null) _reloj?.Stop();
                return;
            }

            if (_lblQr is not null)
                _lblQr.Text = PaseSalida.TextoRestante(pase);
        };
        _reloj.Start();
    }

    private async Task AbrirQrAsync()
    {
        if (PaseSalida.Actual is null)
        {
            await Ui.Informar(this, "El QR se habilita cuando pagas el ticket. Sirve para salir y dura 10 minutos.");
            return;
        }

        await Navigation.PushAsync(new vSalida());
    }

    private static string Iniciales(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return "UP";
        var limpio = nombre.Trim();
        return limpio.Length >= 2 ? limpio[..2].ToUpperInvariant() : limpio.ToUpperInvariant();
    }

    private async Task Abrir(string clave)
    {
        // Defensa redundante, igual que el frontend original.
        if (clave == "reporte" && !Sesion.EsAdmin) { await Ui.Error(this, "No tienes acceso a esta pantalla con tu perfil"); return; }
        if (clave == "resilience" && !Sesion.EsAdmin) { await Ui.Error(this, "Solo ADMIN puede usar Resilience"); return; }
        if (clave == "usuarios" && !Sesion.EsAdmin) { await Ui.Error(this, "No tienes acceso a esta pantalla con tu perfil"); return; }
        if (clave == "ia" && Sesion.RolPrincipal == "OPERADOR") { await Ui.Error(this, "No tienes acceso a esta pantalla con tu perfil"); return; }

        Page destino = clave switch
        {
            "plazas" => new vPlazas(),
            "vehiculos" => new vVehiculos(),
            "tickets" => new vTickets(),
            "ia" => new vIa(),
            "reporte" => new vReporte(),
            "resilience" => new vResilience(),
            "usuarios" => new vUsuarios(),
            "perfil" => new vPerfil(),
            "token" => new vToken(),
            _ => new vMenu()
        };

        await Navigation.PushAsync(destino);
    }

    private async void OnLlegadaTapped(object? sender, TappedEventArgs e) =>
        await Navigation.PushAsync(new vLlegada());

    private async void OnLlegadaClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new vLlegada());

    private async void OnTicketsClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new vTickets());

    private async Task SalirAsync()
    {
        if (!await Ui.Confirmar(this, "Cerrar sesión", "¿Seguro que quieres salir?"))
            return;

        Sesion.Cerrar();
        PaseSalida.Limpiar();
        // Se cambia la página fuera del evento del botón: si no, WinUI se cae al
        // reemplazar el Shell mientras procesa el clic.
        await Task.Delay(60);
        if (Application.Current?.Windows.Count > 0)
            Application.Current.Windows[0].Page = Ui.NavegacionConMarca(new vLogin());
    }
}
