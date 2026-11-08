using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Barra inferior: Mapa, Autos, Reportería y Cambiar precios (ADMIN), Perfil y Salir.
/// La primera pestaña es el mapa con las sedes mientras el cliente no tenga nada; en cuanto
/// tiene un puesto ocupado o una reserva, esa misma pestaña pasa a mostrar SU parqueadero
/// (la pantalla de plazas con su puesto marcado) y aparece la pestaña QR con su código.
/// Mientras hay un pase de salida vigente aparece además la pestaña "Salida" con la cuenta atrás.
/// </summary>
public class vPrincipal : Shell
{
    private readonly TabBar _barra = new();
    private ShellContent? _pestanaMapa;
    private ShellContent? _pestanaQr;
    private ShellContent? _pestanaSalida;
    private ShellContent? _pestanaSalir;
    private IDispatcherTimer? _reloj;

    /// <summary>Ticket vigente con el que se pintaron las pestañas (null = sin puesto).</summary>
    private long? _ticketPintado;
    private bool _ajustando;
    private DateTime _ultimaRevision = DateTime.MinValue;

    public vPrincipal()
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;

        Shell.SetBackgroundColor(this, Color.FromArgb("#0A0E13"));
        Shell.SetTabBarBackgroundColor(this, Color.FromArgb("#141B24"));
        Shell.SetTabBarForegroundColor(this, Color.FromArgb("#F2C51A"));
        Shell.SetTabBarTitleColor(this, Color.FromArgb("#F2C51A"));
        Shell.SetTabBarUnselectedColor(this, Color.FromArgb("#7C8A9C"));

        // Lo primero es el parqueadero: el mapa con las 12 sedes o, si ya tiene puesto, el suyo.
        _pestanaMapa = PestanaMapa(null);
        _barra.Items.Add(_pestanaMapa);
        _barra.Items.Add(Pestana("Autos", "tab_auto.png", typeof(vVehiculos)));
        if (Sesion.EsAdmin)
        {
            _barra.Items.Add(Pestana("Reportería", "tab_reporte.png", typeof(vReporte)));
            _barra.Items.Add(Pestana("Cambiar precios", "tab_precios.png", typeof(vTarifas)));
        }
        else
            _barra.Items.Add(Pestana("Llegar", "tab_llegar.png", typeof(vLlegada)));
        _barra.Items.Add(Pestana("Perfil", "tab_perfil.png", typeof(vPerfil)));
        _pestanaSalir = Pestana("Salir", "tab_salir.png", typeof(vSalir));
        _barra.Items.Add(_pestanaSalir);
        Items.Add(_barra);

        Navigated += OnNavegado;

        Loaded += (_, _) =>
        {
            PaseSalida.Cambio -= OnPaseCambio;
            PaseSalida.Cambio += OnPaseCambio;
            ActualizarPestanaSalida();
            _ = AjustarPestanasAsync(forzar: true);
            // al arrancar la sesión puede tardar un instante en estar disponible
            Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(2), () => _ = AjustarPestanasAsync());
        };
        Unloaded += (_, _) =>
        {
            PaseSalida.Cambio -= OnPaseCambio;
            _reloj?.Stop();
        };
    }

    /// <summary>
    /// Al pulsar Mis Vehículos se vuelve al formulario de registro,
    /// aunque el listado de registrados haya quedado encima.
    /// Además, cada navegación revisa si el cliente entró o cerró su puesto para
    /// volver a armar la pestaña Mapa y la del QR.
    /// </summary>
    private async void OnNavegado(object? sender, ShellNavigatedEventArgs e)
    {
        var nav = CurrentPage?.Navigation;
        if (nav is not null && CurrentPage is not vVehiculos)
        {
            if (nav.NavigationStack.OfType<vVehiculos>().Any())
                await nav.PopToRootAsync();
        }

        await AjustarPestanasAsync();
    }

    // ================================================================ pestañas según el puesto
    /// <summary>
    /// Las pantallas que ya saben que el cliente entró o cerró un puesto llaman aquí para que la
    /// barra se rehaga sin esperar a la siguiente navegación.
    /// </summary>
    public static void RevisarPestanas()
    {
        if (Application.Current?.Windows.Count > 0 &&
            Application.Current.Windows[0].Page is vPrincipal principal)
            _ = principal.AjustarPestanasAsync();
    }

    /// <summary>Pestaña del parqueadero: el suyo si tiene puesto o reserva, si no el mapa de los 12.</summary>
    private static ShellContent PestanaMapa(SedeDto? sede)
    {
        var contenido = new ShellContent
        {
            Title = "Mapa",
            Icon = "tab_mapa.png",
            Content = sede is null ? new vSedes() : (Page)new vPlazas(sede)
        };
        Shell.SetNavBarIsVisible(contenido, false);
        return contenido;
    }

    /// <summary>
    /// Rehace la primera pestaña (mapa o "mi parqueadero") y muestra u oculta la del QR
    /// cuando el puesto del cliente cambia. Si no cambió nada, no toca las pestañas.
    /// </summary>
    private async Task AjustarPestanasAsync(bool forzar = false)
    {
        if (_ajustando || _barra.Items.Count == 0) return;

        // Cada navegación dispara varios eventos: se revisa como mucho una vez por segundo.
        if (!forzar && DateTime.Now - _ultimaRevision < TimeSpan.FromSeconds(1)) return;
        _ultimaRevision = DateTime.Now;

        _ajustando = true;
        try
        {
            var pase = await PuestoActual.CargarAsync();
            var ticket = pase?.TicketId;
            if (!forzar && Nullable.Equals(ticket, _ticketPintado)) return;
            _ticketPintado = ticket;

            var sede = pase is null ? null : await PuestoActual.SedeAsync(pase);
            CambiarPestanaMapa(sede);
            MostrarPestanaQr(ticket is not null);
        }
        catch (Exception)
        {
            // sin conexión se queda como está
        }
        finally
        {
            _ajustando = false;
        }
    }

    private void CambiarPestanaMapa(SedeDto? sede)
    {
        var anterior = _pestanaMapa;
        var nueva = PestanaMapa(sede);

        var indice = anterior is null ? -1 : _barra.Items.IndexOf(anterior);
        if (indice < 0)
        {
            _barra.Items.Insert(0, nueva);
            _pestanaMapa = nueva;
            return;
        }

        var viendoMapa = CurrentItem?.CurrentItem?.CurrentItem == anterior;

        // Se inserta la nueva al lado, se pasa a ella si el cliente la está mirando y solo
        // entonces se quita la vieja: quitar la pestaña visible deja al Shell sin página.
        _barra.Items.Insert(indice + 1, nueva);
        if (viendoMapa && nueva.Parent is ShellSection seccion)
        {
            try { CurrentItem!.CurrentItem = seccion; }
            catch (Exception) { /* Shell reacomoda solo */ }
        }
        _barra.Items.Remove(anterior);
        _pestanaMapa = nueva;
    }

    private void MostrarPestanaQr(bool visible)
    {
        if (visible)
        {
            if (_pestanaQr is not null && _barra.Items.Contains(_pestanaQr))
            {
                // el pase pudo cambiar (otro puesto): se rehace para que cargue el nuevo
                var indice = _barra.Items.IndexOf(_pestanaQr);
                _barra.Items.RemoveAt(indice);
                _pestanaQr = PestanaQr();
                _barra.Items.Insert(Math.Min(indice, _barra.Items.Count), _pestanaQr);
                return;
            }

            _pestanaQr = PestanaQr();
            var indiceSalir = _pestanaSalir is null ? -1 : _barra.Items.IndexOf(_pestanaSalir);
            if (indiceSalir >= 0) _barra.Items.Insert(indiceSalir, _pestanaQr);
            else _barra.Items.Add(_pestanaQr);
            return;
        }

        if (_pestanaQr is null) return;
        var quitar = _pestanaQr;
        _pestanaQr = null;

        var estaEnQr = CurrentItem?.CurrentItem?.CurrentItem == quitar;
        Dispatcher.DispatchDelayed(estaEnQr ? TimeSpan.FromMilliseconds(60) : TimeSpan.Zero, () =>
        {
            if (_barra.Items.Contains(quitar)) _barra.Items.Remove(quitar);
            if (estaEnQr && _barra.Items.Count > 0) CurrentItem!.CurrentItem = _barra.Items[0].Parent as ShellSection ?? CurrentItem!.CurrentItem;
        });
    }

    private static ShellContent PestanaQr()
    {
        var contenido = new ShellContent
        {
            Title = "QR",
            Icon = "tab_qr.png",
            Content = new vQrPuesto()
        };
        Shell.SetNavBarIsVisible(contenido, false);
        return contenido;
    }

    private void OnPaseCambio(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(ActualizarPestanaSalida);

    private void ActualizarPestanaSalida()
    {
        var pases = PaseSalida.Activos;
        if (pases.Count == 0)
        {
            QuitarPestanaSalida();
            return;
        }

        if (_pestanaSalida is null)
        {
            _pestanaSalida = Pestana(TituloSalida(pases), "tab_salida.png", typeof(vSalida));
            var indiceSalir = _pestanaSalir is null ? -1 : _barra.Items.IndexOf(_pestanaSalir);
            if (indiceSalir >= 0)
                _barra.Items.Insert(indiceSalir, _pestanaSalida);
            else
                _barra.Items.Add(_pestanaSalida);
        }

        _reloj ??= CrearReloj();
        _reloj.Start();
    }

    private IDispatcherTimer CrearReloj()
    {
        var reloj = Dispatcher.CreateTimer();
        reloj.Interval = TimeSpan.FromSeconds(1);
        reloj.Tick += (_, _) =>
        {
            var pases = PaseSalida.Activos;
            if (pases.Count == 0)
            {
                QuitarPestanaSalida();
                return;
            }

            if (_pestanaSalida is not null)
                _pestanaSalida.Title = TituloSalida(pases);
        };
        return reloj;
    }

    private void QuitarPestanaSalida()
    {
        _reloj?.Stop();
        if (_pestanaSalida is null) return;

        var pestana = _pestanaSalida;
        _pestanaSalida = null;

        // si el usuario está mirando el QR vencido, se le deja ver el aviso un momento
        var estaEnSalida = CurrentItem?.CurrentItem?.CurrentItem == pestana;
        Dispatcher.DispatchDelayed(estaEnSalida ? TimeSpan.FromSeconds(4) : TimeSpan.Zero, () =>
        {
            if (_barra.Items.Count == 0) return;
            if (CurrentItem?.CurrentItem?.CurrentItem == pestana)
                CurrentItem.CurrentItem = _barra.Items[0];
            _barra.Items.Remove(pestana);
        });
    }

    private static string TituloSalida(IReadOnlyList<PaseSalida.Pase> pases)
    {
        var proximo = pases.MinBy(p => p.ExpiraEn);
        if (proximo is null) return "Salida";
        var tiempo = PaseSalida.TextoRestante(proximo);
        return pases.Count == 1 ? $"Salida {tiempo}" : $"Salida {pases.Count} · {tiempo}";
    }

    private static ShellContent Pestana(string titulo, string icono, Type pagina)
    {
        var item = new ShellContent
        {
            Title = titulo,
            Icon = icono,
            ContentTemplate = new DataTemplate(pagina)
        };
        Shell.SetNavBarIsVisible(item, false);
        return item;
    }
}
