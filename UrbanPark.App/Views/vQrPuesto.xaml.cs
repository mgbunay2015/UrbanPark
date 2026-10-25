using System.Globalization;
using QRCoder;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// QR del puesto. Se abre en el instante en que el cliente elige una plaza: muestra los datos
/// (parqueadero, dirección, puesto, placa, entrada) y el valor a pagar en ese momento, que se
/// recalcula cada segundo con la misma fórmula del backend.
/// </summary>
public partial class vQrPuesto : ContentPage
{
    private readonly PasePuestoDto _pase;
    private IDispatcherTimer? _reloj;
    private DateTime _ultimoRefresco = DateTime.MinValue;
    private double _montoPintado = -1;
    private bool _refrescando;
    private bool _finalizado;
    private bool _activando;

    /// <summary>Se entra desde el pase recién creado al elegir el puesto.</summary>
    public vQrPuesto(PasePuestoDto pase)
    {
        InitializeComponent();
        _pase = pase;
        PintarTodo();
    }

    /// <summary>Se entra desde un ticket abierto (pestaña Tickets): el pase se pide al API.</summary>
    public vQrPuesto(TicketDto ticket)
    {
        InitializeComponent();
        _pase = new PasePuestoDto
        {
            TicketId = ticket.Id,
            TicketCodigo = ticket.Codigo,
            Placa = ticket.Placa,
            EspacioId = ticket.EspacioId,
            EspacioCodigo = ticket.EspacioCodigo ?? "",
            Zona = ticket.Zona ?? "",
            TarifaHora = ticket.TarifaHora ?? 0,
            Entrada = ticket.Entrada,
            Estado = ticket.Estado,
            Usuario = ticket.Usuario ?? ""
        };
        PintarTodo();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        IniciarReloj();
        await RefrescarAsync(forzar: true);
    }

    protected override void OnDisappearing()
    {
        _reloj?.Stop();
        base.OnDisappearing();
    }

    // ================================================================ pintado
    private void PintarTodo()
    {
        lblSede.Text = string.IsNullOrWhiteSpace(_pase.SedeNombre) ? "Urban Park" : _pase.SedeNombre;
        lblPuesto.Text = string.IsNullOrWhiteSpace(_pase.EspacioCodigo)
            ? $"Puesto {_pase.EspacioId}"
            : _pase.EspacioCodigo;
        lblDireccion.Text = string.IsNullOrWhiteSpace(_pase.SedeDireccion)
            ? "Consultando la dirección del parqueadero…"
            : "📍 " + _pase.SedeDireccion;

        if (EsReserva)
        {
            lblSubtitulo.Text = "Reserva confirmada · muestra este QR al llegar";
            lblEtiquetaMonto.Text = "VALOR ESTIMADO DE LA RESERVA";
            btnPagar.Text = "Ya llegué: activar";
        }
        else
        {
            lblSubtitulo.Text = "Muestra este QR en la entrada del parqueadero";
            lblEtiquetaMonto.Text = "VALOR A PAGAR EN ESTE MOMENTO";
            btnPagar.Text = "Cerrar y pagar";
        }

        PintarMonto();
        PintarDatos();
    }

    private bool EsReserva =>
        string.Equals(_pase.Estado, "RESERVADO", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_pase.Modo, "RESERVA", StringComparison.OrdinalIgnoreCase);

    private void PintarMonto()
    {
        var descuento = _pase.Descuento;

        if (EsReserva)
        {
            // Reserva: se muestra el importe de la franja guardada y cuánto falta para entrar.
            var inicio = _pase.Entrada;
            var fin = _pase.SalidaPrevista ?? inicio.AddHours(1);
            var horas = Math.Max(1, _pase.HorasPrevistas > 0
                ? _pase.HorasPrevistas
                : (int)Math.Ceiling((fin - inicio).TotalMinutes / 60.0));
            var monto = _pase.MontoPrevisto > 0
                ? _pase.MontoPrevisto
                : Math.Round(horas * _pase.TarifaHora * (1 - descuento / 100.0), 2);

            lblMonto.Text = Ui.Moneda(monto);

            var falta = inicio - DateTime.Now;
            lblTiempo.Text = falta > TimeSpan.Zero
                ? $"Empieza en {(int)falta.TotalHours:00}:{falta.Minutes:00}:{falta.Seconds:00}"
                : "Reserva en curso · actívala al llegar";

            var texto = $"{horas} h × {Ui.Moneda(_pase.TarifaHora)}/h";
            if (descuento > 0) texto += $" − {descuento}%";
            texto += $" · {inicio:dd/MM HH:mm} a {fin:dd/MM HH:mm}";
            lblCalculo.Text = texto;

            if (Math.Abs(monto - _montoPintado) > 0.004)
            {
                _montoPintado = monto;
                var payload = string.IsNullOrWhiteSpace(_pase.QrPayload) ? PayloadLocal() : _pase.QrPayload;
                imgQr.Source = ImagenQr(payload);
            }

            return;
        }

        var montoAhora = Ui.MontoEstimado(_pase.Entrada, _pase.TarifaHora, descuento);
        lblMonto.Text = Ui.Moneda(montoAhora);

        var transcurrido = DateTime.Now - _pase.Entrada;
        if (transcurrido < TimeSpan.Zero) transcurrido = TimeSpan.Zero;
        lblTiempo.Text = $"{(int)transcurrido.TotalHours:00}:{transcurrido.Minutes:00}:{transcurrido.Seconds:00} dentro";

        var horasDentro = Math.Max(1, (int)Math.Ceiling(Math.Max(1, transcurrido.TotalMinutes) / 60.0));
        var porHora = _pase.TarifaHora;
        var detalle = $"{horasDentro} h × {Ui.Moneda(porHora)}/h";
        if (descuento > 0)
            detalle += $" − {descuento}% de descuento";
        detalle += $" · {Ui.Tarifa(_pase.TarifaHora)}";
        lblCalculo.Text = detalle;

        // El QR se regenera solo cuando cambia el valor (cada hora) o cuando llega un pase nuevo.
        if (Math.Abs(montoAhora - _montoPintado) > 0.004)
        {
            _montoPintado = montoAhora;
            var payload = string.IsNullOrWhiteSpace(_pase.QrPayload) ? PayloadLocal() : _pase.QrPayload;
            imgQr.Source = ImagenQr(payload);
        }
    }

    private void PintarDatos()
    {
        contenidoDatos.Clear();
        contenidoDatos.Add(Encabezado("DATOS DEL PASE"));
        contenidoDatos.Add(Fila("Placa", _pase.Placa, "Ticket", _pase.TicketCodigo));
        contenidoDatos.Add(Fila("Puesto", string.IsNullOrWhiteSpace(_pase.EspacioCodigo) ? "—" : _pase.EspacioCodigo,
            "Zona", string.IsNullOrWhiteSpace(_pase.Zona) ? "—" : _pase.Zona));
        contenidoDatos.Add(Fila("Tipo", _pase.TipoVehiculo, "Nivel", Ui.Nivel(_pase.Nivel)));
        contenidoDatos.Add(Fila("Entrada", _pase.Entrada.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-EC")),
            "Tarifa", Ui.Tarifa(_pase.TarifaHora)));

        if (_pase.Descuento > 0)
        {
            contenidoDatos.Add(Fila("Descuento", $"{_pase.Descuento}%", "Por hora", Ui.Moneda(_pase.MontoPorHora)));
        }

        if (!string.IsNullOrWhiteSpace(_pase.SedeSector))
            contenidoDatos.Add(Fila("Sector", _pase.SedeSector, "Usuario", _pase.Usuario));
    }

    private static Label Encabezado(string texto) => new()
    {
        Text = texto,
        Style = Ui.EstiloDe("EncabezadoTabla"),
        Margin = new Thickness(0, 0, 0, 4)
    };

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
                TextColor = Ui.ColorDe("TextoClaroSuave")
            },
            new Label
            {
                Text = string.IsNullOrWhiteSpace(valor) ? "—" : valor,
                FontFamily = "OpenSansSemibold",
                FontSize = 12,
                TextColor = Ui.ColorDe("TextoClaro"),
                LineBreakMode = LineBreakMode.TailTruncation
            }
        }
    };

    // ================================================================ reloj y refresco
    private void IniciarReloj()
    {
        _reloj?.Stop();
        _reloj = Dispatcher.CreateTimer();
        _reloj.Interval = TimeSpan.FromSeconds(1);
        _reloj.Tick += async (_, _) =>
        {
            if (_finalizado) return;
            PintarMonto();

            // Cada minuto se confirma con el servidor el descuento y el estado del ticket.
            if (DateTime.Now - _ultimoRefresco > TimeSpan.FromSeconds(60))
                await RefrescarAsync();
        };
        _reloj.Start();
    }

    /// <summary>Copia al pase local lo que devuelve el servidor (el objeto se reutiliza).</summary>
    private void CopiarPase(PasePuestoDto pase)
    {
        _pase.TicketId = pase.TicketId;
        _pase.TicketCodigo = pase.TicketCodigo;
        _pase.Placa = pase.Placa;
        _pase.SedeId = pase.SedeId;
        _pase.SedeCodigo = pase.SedeCodigo;
        _pase.SedeNombre = pase.SedeNombre;
        _pase.SedeDireccion = pase.SedeDireccion;
        _pase.SedeSector = pase.SedeSector;
        _pase.SedeLatitud = pase.SedeLatitud;
        _pase.SedeLongitud = pase.SedeLongitud;
        _pase.EspacioId = pase.EspacioId;
        _pase.EspacioCodigo = pase.EspacioCodigo;
        _pase.Zona = pase.Zona;
        _pase.Nivel = pase.Nivel;
        _pase.TipoVehiculo = pase.TipoVehiculo;
        _pase.TarifaHora = pase.TarifaHora;
        _pase.Entrada = pase.Entrada;
        _pase.Descuento = pase.Descuento;
        _pase.MontoAhora = pase.MontoAhora;
        _pase.MontoPorHora = pase.MontoPorHora;
        _pase.Estado = pase.Estado;
        _pase.Usuario = pase.Usuario;
        _pase.Modo = pase.Modo;
        _pase.SalidaPrevista = pase.SalidaPrevista;
        _pase.HorasPrevistas = pase.HorasPrevistas;
        _pase.MontoPrevisto = pase.MontoPrevisto;
        if (!string.IsNullOrWhiteSpace(pase.QrPayload)) _pase.QrPayload = pase.QrPayload;
    }

    private async Task RefrescarAsync(bool forzar = false)
    {
        if (_refrescando || _finalizado) return;
        if (!forzar && _pase.TicketId <= 0) return;

        _refrescando = true;
        try
        {
            var pase = await ApiCliente.PaseAsync(_pase.TicketId);
            if (pase is null) return;

            CopiarPase(pase);
            _montoPintado = -1;
            _ultimoRefresco = DateTime.Now;
            PintarTodo();

            if (!string.Equals(_pase.Estado, "ABIERTO", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(_pase.Estado, "RESERVADO", StringComparison.OrdinalIgnoreCase))
                await CerrarVistaAsync();
        }
        catch (Exception ex) when (ex is ApiExcepcion)
        {
            // Sin conexión se mantiene el cálculo local: el QR sigue sirviendo.
            lblNota.Text = "Sin conexión: el valor se calcula en el equipo con la misma fórmula del sistema.";
        }
        catch (Exception)
        {
            // cualquier otro fallo no debe tumbar el QR
        }
        finally
        {
            _refrescando = false;
        }
    }

    // ================================================================ acciones
    private async void OnLlegarClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_pase.SedeLatitud == 0 && _pase.SedeLongitud == 0)
            {
                await Ui.Informar(this, "Todavía no se cargó la ubicación del parqueadero.");
                return;
            }

            var lat = _pase.SedeLatitud.ToString("0.######", CultureInfo.InvariantCulture);
            var lng = _pase.SedeLongitud.ToString("0.######", CultureInfo.InvariantCulture);
            await Launcher.Default.OpenAsync(
                $"https://www.google.com/maps/dir/?api=1&destination={lat},{lng}&travelmode=walking");
        }
        catch (Exception ex)
        {
            await Ui.Error(this, "No se pudo abrir el mapa: " + ex.Message);
        }
    }

    private async void OnPagarClicked(object? sender, EventArgs e)
    {
        // En una reserva, este botón la activa: el QR pasa a contar el tiempo real.
        if (EsReserva)
        {
            await ActivarReservaAsync();
            return;
        }

        await Navigation.PushAsync(new vTickets());
    }

    private async Task ActivarReservaAsync()
    {
        if (_activando) return;
        _activando = true;
        btnPagar.IsEnabled = false;
        btnPagar.Text = "Activando…";
        try
        {
            var pase = await ApiCliente.ActivarPaseAsync(_pase.TicketId);
            CopiarPase(pase);
            _montoPintado = -1;
            PintarTodo();
            await Ui.Exito(this, "Reserva activada. Tu tiempo empieza a correr ahora.");
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            _activando = false;
            btnPagar.IsEnabled = true;
            btnPagar.Text = EsReserva ? "Ya llegué: activar" : "Cerrar y pagar";
        }
    }

    private async void OnAnularClicked(object? sender, EventArgs e)
    {
        if (_finalizado) return;

        if (!await Ui.Confirmar(this, "Anular reserva",
                "Se liberará el puesto y no se cobrará nada. ¿Continuar?"))
            return;

        btnAnular.IsEnabled = false;
        try
        {
            await ApiCliente.CancelarPaseAsync(_pase.TicketId);
            _pase.Estado = "ANULADO";
            await CerrarVistaAsync("Reserva anulada. El puesto quedó libre.");
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            btnAnular.IsEnabled = true;
        }
    }

    private async Task CerrarVistaAsync(string? mensaje = null)
    {
        if (_finalizado) return;
        _finalizado = true;
        _reloj?.Stop();

        if (!string.IsNullOrWhiteSpace(mensaje))
            await Ui.Exito(this, mensaje);

        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
    }

    // ================================================================ QR
    private static ImageSource ImagenQr(string texto)
    {
        using var generador = new QRCodeGenerator();
        using var datos = generador.CreateQrCode(texto, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(datos);
        var bytes = png.GetGraphic(16);
        return ImageSource.FromStream(() => new MemoryStream(bytes));
    }

    /// <summary>
    /// Contenido del QR si el pase vino sin payload (por ejemplo desde un ticket abierto y sin
    /// conexión). Tiene el mismo formato que arma el API.
    /// </summary>
    private string PayloadLocal() =>
        string.Join('|',
            "URBANPARK",
            "ESTACION",
            _pase.TicketCodigo,
            _pase.SedeCodigo,
            _pase.SedeNombre,
            _pase.SedeDireccion,
            _pase.EspacioCodigo,
            _pase.Zona,
            _pase.Placa,
            _pase.Usuario,
            _pase.Entrada.ToString("O", CultureInfo.InvariantCulture),
            _pase.TarifaHora.ToString("0.00", CultureInfo.InvariantCulture),
            _pase.Descuento.ToString(CultureInfo.InvariantCulture),
            Ui.MontoEstimado(_pase.Entrada, _pase.TarifaHora, _pase.Descuento)
                .ToString("0.00", CultureInfo.InvariantCulture),
            _pase.SedeLatitud.ToString("0.000000", CultureInfo.InvariantCulture),
            _pase.SedeLongitud.ToString("0.000000", CultureInfo.InvariantCulture),
            _pase.Modo,
            _pase.SalidaPrevista?.ToString("O", CultureInfo.InvariantCulture) ?? "");
}
