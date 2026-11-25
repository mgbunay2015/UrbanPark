using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Primera pantalla de la aplicación: el mapa con los 12 parqueaderos del grupo.
/// El cliente ve dónde está cada uno, su distancia y sus plazas libres, puede pedirle a la IA
/// que le diga cuál le conviene y desde aquí entra a elegir el puesto.
/// </summary>
public partial class vSedes : ContentPage
{
    private const string FiltroTodos = "TODOS";

    private static readonly Color Amarillo = Color.FromArgb("#F2C51A");
    private static readonly Color SuperficieAlta = Color.FromArgb("#1C2530");
    private static readonly Color Borde = Color.FromArgb("#28323F");
    private static readonly Color Gris = Color.FromArgb("#7C8A9C");
    private static readonly Color Claro = Color.FromArgb("#F8FAFC");
    private static readonly Color Tinta = Color.FromArgb("#0F172A");
    private static readonly Color Azul = Color.FromArgb("#2D4E8E");
    private static readonly Color AzulOscuro = Color.FromArgb("#1F3566");
    private static readonly Color Verde = Color.FromArgb("#15803D");
    private static readonly Color FondoTarjeta = Colors.White;

    private readonly List<(string? Codigo, string Texto)> _tipos =
    [
        (null, "🚗 Todos"),
        ("AUTO", "🚗 Auto"),
        ("MOTO", "🏍 Moto"),
        ("CAMIONETA", "🚙 Camioneta")
    ];

    private List<SedeDto> _sedes = [];
    private List<SedeDto> _visibles = [];
    private List<VehiculoDto> _vehiculos = [];
    private readonly Dictionary<Button, string> _chipClaves = new();
    private string _filtroSector = FiltroTodos;
    private string _busqueda = "";
    private bool _pinElegidoEnMapa;
    private Location? _origen;
    private SedeDto? _seleccionada;
    private string? _tipo = "AUTO";
    private bool _cargando;
    private string? _perfilConductor;

    /// <summary>Puesto ocupado o reserva vigente del cliente: si existe, el mapa no se muestra.</summary>
    private PasePuestoDto? _pase;

    /// <summary>El cliente pidió ver el mapa igual, aunque ya tenga su puesto.</summary>
    private bool _verMapa;

    /// <summary>Texto de ubicación del mapa, para restaurarlo al volver del modo "mi parqueadero".</summary>
    private string _textoUbicacion = "Ubicando tu posición…";

    private IDispatcherTimer? _reloj;
    private Label? _lblMontoMi;
    private Label? _lblTiempoMi;

    public vSedes()
    {
        InitializeComponent();
        PintarTipos();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Cada vez que se entra a la pestaña se vuelve a "mi parqueadero": el mapa solo
        // aparece si el cliente no tiene puesto o si pide verlo.
        _verMapa = false;
        // La recomendación con IA es para CLIENTE y ADMIN (el OPERADOR asigna desde Tickets).
        btnIa.IsVisible = Sesion.RolPrincipal != "OPERADOR";
        await CargarAsync();
        // Si el cliente ya tiene puesto o reserva, la barra cambia a su parqueadero y al QR.
        vPrincipal.RevisarPestanas();
    }

    protected override void OnDisappearing()
    {
        _reloj?.Stop();
        _reloj = null;
        base.OnDisappearing();
    }

    private async void OnRefrescarClicked(object? sender, EventArgs e)
    {
        // El refresco debe quedarse en la pantalla donde está el usuario: si estaba viendo el
        // mapa sigue en el mapa (antes saltaba a «Mi parqueadero» cada vez que tenía un pase).
        _verMapa = panelMapa.IsVisible;

        // Se muestra de inmediato para que el usuario vea que sí está buscando el GPS, aunque
        // la lectura tarde unos segundos.
        lblUbicacion.Text = "Buscando tu posición real…";
        await CargarAsync(refrescarUbicacion: true);
    }

    /// <summary>
    /// Texto de la barra superior con la posición usada para calcular distancias. Cuando la
    /// lectura es del GPS se añade la precisión y la hora, para que se note que es una lectura
    /// nueva y no el punto de referencia de Quito.
    /// </summary>
    private static string DescribirUbicacion(bool real)
    {
        if (!real)
            return Ubicacion.Intentado
                ? "Sin GPS: calculamos desde el centro-norte de Quito"
                : "Ubicando tu posición…";

        var texto = $"Tu ubicación: {Ubicacion.Texto}";
        if (Ubicacion.Precision is > 0 and var precision)
            texto += $" · ±{precision:0} m";
        if (Ubicacion.LeidaEn is { } leida)
            texto += $" · {leida:HH:mm}";

        return texto;
    }

    // ================================================================ carga
    private async Task CargarAsync(bool refrescarUbicacion = false)
    {
        if (_cargando) return;
        _cargando = true;
        spinner.IsVisible = spinner.IsRunning = true;
        btnRefrescar.IsEnabled = false;

        try
        {
            var (lat, lng, real) = await Ubicacion.ObtenerAsync(pedirPermiso: true, refrescar: refrescarUbicacion);
            _origen = new Location(lat, lng);
            _textoUbicacion = DescribirUbicacion(real);
            lblUbicacion.Text = _textoUbicacion;

            if (_vehiculos.Count == 0)
                _vehiculos = await VehiculosSeguroAsync();

            var vehiculo = VehiculoPreferido();
            _perfilConductor = vehiculo?.Propietario;

            _sedes = await ApiCliente.SedesAsync(lat, lng, _tipo, _perfilConductor) ?? [];

            // Mantiene la selección si la sede sigue existiendo; si no, propone la más cercana.
            var anterior = _seleccionada is null ? null : _sedes.FirstOrDefault(s => s.Id == _seleccionada.Id);
            _seleccionada = anterior ?? _sedes.FirstOrDefault();

            PintarFiltrosSectores();
            AplicarFiltros();
            PintarMapaRuta();
            await CargarMiParqueaderoAsync();
            AplicarModo();
        }
        catch (Exception ex)
        {
            lblListaInfo.Text = "No se pudo cargar el mapa";
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            spinner.IsRunning = false;
            spinner.IsVisible = false;
            btnRefrescar.IsEnabled = true;
            _cargando = false;
        }
    }

    private VehiculoDto? VehiculoPreferido() =>
        _vehiculos.Where(v => v.Activo)
            .OrderByDescending(v => string.Equals(v.Tipo, _tipo, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault()
        ?? _vehiculos.FirstOrDefault();

    private static async Task<List<VehiculoDto>> VehiculosSeguroAsync()
    {
        try { return await ApiCliente.ListarVehiculosAsync() ?? []; }
        catch (Exception) { return []; }
    }

    // ================================================================ mi parqueadero
    /// <summary>
    /// Busca si el cliente ya tiene un puesto ocupado o una reserva. Si existe, la pantalla deja
    /// de ser el mapa (elegir parqueadero) y pasa a mostrar SU parqueadero: dónde está, en qué
    /// puesto, con qué placa, desde cuándo y cuánto lleva.
    /// </summary>
    private async Task CargarMiParqueaderoAsync()
    {
        try
        {
            var abiertos = await ApiCliente.TicketsAbiertosAsync() ?? [];
            var mias = abiertos.Where(t => Sesion.EsMio(t.UsuarioId, t.Usuario)).ToList();

            // Un puesto ya ocupado manda sobre una reserva para más tarde.
            var ticket = mias.FirstOrDefault(t => !t.EsReserva)
                         ?? mias.FirstOrDefault()
                         ?? abiertos.FirstOrDefault(t => string.IsNullOrWhiteSpace(t.Usuario));

            _pase = ticket is null ? null : await PaseDeTicketAsync(ticket);
        }
        catch (Exception)
        {
            _pase = null;
        }

        PintarMiParqueadero();
    }

    /// <summary>Datos completos del pase; sin conexión se arman con el ticket y el monto local.</summary>
    private async Task<PasePuestoDto> PaseDeTicketAsync(TicketDto ticket)
    {
        try
        {
            var pase = await ApiCliente.PaseAsync(ticket.Id);
            if (pase is not null) return pase;
        }
        catch (Exception)
        {
            // el QR y el monto también se calculan en el equipo, igual que en vQrPuesto
        }

        return new PasePuestoDto
        {
            TicketId = ticket.Id,
            TicketCodigo = ticket.Codigo,
            Placa = ticket.Placa,
            EspacioId = ticket.EspacioId,
            EspacioCodigo = ticket.EspacioCodigo ?? "",
            Zona = ticket.Zona ?? "",
            SedeNombre = ticket.Sede ?? "",
            TarifaHora = ticket.TarifaHora ?? 0,
            Entrada = ticket.Entrada,
            Estado = ticket.Estado,
            Usuario = ticket.Usuario ?? "",
            Modo = ticket.Modo,
            SalidaPrevista = ticket.SalidaPrevista,
            Descuento = Ui.DescuentoDe(_vehiculos
                .FirstOrDefault(v => string.Equals(v.Placa, ticket.Placa, StringComparison.OrdinalIgnoreCase))?.Propietario)
        };
    }

    /// <summary>Alterna entre el mapa (elegir parqueadero) y «mi parqueadero».</summary>
    private void AplicarModo()
    {
        var tiene = _pase is not null;
        var mapaVisible = !tiene || _verMapa;

        panelMapa.IsVisible = mapaVisible;
        panelBusqueda.IsVisible = mapaVisible;
        panelLista.IsVisible = mapaVisible;
        scrollMiParqueadero.IsVisible = !mapaVisible;
        btnMiPuesto.IsVisible = mapaVisible && tiene;

        if (mapaVisible)
        {
            lblTitulo.Text = "Elige tu parqueadero";
            // Se repone la leyenda con el conteo de parqueaderos, no solo la coordenada.
            lblUbicacion.Text = _leyendaMapa;
            ActualizarBannerEstacionado();
            PintarSeleccion();
            LanzarReloj(false);
            return;
        }

        var pase = _pase!;
        var reserva = EsReserva(pase);
        lblTitulo.Text = reserva ? "Tu reserva" : "Tu parqueadero";
        lblUbicacion.Text = reserva
            ? "Reserva confirmada · muestra el QR al llegar"
            : "Estás estacionado · el cobro corre desde la entrada";
        panelEstacionado.IsVisible = false;
        tarjetaSeleccion.IsVisible = false;

        RefrescarValores();
        LanzarReloj(true);

        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150),
            async () => await scrollMiParqueadero.ScrollToAsync(0, 0, false));

        // El mapa quedó oculto: al volver a mostrarlo hay que repintarlo.
    }

    /// <summary>Aviso compacto con el puesto del cliente, solo cuando está mirando el mapa.</summary>
    private void ActualizarBannerEstacionado()
    {
        var pase = _pase;
        if (pase is null)
        {
            panelEstacionado.IsVisible = false;
            return;
        }

        var puesto = PuestoTexto(pase);
        lblEstPlaza.Text = EsReserva(pase) ? $"Reserva en la plaza {puesto}" : $"Estás en la plaza {puesto}";
        lblEstValor.Text = MontoTexto(pase);
        lblEstDetalle.Text = string.Join(" · ", new[]
        {
            string.IsNullOrWhiteSpace(pase.SedeNombre) ? "" : pase.SedeNombre.Replace("Urban Park ", ""),
            pase.Zona,
            $"placa {pase.Placa}",
            $"ticket {pase.TicketCodigo}"
        }.Where(t => !string.IsNullOrWhiteSpace(t)));
        panelEstacionado.IsVisible = true;
    }

    private void PintarMiParqueadero()
    {
        contenidoMiParqueadero.Clear();
        _lblMontoMi = null;
        _lblTiempoMi = null;

        var pase = _pase;
        if (pase is null) return;

        contenidoMiParqueadero.Add(TarjetaMiParqueadero(pase));
        contenidoMiParqueadero.Add(new Label
        {
            Text = "Mientras tengas un puesto o una reserva, el mapa se oculta para mostrarte tu parqueadero.",
            FontSize = 10,
            TextColor = Gris,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(6, 0)
        });

        var verMapa = new Button
        {
            Text = "🗺 Ver el mapa y otros parqueaderos",
            Style = Ui.EstiloDe("BotonFantasma"),
            FontSize = 11,
            HeightRequest = 36
        };
        verMapa.Clicked += (_, _) =>
        {
            _verMapa = true;
            AplicarModo();
        };
        contenidoMiParqueadero.Add(verMapa);
    }

    private View TarjetaMiParqueadero(PasePuestoDto pase)
    {
        var reserva = EsReserva(pase);
        var nombre = string.IsNullOrWhiteSpace(pase.SedeNombre) ? "Urban Park" : pase.SedeNombre;

        // -------- encabezado: código de la sede y nombre del parqueadero
        var cabecera = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };
        cabecera.Add(new Border
        {
            BackgroundColor = Amarillo,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            WidthRequest = 52,
            HeightRequest = 52,
            VerticalOptions = LayoutOptions.Start,
            Content = new Label
            {
                Text = CodigoSede(pase),
                FontFamily = "OpenSansSemibold",
                FontSize = 14,
                TextColor = Tinta,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        }, 0, 0);
        cabecera.Add(new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = Limpiar(nombre),
                    FontFamily = "OpenSansSemibold",
                    FontSize = 16,
                    TextColor = Tinta,
                    LineBreakMode = LineBreakMode.TailTruncation
                },
                new Label
                {
                    Text = string.IsNullOrWhiteSpace(pase.SedeDireccion)
                        ? "Dirección no disponible"
                        : "📍 " + pase.SedeDireccion,
                    FontSize = 11,
                    TextColor = Color.FromArgb("#475569"),
                    LineBreakMode = LineBreakMode.WordWrap
                }
            }
        }, 1, 0);

        // -------- estado: ocupado o reserva
        var estado = new Border
        {
            BackgroundColor = reserva ? Color.FromArgb("#FEF4E3") : Color.FromArgb("#E7F8EE"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 9 },
            Padding = new Thickness(9, 5),
            HorizontalOptions = LayoutOptions.Start,
            Content = new Label
            {
                Text = reserva ? "🕒 Reserva confirmada" : "🅿 Estacionado ahora",
                FontFamily = "OpenSansSemibold",
                FontSize = 11,
                TextColor = reserva ? Color.FromArgb("#B45309") : Verde
            }
        };

        // -------- datos del puesto
        var detalles = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Fila("Puesto", PuestoTexto(pase), "Zona", Texto(pase.Zona)),
                Fila("Placa", Texto(pase.Placa), "Ticket", Texto(pase.TicketCodigo)),
                Fila("Nivel", Ui.Nivel(pase.Nivel), "Vehículo", Ui.IconoTipo(pase.TipoVehiculo) + " " + Texto(pase.TipoVehiculo)),
                reserva
                    ? Fila("Ingreso", Fecha(pase.Entrada), "Salida", Fecha(pase.SalidaPrevista))
                    : Fila("Entrada", Fecha(pase.Entrada), "Tarifa", Ui.Tarifa(pase.TarifaHora))
            }
        };

        // -------- valor y tiempo (se refrescan cada segundo)
        _lblMontoMi = new Label
        {
            FontFamily = "OpenSansSemibold",
            FontSize = 26,
            TextColor = Color.FromArgb("#FFE14A")
        };
        _lblTiempoMi = new Label
        {
            FontSize = 11,
            TextColor = Color.FromArgb("#C9D6F0")
        };

        var bloqueValor = new Border
        {
            BackgroundColor = AzulOscuro,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(12, 10),
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label
                    {
                        Text = reserva ? "VALOR ESTIMADO DE LA RESERVA" : "VALOR A PAGAR EN ESTE MOMENTO",
                        FontSize = 9,
                        TextColor = Color.FromArgb("#9FB3D9"),
                        CharacterSpacing = 1
                    },
                    _lblMontoMi,
                    _lblTiempoMi
                }
            }
        };

        // -------- acciones
        var verQr = new Button
        {
            Text = "Ver mi QR",
            Style = Ui.EstiloDe("BotonAcento"),
            FontSize = 12,
            HeightRequest = 40,
            CornerRadius = 10
        };
        verQr.Clicked += async (_, _) => await AbrirQrAsync();

        var llegar = new Button
        {
            Text = "🗺 Cómo llegar",
            Style = Ui.EstiloDe("BotonContorno"),
            FontSize = 12,
            HeightRequest = 40,
            CornerRadius = 10
        };
        llegar.Clicked += async (_, _) => await AbrirRutaAsync();

        var acciones = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8
        };
        acciones.Add(verQr, 0, 0);
        acciones.Add(llegar, 1, 0);

        var extras = new VerticalStackLayout { Spacing = 8 };
        if (reserva)
        {
            var activar = new Button
            {
                Text = "Ya llegué: activar",
                BackgroundColor = Ui.ColorDe("Primario"),
                TextColor = Colors.White,
                FontFamily = "OpenSansSemibold",
                FontSize = 12,
                HeightRequest = 40,
                CornerRadius = 10
            };
            activar.Clicked += async (_, _) => await ActivarReservaAsync();

            var anular = new Button
            {
                Text = "Anular reserva",
                Style = Ui.EstiloDe("BotonPeligro"),
                FontSize = 11,
                HeightRequest = 36,
                CornerRadius = 10
            };
            anular.Clicked += async (_, _) => await AnularReservaAsync();

            extras.Children.Add(activar);
            extras.Children.Add(anular);
        }
        else
        {
            var pagar = new Button
            {
                Text = "Pagar y salir",
                BackgroundColor = Ui.ColorDe("Primario"),
                TextColor = Colors.White,
                FontFamily = "OpenSansSemibold",
                FontSize = 12,
                HeightRequest = 40,
                CornerRadius = 10
            };
            pagar.Clicked += async (_, _) => await Navigation.PushAsync(new vTickets());
            extras.Children.Add(pagar);
        }

        return new Border
        {
            BackgroundColor = FondoTarjeta,
            Stroke = Color.FromArgb("#E5E9F0"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(14),
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    cabecera,
                    estado,
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E5E9F0") },
                    detalles,
                    bloqueValor,
                    acciones,
                    extras
                }
            }
        };
    }

    private static bool EsReserva(PasePuestoDto pase) =>
        string.Equals(pase.Estado, "RESERVADO", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(pase.Modo, "RESERVA", StringComparison.OrdinalIgnoreCase);

    private static string PuestoTexto(PasePuestoDto pase) =>
        string.IsNullOrWhiteSpace(pase.EspacioCodigo) ? $"#{pase.EspacioId}" : pase.EspacioCodigo;

    private string CodigoSede(PasePuestoDto pase)
    {
        if (!string.IsNullOrWhiteSpace(pase.SedeCodigo)) return pase.SedeCodigo;
        var sede = _sedes.FirstOrDefault(s =>
            string.Equals(s.Nombre, pase.SedeNombre, StringComparison.OrdinalIgnoreCase));
        return sede?.Codigo ?? "P";
    }

    private static string Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? "—" : valor;

    private static string Fecha(DateTime? valor) =>
        valor is null ? "—" : valor.Value.ToString("dd/MM HH:mm");

    /// <summary>Franja reservada: horas guardadas y monto estimado.</summary>
    private static (int Horas, double Monto) FranjaReserva(PasePuestoDto pase)
    {
        var inicio = pase.Entrada;
        var fin = pase.SalidaPrevista ?? inicio.AddHours(1);
        var horas = Math.Max(1, pase.HorasPrevistas > 0
            ? pase.HorasPrevistas
            : (int)Math.Ceiling((fin - inicio).TotalMinutes / 60.0));
        var monto = pase.MontoPrevisto > 0
            ? pase.MontoPrevisto
            : Math.Round(horas * pase.TarifaHora * (1 - pase.Descuento / 100.0), 2);
        return (horas, monto);
    }

    private static string MontoTexto(PasePuestoDto pase) =>
        EsReserva(pase)
            ? Ui.Moneda(FranjaReserva(pase).Monto)
            : Ui.Moneda(Ui.MontoEstimado(pase.Entrada, pase.TarifaHora, pase.Descuento));

    private void RefrescarValores()
    {
        var pase = _pase;
        if (pase is null) return;

        if (_lblMontoMi is not null) _lblMontoMi.Text = MontoTexto(pase);
        if (_lblTiempoMi is null) return;

        if (EsReserva(pase))
        {
            var (horas, _) = FranjaReserva(pase);
            var falta = pase.Entrada - DateTime.Now;
            _lblTiempoMi.Text = falta > TimeSpan.Zero
                ? $"Empieza en {(int)falta.TotalHours:00}:{falta.Minutes:00}:{falta.Seconds:00} · {horas} h reservadas"
                : $"Reserva en curso · {horas} h · actívala al llegar";
            return;
        }

        var dentro = DateTime.Now - pase.Entrada;
        if (dentro < TimeSpan.Zero) dentro = TimeSpan.Zero;
        var detalle = $"{(int)dentro.TotalHours:00}:{dentro.Minutes:00}:{dentro.Seconds:00} dentro · {Ui.Tarifa(pase.TarifaHora)}";
        if (pase.Descuento > 0) detalle += $" − {pase.Descuento}%";
        _lblTiempoMi.Text = detalle;
    }

    private void LanzarReloj(bool activo)
    {
        if (!activo || _pase is null)
        {
            _reloj?.Stop();
            return;
        }

        if (_reloj is null)
        {
            _reloj = Dispatcher.CreateTimer();
            _reloj.Interval = TimeSpan.FromSeconds(1);
            _reloj.Tick += (_, _) => RefrescarValores();
        }

        if (!_reloj.IsRunning) _reloj.Start();
    }

    private async void OnVolverMiParqueaderoClicked(object? sender, EventArgs e)
    {
        _verMapa = false;
        AplicarModo();
        await Task.CompletedTask;
    }

    private async Task AbrirQrAsync()
    {
        if (_pase is null) return;
        if (vPrincipal.IrAPestana("QR"))
            return;

        await Navigation.PushAsync(new vQrPuesto(_pase));
    }

    private async Task AbrirRutaAsync()
    {
        var pase = _pase;
        if (pase is null) return;

        try
        {
            vLlegada.SedeDestacarAlMostrar = pase.SedeId > 0 ? pase.SedeId : null;
            if (vPrincipal.IrAPestana("Llegar"))
                return;

            await Navigation.PushAsync(new vLlegada(vLlegada.SedeDestacarAlMostrar));
        }
        catch (Exception ex)
        {
            await Ui.Error(this, "No se pudo abrir la ruta: " + ex.Message);
        }
    }

    private async Task ActivarReservaAsync()
    {
        var pase = _pase;
        if (pase is null) return;

        try
        {
            await ApiCliente.ActivarPaseAsync(pase.TicketId);
            await Ui.Exito(this, "Reserva activada. Tu tiempo empieza a correr ahora.");
            await CargarAsync();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
    }

    private async Task AnularReservaAsync()
    {
        var pase = _pase;
        if (pase is null) return;

        if (!await Ui.Confirmar(this, "Anular reserva",
                "Se liberará el puesto y no se cobrará nada. ¿Continuar?"))
            return;

        try
        {
            await ApiCliente.CancelarPaseAsync(pase.TicketId);
            await Ui.Exito(this, "Reserva anulada. El puesto quedó libre.");
            await CargarAsync();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
    }

    // ================================================================ tipos
    private void PintarTipos()
    {
        contenedorTipos.Children.Clear();
        foreach (var (codigo, texto) in _tipos)
        {
            var elegido = _tipo == codigo;
            var chip = new Border
            {
                BackgroundColor = elegido ? Amarillo : Colors.White,
                Stroke = elegido ? Amarillo : Color.FromArgb("#CBD5E1"),
                StrokeThickness = elegido ? 1.5 : 1,
                StrokeShape = new RoundRectangle { CornerRadius = 20 },
                Padding = new Thickness(12, 6),
                Margin = new Thickness(0, 0, 6, 0),
                Content = new Label
                {
                    Text = texto,
                    FontSize = 11,
                    FontFamily = "OpenSansSemibold",
                    TextColor = elegido ? Tinta : Color.FromArgb("#334155"),
                    LineBreakMode = LineBreakMode.NoWrap
                }
            };
            if (elegido)
            {
                chip.Shadow = new Shadow
                {
                    Brush = Amarillo,
                    Offset = new Point(0, 2),
                    Radius = 6,
                    Opacity = 0.35f
                };
            }
            FlexLayout.SetShrink(chip, 0);
            FlexLayout.SetGrow(chip, 0);

            var toque = new TapGestureRecognizer();
            toque.Tapped += async (_, _) =>
            {
                if (_tipo == codigo) return;
                _tipo = codigo;
                PintarTipos();
                await CargarAsync();
            };
            chip.GestureRecognizers.Add(toque);
            contenedorTipos.Children.Add(chip);
        }
    }

    // ================================================================ filtros (como Llegar)
    private bool EsFiltroTodos() =>
        string.Equals(_filtroSector, FiltroTodos, StringComparison.OrdinalIgnoreCase);

    private void OnBuscarTextChanged(object? sender, TextChangedEventArgs e)
    {
        _busqueda = (txtBuscar.Text ?? "").Trim();

        // Al escribir una búsqueda se suelta el filtro de sector: si no, "San Blas" dentro del
        // sector "Centro Histórico" devolvía cero resultados y no había nada que abrir.
        if (_busqueda.Length > 0 && !EsFiltroTodos())
        {
            _filtroSector = FiltroTodos;
            _pinElegidoEnMapa = false;
            ActualizarChipsSector();
        }

        AplicarFiltros();
    }

    private void PintarFiltrosSectores()
    {
        contenedorFiltros.Clear();
        _chipClaves.Clear();

        var sectores = _sedes
            .Select(s => (s.Sector ?? "").Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AgregarChipSector(FiltroTodos, "Todos");
        foreach (var sector in sectores)
            AgregarChipSector(sector, sector);
    }

    private void AgregarChipSector(string clave, string texto)
    {
        var btn = new Button
        {
            Text = texto,
            Style = EstiloChipSector(string.Equals(_filtroSector, clave, StringComparison.OrdinalIgnoreCase))
        };
        btn.Clicked += (_, _) =>
        {
            _filtroSector = clave;
            if (string.Equals(clave, FiltroTodos, StringComparison.OrdinalIgnoreCase))
                _pinElegidoEnMapa = false;
            ActualizarChipsSector();
            AplicarFiltros();
        };
        _chipClaves[btn] = clave;
        contenedorFiltros.Add(btn);
    }

    private static Style? EstiloChipSector(bool activo) =>
        Application.Current?.Resources.TryGetValue(activo ? "FiltroChipActivo" : "FiltroChip", out var s) == true && s is Style st
            ? st
            : Ui.EstiloDe(activo ? "BotonPrimario" : "BotonSecundario");

    private void ActualizarChipsSector()
    {
        foreach (var (btn, clave) in _chipClaves)
        {
            var activo = string.Equals(_filtroSector, clave, StringComparison.OrdinalIgnoreCase);
            btn.Style = EstiloChipSector(activo);
        }
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

        if (_sedes.Count == 0)
            lblListaInfo.Text = "No hay parqueaderos con plazas para ese vehículo.";
        else if (_visibles.Count == 0)
            lblListaInfo.Text = string.IsNullOrEmpty(_busqueda)
                ? "Ningún parqueadero de este sector tiene plazas para ese vehículo."
                : $"Ningún parqueadero coincide con «{_busqueda}». Borra la búsqueda o pulsa ↻.";
        else
        {
            lblListaInfo.Text = _visibles.Count == _sedes.Count
                ? $"Mostrando los {_visibles.Count} parqueaderos."
                : $"Mostrando {_visibles.Count} de {_sedes.Count} parqueaderos.";
        }

        PintarLista();

        if (_seleccionada is not null && !_visibles.Any(v => v.Id == _seleccionada.Id))
            Seleccionar(_visibles.FirstOrDefault(), false);
        else if (_seleccionada is null && _visibles.Count > 0)
            Seleccionar(_visibles[0], false);
        else
            PintarSeleccion();

        PintarMapaRuta();
    }

    // ================================================================ mapa y lista
    /// <summary>
    /// Texto que describe la posición usada para calcular. Se guarda aparte porque el evento
    /// Navigated del WebView llegaba con resultado distinto de Success y pisaba el rótulo con
    /// un «no se pudo cargar el mapa» que no era cierto.
    /// </summary>
    private string _leyendaMapa = "Ubicando tu posición…";

    private void PintarMapaRuta()
    {
        if (_origen is null)
            _origen = new Location(Ubicacion.Latitud, Ubicacion.Longitud);

        var conCoords = _visibles.Where(s => s.Latitud != 0 || s.Longitud != 0).ToList();
        if (conCoords.Count == 0)
            return;

        mapaSpinner.IsVisible = mapaSpinner.IsRunning = true;

        var sedeMapa = _seleccionada ?? conCoords[0];
        string html;

        if (EsFiltroTodos() && !_pinElegidoEnMapa)
        {
            html = MapaGoogle.HtmlMarcadoresSinRuta(
                _origen,
                conCoords,
                _seleccionada?.Id,
                Ubicacion.Texto,
                Ubicacion.EsReal,
                Ubicacion.Precision);
            _leyendaMapa = Ubicacion.EsReal
                ? $"{_textoUbicacion} · {conCoords.Count} parqueaderos (sin rutas)"
                : $"{_textoUbicacion} · {conCoords.Count} parqueadero(s)";
        }
        else if (sedeMapa.Latitud != 0 || sedeMapa.Longitud != 0)
        {
            html = MapaGoogle.HtmlEmbebido(
                MapaGoogle.UrlRuta(_origen, new Location(sedeMapa.Latitud, sedeMapa.Longitud)));
            _leyendaMapa = Ubicacion.EsReal
                ? $"{_textoUbicacion} · ruta a pie a {Limpiar(sedeMapa.Nombre)}"
                : $"{_textoUbicacion} · ruta de ejemplo (sin GPS real)";
        }
        else
            return;

        lblUbicacion.Text = _leyendaMapa;
        mapaRuta.Source = new HtmlWebViewSource { Html = html };
    }

    private void OnMapaNavegado(object? sender, WebNavigatedEventArgs e)
    {
        mapaSpinner.IsRunning = false;
        mapaSpinner.IsVisible = false;

        // Con un resultado correcto se repone la descripción de la posición. Solo si el mapa
        // realmente falla se avisa, y se deja claro que los datos de la lista siguen valiendo.
        if (e.Result == WebNavigationResult.Success)
        {
            lblUbicacion.Text = _leyendaMapa;
            return;
        }

        if (e.Result == WebNavigationResult.Failure)
            lblUbicacion.Text = "No se pudo dibujar el mapa. Pulsa ↻ o revisa la conexión.";
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

        ActualizarChipsSector();
        _seleccionada = sede;
        AplicarFiltros();
    }

    private void PintarLista()
    {
        contenedorSedes.Clear();
        foreach (var sede in _visibles)
            contenedorSedes.Add(FilaSede(sede));
    }

    private View FilaSede(SedeDto sede)
    {
        var elegida = _seleccionada?.Id == sede.Id;
        var libres = sede.PlazasLibresVehiculo > 0 ? sede.PlazasLibresVehiculo : sede.PlazasLibres;

        var tarjeta = new Border
        {
            Style = Ui.EstiloDe("TarjetaClara"),
            Padding = new Thickness(10, 8),
            Stroke = Ui.ColorDe(elegida ? "Primario" : "BordeClaro"),
            StrokeThickness = elegida ? 2 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Opacity = libres == 0 ? 0.75 : 1,
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
                                Text = Limpiar(sede.Nombre),
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
                                Text = DetalleSede(sede, libres),
                                FontSize = 10,
                                TextColor = Ui.ColorDe("TextoSuave")
                            }
                        }
                    },
                    new Label
                    {
                        Text = elegida ? "✓" : "›",
                        FontSize = 18,
                        TextColor = Ui.ColorDe(elegida ? "Primario" : "TextoMedio"),
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };

        var toque = new TapGestureRecognizer();
        toque.Tapped += (_, _) => Seleccionar(sede, true);
        tarjeta.GestureRecognizers.Add(toque);
        return tarjeta;
    }

    private static string DetalleSede(SedeDto sede, int libres)
    {
        var partes = new List<string>();
        if (!string.IsNullOrWhiteSpace(sede.DistanciaTexto))
            partes.Add(sede.DistanciaTexto);
        if (!string.IsNullOrWhiteSpace(sede.CaminandoTexto))
            partes.Add(sede.CaminandoTexto);
        partes.Add(libres == 0 ? "Lleno" : $"{libres} libres");
        if (sede.TarifaBase > 0)
            partes.Add(Ui.Tarifa(sede.TarifaBase));
        return string.Join(" · ", partes);
    }

    /// <summary>Fila de datos con dos campos (etiqueta + valor).</summary>
    private static View Fila(string etiqueta1, string valor1, string etiqueta2, string valor2)
    {
        var grilla = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8
        };
        grilla.Add(Campo(etiqueta1, valor1), 0, 0);
        grilla.Add(Campo(etiqueta2, valor2), 1, 0);
        return grilla;
    }

    private static View Campo(string etiqueta, string valor) => new VerticalStackLayout
    {
        Spacing = 0,
        Children =
        {
            new Label
            {
                Text = etiqueta.ToUpperInvariant(),
                FontSize = 9,
                TextColor = Color.FromArgb("#64748B")
            },
            new Label
            {
                Text = valor,
                FontFamily = "OpenSansSemibold",
                FontSize = 12,
                TextColor = Tinta,
                LineBreakMode = LineBreakMode.TailTruncation
            }
        }
    };

    private static string Limpiar(string nombre) => nombre.Replace("Urban Park ", "Urban Park · ");

    private void Seleccionar(SedeDto? sede, bool pintarLista)
    {
        _seleccionada = sede;
        if (pintarLista) PintarLista();
        PintarSeleccion();
        PintarMapaRuta();
    }

    private void PintarSeleccion()
    {
        if (_seleccionada is null)
        {
            tarjetaSeleccion.IsVisible = false;
            return;
        }

        var s = _seleccionada;
        lblTitulo.Text = Limpiar(s.Nombre);
        lblSelNombre.Text = Limpiar(s.Nombre);
        lblDistancia.Text = string.IsNullOrWhiteSpace(s.DistanciaTexto) ? "—" : s.DistanciaTexto;
        lblSelDetalle.Text = string.Join(" · ", new[]
        {
            s.CaminandoTexto,
            s.LibreTexto,
            Ui.Tarifa(s.TarifaBase),
            string.IsNullOrWhiteSpace(s.Direccion) ? null : s.Direccion
        }.Where(t => !string.IsNullOrWhiteSpace(t)));
        tarjetaSeleccion.IsVisible = true;
    }

    private async Task AbrirPuestosAsync(SedeDto sede)
    {
        // Antes, si la sede aparecía sin plazas libres, el botón se quedaba en un aviso y no
        // dejaba ver el plano. El dibujo siempre sirve (aunque esté lleno), así que se entra
        // igual y el propio plano muestra el estado real de cada bahía.
        await Navigation.PushAsync(new vPlazas(sede));
    }

    private async void OnNavegarClicked(object? sender, EventArgs e)
    {
        if (_seleccionada is null)
        {
            await Ui.Informar(this, "Elige un parqueadero de la lista.");
            return;
        }

        var origen = _origen ?? new Location(Ubicacion.Latitud, Ubicacion.Longitud);

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
            // URL de respaldo abajo
        }

        var cultura = CultureInfo.InvariantCulture;
        var desde = string.Create(cultura, $"{origen.Latitude},{origen.Longitude}");
        var destino = Uri.EscapeDataString(
            $"{_seleccionada.Nombre} {_seleccionada.Direccion}".Trim());
        await Launcher.Default.OpenAsync(
            $"https://www.google.com/maps/dir/?api=1&origin={desde}&destination={destino}&travelmode=walking");
    }

    private async void OnVerPuestosClicked(object? sender, EventArgs e)
    {
        // Antes volvía en silencio si no había nada elegido y parecía que el botón no hacía nada.
        if (_seleccionada is null)
        {
            await Ui.Informar(this, "Elige un parqueadero de la lista o toca un pin del mapa y vuelve a pulsar «Ver puestos».");
            return;
        }

        await AbrirPuestosAsync(_seleccionada);
    }

    private async void OnTodasLasPlazasClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new vPlazas());

    private async void OnEstacionadoTapped(object? sender, TappedEventArgs e) => await AbrirQrAsync();

    // ================================================================ IA
    private async void OnIaClicked(object? sender, EventArgs e)
    {
        if (_sedes.Count == 0)
        {
            await Ui.Informar(this, "Todavía no cargaron los parqueaderos. Pulsa ↻ y vuelve a intentar.");
            return;
        }

        btnIa.IsEnabled = false;
        var textoOriginal = btnIa.Text;
        btnIa.Text = "Pensando…";
        try
        {
            var (lat, lng, _) = await Ubicacion.ObtenerAsync(true);
            var vehiculo = VehiculoPreferido();
            var tipo = vehiculo?.Tipo is { Length: > 0 } t ? t : _tipo;
            var recomendacion = await ApiCliente.RecomendarSedeAsync(tipo ?? "AUTO", lat, lng,
                vehiculo?.Propietario, vehiculo?.Placa);

            PintarRecomendacion(recomendacion);

            var elegida = _sedes.FirstOrDefault(s => s.Id == recomendacion.SedeId);
            if (elegida is not null) Seleccionar(elegida, true);
        }
        catch (Exception ex)
        {
            PintarErrorIa(ex is ApiExcepcion ? ex.Message : "No se pudo consultar la IA: " + ex.Message);
        }
        finally
        {
            btnIa.IsEnabled = true;
            btnIa.Text = textoOriginal;
        }
    }

    private void PintarRecomendacion(RecomendacionSedeDto r)
    {
        contenidoIa.Clear();
        panelIa.IsVisible = true;
        panelIa.Stroke = Amarillo;

        contenidoIa.Add(new Label
        {
            Text = "🤖 EL PARQUEADERO QUE TE CONVIENE",
            Style = Ui.EstiloDe("EncabezadoTabla"),
            TextColor = Amarillo
        });

        contenidoIa.Add(new Label
        {
            Text = r.Nombre,
            FontFamily = "OpenSansSemibold",
            FontSize = 17,
            TextColor = Claro
        });

        contenidoIa.Add(new Label
        {
            Text = $"📍 {r.Direccion}",
            FontSize = 12,
            TextColor = Color.FromArgb("#B6C2D1")
        });

        contenidoIa.Add(new Label
        {
            Text = string.Join("  ·  ", new[]
            {
                $"{r.DistanciaKm:0.0} km",
                $"{r.TiempoCaminandoMin} min a pie",
                $"{r.PlazasLibres} libres",
                Ui.Tarifa(r.TarifaBase),
                $"{r.Puntaje}/100"
            }),
            FontSize = 11,
            TextColor = Amarillo
        });

        contenidoIa.Add(new Label
        {
            Text = r.Motivo,
            FontFamily = "OpenSansSemibold",
            FontSize = 13,
            TextColor = Claro
        });

        if (!string.IsNullOrWhiteSpace(r.ExplicacionIa))
        {
            contenidoIa.Add(new Label
            {
                Text = r.ExplicacionIa,
                FontSize = 12,
                TextColor = Color.FromArgb("#B6C2D1"),
                LineBreakMode = LineBreakMode.WordWrap
            });
        }

        if (!string.IsNullOrWhiteSpace(r.ProveedorIa))
        {
            contenidoIa.Add(new Label
            {
                Text = $"Motor: {r.ProveedorIa}",
                FontSize = 9,
                TextColor = Gris
            });
        }

        if (r.Alternativas.Count > 0)
        {
            contenidoIa.Add(new Label
            {
                Text = "Otras opciones",
                Style = Ui.EstiloDe("EncabezadoTabla"),
                TextColor = Gris,
                Margin = new Thickness(0, 4, 0, 0)
            });

            foreach (var alternativa in r.Alternativas.Take(3))
            {
                contenidoIa.Add(FilaAlternativa(alternativa));
            }
        }

        var ver = new Button
        {
            Text = "Ver puestos de esta sede",
            BackgroundColor = Amarillo,
            TextColor = Tinta,
            FontFamily = "OpenSansSemibold",
            FontSize = 12,
            CornerRadius = 10,
            HeightRequest = 36,
            Margin = new Thickness(0, 6, 0, 0)
        };
        ver.Clicked += async (_, _) =>
        {
            var sede = _sedes.FirstOrDefault(s => s.Id == r.SedeId);
            if (sede is not null) await AbrirPuestosAsync(sede);
        };
        contenidoIa.Add(ver);
    }

    private View FilaAlternativa(SedeCandidataDto alternativa)
    {
        var fila = new Border
        {
            BackgroundColor = SuperficieAlta,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(8, 6),
            Content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 8,
                Children =
                {
                    new VerticalStackLayout
                    {
                        Spacing = 1,
                        Children =
                        {
                            new Label
                            {
                                Text = alternativa.Nombre.Replace("Urban Park ", ""),
                                FontFamily = "OpenSansSemibold",
                                FontSize = 12,
                                TextColor = Claro
                            },
                            new Label
                            {
                                Text = alternativa.Motivo,
                                FontSize = 10,
                                TextColor = Gris
                            }
                        }
                    },
                    new Label
                    {
                        Text = $"{alternativa.Puntaje}",
                        FontFamily = "OpenSansSemibold",
                        FontSize = 14,
                        TextColor = Amarillo,
                        HorizontalTextAlignment = TextAlignment.End,
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };

        var toque = new TapGestureRecognizer();
        toque.Tapped += (_, _) =>
        {
            var sede = _sedes.FirstOrDefault(s => s.Id == alternativa.SedeId);
            if (sede is not null) Seleccionar(sede, true);
        };
        fila.GestureRecognizers.Add(toque);
        return fila;
    }

    private void PintarErrorIa(string mensaje)
    {
        contenidoIa.Clear();
        panelIa.IsVisible = true;
        panelIa.Stroke = Ui.ColorDe("Peligro");

        contenidoIa.Add(new Label
        {
            Text = "No se pudo recomendar",
            FontFamily = "OpenSansSemibold",
            FontSize = 14,
            TextColor = Ui.ColorDe("Peligro")
        });
        contenidoIa.Add(new Label
        {
            Text = mensaje,
            FontSize = 12,
            TextColor = Color.FromArgb("#B6C2D1"),
            LineBreakMode = LineBreakMode.WordWrap
        });
        contenidoIa.Add(new Label
        {
            Text = "Mientras tanto puedes elegir un parqueadero de la lista.",
            FontSize = 11,
            TextColor = Gris
        });
    }
}
