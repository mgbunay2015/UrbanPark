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
    private PasePuestoDto _pase = null!;
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

    /// <summary>Pestaña QR: busca el puesto vigente del cliente y lo muestra.</summary>
    public vQrPuesto()
    {
        InitializeComponent();
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

        try
        {
            if (_pase is null && !await CargarPuestoAsync())
            {
                MostrarSinPuesto();
                return;
            }

            if (_pase is null) return;

            IniciarReloj();
            await RefrescarAsync(forzar: true);
        }
        catch (Exception ex)
        {
            // La pantalla del QR nunca debe tumbar la app: se deja el estado vacío.
            try { MostrarSinPuesto(); } catch (Exception) { /* sin nada más que hacer */ }
            Registro.Error("vQrPuesto", "No se pudo preparar la pantalla del QR.", ex);
        }
    }

    /// <summary>Carga el puesto vigente (lo usa la pestaña QR).</summary>
    private async Task<bool> CargarPuestoAsync()
    {
        var pase = await PuestoActual.CargarAsync();

        // Sin internet (o si el API no responde) se usa el pase guardado en el equipo:
        // así el QR sigue visible aunque no haya conexión.
        pase ??= AlmacenLocal.PaseSinRed();

        if (pase is null) return false;

        _pase = pase;
        _montoPintado = -1;
        PintarTodo();
        return true;
    }

    /// <summary>Estado vacío: el cliente no tiene puesto ocupado ni reserva.</summary>
    private void MostrarSinPuesto()
    {
        _reloj?.Stop();
        _finalizado = false;

        lblTituloQr.Text = "Sin puesto activo";
        lblSubtitulo.Text = "Todavía no tienes un puesto ocupado ni una reserva";
        lblTicketNumero.Text = "N: —";
        lblTicketFecha.Text = DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-EC"));
        lblTicketPlaca.Text = "—";
        lblTicketEntrada.Text = "E: —";
        lblTicketHora.Text = "--:--";
        lblTicketSede.Text = "URBAN PARK";
        imgQr.Source = null;
        panelPerfil.IsVisible = false;
        lblVehiculoDetalle.Text = "";
        lblDireccion.Text = "Cuando entres a un puesto o reserves uno, aquí verás tu QR.";
        lblEtiquetaMonto.Text = "EL QR APARECE CON TU PUESTO";
        lblMonto.Text = "$0.00";
        lblTiempo.Text = "";
        lblCalculo.Text = "";
        contenidoDatos.Clear();
        btnLlegar.IsVisible = false;
        btnPagar.IsVisible = false;
        btnAnular.IsVisible = false;
        lblNota.Text = "Elige un parqueadero en la pestaña Mapa y tu QR aparecerá aquí.";
    }
    protected override void OnDisappearing()
    {
        _reloj?.Stop();
        base.OnDisappearing();
    }

    // ================================================================ pintado
    private void PintarTodo()
    {
        if (_pase is null) return;

        lblTituloQr.Text = EsReserva ? "Tu puesto está reservado" : "Tu QR";

        // Cabecera del ticket impreso: número, fecha, placa, entrada y hora.
        var cultura = CultureInfo.GetCultureInfo("es-EC");
        lblTicketNumero.Text = $"N: {CodigoTicket()}";
        lblTicketFecha.Text = _pase.Entrada.ToString("dd/MM/yyyy", cultura);
        lblTicketPlaca.Text = PlacaEcuador.Formatear(_pase.Placa).Replace("-", "");
        lblTicketEntrada.Text = $"E: {NumeroEntrada()}";
        lblTicketHora.Text = _pase.Entrada.ToString("HH:mm", cultura);
        lblTicketSede.Text = string.IsNullOrWhiteSpace(_pase.SedeNombre)
            ? "URBAN PARK"
            : _pase.SedeNombre!.ToUpperInvariant();

        lblDireccion.Text = string.IsNullOrWhiteSpace(_pase.SedeDireccion)
            ? "Consultando la dirección del parqueadero…"
            : "📍 " + _pase.SedeDireccion;

        PintarDibujoVehiculo();

        // Aviso de modo local: el QR y el valor salen del equipo.
        if (ApiCliente.SinConexion || !HayRedLocal())
        {
            lblSubtitulo.Text = "Sin conexión · el QR y el valor se calculan en el equipo";
            btnAnular.IsEnabled = false;
            btnPagar.IsEnabled = false;
        }
        else
        {
            btnAnular.IsEnabled = true;
            btnPagar.IsEnabled = true;
        }

        if (EsReserva)
        {
            lblSubtitulo.Text = "Reserva confirmada · muestra este QR al llegar";
            lblEtiquetaMonto.Text = "VALOR ESTIMADO DE LA RESERVA";
            btnPagar.Text = "Ya llegué: activar";
            btnAnular.IsVisible = true;
        }
        else
        {
            lblSubtitulo.Text = "Muestra este QR en la entrada del parqueadero";
            lblEtiquetaMonto.Text = "VALOR A PAGAR EN ESTE MOMENTO";
            btnPagar.Text = "Cerrar y pagar";
            // anular solo tiene sentido con una reserva: una estancia se cierra y se paga
            btnAnular.IsVisible = false;
        }

        PintarMonto();
        PintarDatos();
    }

    private bool EsReserva =>
        _pase is not null &&
        (string.Equals(_pase.Estado, "RESERVADO", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(_pase.Modo, "RESERVA", StringComparison.OrdinalIgnoreCase));

    /// <summary>Red disponible: con esto se decide si se muestran los botones de acción.</summary>
    private static bool HayRedLocal() =>
        Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    /// <summary>
    /// Pinta el perfil de quien conduce en la cabecera del ticket (icono, nombre y descuento)
    /// y la marca/color debajo. Es el distintivo de la persona: embarazada, tercera edad,
    /// capacidades especiales, estudiante…
    /// </summary>
    private void PintarDibujoVehiculo()
    {
        if (_pase is null) return;

        var perfil = _pase.PerfilConductor ?? "";

        var (icono, nombre) = IconoPerfil(perfil);
        panelPerfil.IsVisible = !string.IsNullOrWhiteSpace(nombre);
        lblPerfilIcono.Text = icono;
        lblPerfilNombre.Text = nombre;
        lblPerfilDescuento.IsVisible = _pase.Descuento > 0;
        lblPerfilDescuento.Text = _pase.Descuento > 0 ? $"{_pase.Descuento}% descuento" : "";

        // Marca y color bajo el perfil, si el API los trae.
        var detalle = string.Join(" · ",
            new[] { _pase.Marca?.Trim(), _pase.Color?.Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)));
        lblVehiculoDetalle.Text = detalle;
        lblVehiculoDetalle.IsVisible = detalle.Length > 0;
    }

    /// <summary>Icono y nombre legible del perfil de quien conduce.</summary>
    private static (string Icono, string Nombre) IconoPerfil(string perfil)
    {
        var p = (perfil ?? "").ToLowerInvariant();
        if (p.Length == 0) return ("👤", "");
        if (p.Contains("capacidades") || p.Contains("discapacit") || p.Contains("especial"))
            return ("♿", "Capacidades especiales");
        if (p.Contains("tercera") || p.Contains("anciano") || p.Contains("mayor"))
            return ("👴", "Tercera edad");
        if (p.Contains("embaraz"))
            return ("🤰", "Embarazada");
        if (p.Contains("estudiante") || p.Contains("estudiate"))
            return ("🎓", "Estudiante");
        if (p.Contains("mujer"))
            return ("👩", "Mujer");
        if (p.Contains("hombre"))
            return ("👨", "Hombre");
        return ("👤", perfil!);
    }

    /// <summary>Número del ticket como en el papel: 0433656.</summary>
    private string CodigoTicket()
    {
        var digitos = new string((_pase.TicketCodigo ?? "").Where(char.IsDigit).ToArray());
        if (digitos.Length == 0) return _pase.TicketId.ToString("D7");
        return digitos.Length > 7 ? digitos[^7..] : digitos.PadLeft(7, '0');
    }

    /// <summary>Número de entrada (011 en el ticket físico): sale de la plaza asignada.</summary>
    private string NumeroEntrada()
    {
        var digitos = new string((_pase.EspacioCodigo ?? "").Where(char.IsDigit).ToArray());
        return digitos.Length == 0 ? "000" : digitos.PadLeft(3, '0');
    }

    private void PintarMonto()
    {
        if (_pase is null) return;

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
        if (_pase is null) return;
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
            try
            {
                if (_finalizado || _pase is null) return;
                PintarMonto();

                // Cada minuto se confirma con el servidor el descuento y el estado del ticket.
                if (DateTime.Now - _ultimoRefresco > TimeSpan.FromSeconds(60))
                    await RefrescarAsync();
            }
            catch (Exception ex)
            {
                // un fallo al repintar no debe detener la cuenta ni cerrar la pantalla
                Registro.Aviso("vQrPuesto", $"Falló la actualización del reloj del QR: {ex.Message}");
            }
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
        _pase.PerfilConductor = pase.PerfilConductor;
        _pase.Marca = pase.Marca;
        _pase.Color = pase.Color;
        if (!string.IsNullOrWhiteSpace(pase.QrPayload)) _pase.QrPayload = pase.QrPayload;
    }

    private async Task RefrescarAsync(bool forzar = false)
    {
        if (_refrescando || _finalizado || _pase is null) return;
        if (!forzar && _pase.TicketId <= 0) return;

        _refrescando = true;
        try
        {
            var id = _pase.TicketId;
            var pase = await ApiCliente.PaseAsync(id);

            // La página pudo cerrarse mientras llegaba la respuesta.
            if (pase is null || _pase is null || _finalizado) return;

            // Cada lectura correcta deja el pase en SQLite para el modo sin conexión.
            AlmacenLocal.GuardarPase(pase, Sesion.Username);

            CopiarPase(pase);
            _montoPintado = -1;
            _ultimoRefresco = DateTime.Now;
            PintarTodo();

            // Estado nulo (respuesta incompleta) se trata como abierto: no se cierra la vista.
            var estado = _pase.Estado ?? "";
            if (!string.Equals(estado, "ABIERTO", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(estado, "RESERVADO", StringComparison.OrdinalIgnoreCase))
            {
                // El ticket se cerró (normalmente porque el cliente acaba de pagar).
                // NO se navega aquí: el pago mueve las pestañas del Shell al mismo tiempo y
                // un PopAsync en ese instante lanza NullReferenceException dentro de MAUI.
                // Basta con dejar de refrescar y mostrar el estado vacío si la página sigue.
                _finalizado = true;
                _reloj?.Stop();
                MostrarSinPuesto();
            }
        }
        catch (Exception ex) when (ex is ApiExcepcion)
        {
            // Sin conexión se mantiene el cálculo local: el QR sigue sirviendo.
            lblNota.Text = "Sin conexión: el QR y el valor se calculan en el equipo con la misma fórmula del sistema.";
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
        if (_pase is null) return;

        try
        {
            vLlegada.SedeDestacarAlMostrar = _pase.SedeId > 0 ? _pase.SedeId : null;
            if (vPrincipal.IrAPestana("Llegar"))
                return;

            await Navigation.PushAsync(new vLlegada(vLlegada.SedeDestacarAlMostrar));
        }
        catch (Exception ex)
        {
            await Ui.Error(this, "No se pudo abrir el mapa: " + ex.Message);
        }
    }

    private async void OnPagarClicked(object? sender, EventArgs e)
    {
        if (_pase is null || _finalizado) return;

        // En una reserva, este botón la activa: el QR pasa a contar el tiempo real.
        if (EsReserva)
        {
            await ActivarReservaAsync();
            return;
        }

        // El cobro se hace aquí: abrir «Tickets» con PushAsync en la pestaña QR no sirve porque
        // el Shell vuelve al inicio de esa pestaña en cada navegación y la pantalla se cierra al instante.
        await CobrarDesdeQrAsync();
    }

    private async Task CobrarDesdeQrAsync()
    {
        if (_pase is null) return;

        var (puede, motivo) = await ApiCliente.PuedeCobrarAsync();
        if (!puede)
        {
            await Ui.Error(this, motivo, "No se puede cobrar");
            return;
        }

        var estimado = Ui.MontoEstimado(_pase.Entrada, _pase.TarifaHora, _pase.Descuento);
        var placa = PlacaEcuador.Formatear(_pase.Placa);
        if (!await Ui.Confirmar(this, "Cerrar y cobrar",
                $"¿Cerrar el ticket {_pase.TicketCodigo} de {placa}? Valor estimado a cobrar: {Ui.Moneda(estimado)}"))
            return;

        btnPagar.IsEnabled = false;
        btnPagar.Text = "Cobrando…";
        try
        {
            vPrincipal.SuspenderReajuste();
            Registro.Info("Cobro",
                $"Cerrando desde QR ticket {_pase.TicketCodigo} de {_pase.Placa} (estimado {Ui.Moneda(estimado)}).");

            var cerrado = await ApiCliente.CerrarTicketAsync(_pase.TicketId);
            var monto = cerrado?.MontoTotal ?? estimado;
            var codigo = cerrado?.Codigo ?? _pase.TicketCodigo;
            var placaCerrada = cerrado?.Placa ?? _pase.Placa;
            var plaza = cerrado?.EspacioCodigo ?? _pase.EspacioCodigo;
            var usuario = cerrado?.Usuario ?? _pase.Usuario ?? Sesion.Username;

            PaseSalida.Activar(codigo, placaCerrada, plaza, monto, usuario, _pase);
            BaseLocal.QuitarPase(_pase.TicketId);
            vPrincipal.RevisarPestanas();

            _finalizado = true;
            _reloj?.Stop();

            if (!vPrincipal.IrAPestana("Salida"))
            {
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), () =>
                {
                    if (!vPrincipal.IrAPestana("Salida"))
                        _ = Navigation.PushAsync(new vSalida());
                });
            }

            await Ui.Exito(this,
                $"Pagado {Ui.Moneda(monto)}. Tienes {PaseSalida.Minutos} minutos para mostrar el QR de salida.");
            MostrarSinPuesto();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            if (!_finalizado)
            {
                btnPagar.IsEnabled = !ApiCliente.SinConexion && HayRedLocal();
                btnPagar.Text = "Cerrar y pagar";
            }
        }
    }

    private async Task ActivarReservaAsync()
    {
        if (_activando || _pase is null) return;
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
        if (_finalizado || _pase is null) return;

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

        // Esta página vive siempre dentro de una pestaña del Shell (se abre con PushAsync sobre
        // la pestaña Mapa o es la propia pestaña QR). Al cerrar el ticket, el Shell reemplaza esa
        // pestaña y destruye su pila de navegación; un PopAsync en ese momento lanza
        // NullReferenceException DENTRO de MAUI porque el NavigationPage ya no tiene handler.
        // Por eso no se navega aquí: basta con dejar el estado vacío y dejar que el Shell
        // desmonte la página al cambiar de pestaña.
        try
        {
            MostrarSinPuesto();
        }
        catch (Exception ex)
        {
            Registro.Aviso("vQrPuesto", $"No se pudo mostrar el estado sin puesto: {ex.Message}");
        }

        await Task.CompletedTask;
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
