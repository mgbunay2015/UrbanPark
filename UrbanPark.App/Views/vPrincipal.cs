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

    /// <summary>
    /// Mientras el usuario está pagando se suspende el reajuste de pestañas: cerrar el
    /// ticket deja al cliente sin puesto y, si en ese instante se quitara la pestaña que
    /// está viendo, el Shell se quedaría sin página y la app se cerraría.
    /// </summary>
    private static bool _suspendido;
    private static DateTime _suspensionHasta = DateTime.MinValue;

    /// <summary>Suspende el reajuste de pestañas unos segundos (se usa al cobrar).</summary>
    public static void SuspenderReajuste(int segundos = 8)
    {
        _suspendido = true;
        _suspensionHasta = DateTime.Now.AddSeconds(segundos);
    }

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
            // El admin cobra en caja: necesita ver de un toque TODOS los tickets abiertos.
            // «Cambiar precios» vive dentro de Reportería (botón Tarifas): el TabBar con 7
            // pestañas desbordaba y WinUI entraba en LayoutCycleException.
            _barra.Items.Add(Pestana("Tickets", "tab_plazas.png", typeof(vTickets)));
            _barra.Items.Add(Pestana("Reportería", "tab_reporte.png", typeof(vReporte)));
        }
        else if (Sesion.EsOperador)
        {
            // El operador también cobra los tickets de cualquier cliente.
            _barra.Items.Add(Pestana("Tickets", "tab_plazas.png", typeof(vTickets)));
            _barra.Items.Add(Pestana("Llegar", "tab_llegar.png", typeof(vLlegada)));
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
            _ = PaseSalida.SincronizarQrsPagadosAsync();
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
        await AsegurarRaizPestanaAsync("QR");
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

    /// <summary>
    /// Lleva al usuario a una pestaña de la barra por su título (por ejemplo «Salida»).
    /// Devuelve false si esa pestaña no existe, para que quien llama pueda abrir la
    /// pantalla como página normal.
    /// </summary>
    public static bool IrAPestana(string titulo)
    {
        if (Application.Current?.Windows.Count == 0) return false;
        if (Application.Current!.Windows[0].Page is not vPrincipal principal) return false;
        return principal.SeleccionarPestana(titulo);
    }

    private bool SeleccionarPestana(string titulo)
    {
        try
        {
            foreach (var item in _barra.Items)
            {
                if (!string.Equals(item.Title, titulo, StringComparison.OrdinalIgnoreCase)) continue;
                if (item.Parent is not ShellSection seccion) continue;

                CurrentItem = seccion;
                return true;
            }
        }
        catch (Exception ex)
        {
            Registro.Aviso("Navegacion", $"No se pudo seleccionar la pestaña: {ex.Message}");
        }

        return false;
    }

    private ShellContent? PestanaActiva() =>
        CurrentItem?.CurrentItem?.CurrentItem as ShellContent;

    private void RestaurarPestana(ShellContent? pestana)
    {
        if (pestana is null || !_barra.Items.Contains(pestana)) return;
        if (pestana.Parent is not ShellSection seccion) return;
        try { CurrentItem = seccion; }
        catch (Exception ex)
        {
            Registro.Aviso("Navegacion", $"No se pudo restaurar la pestaña activa: {ex.Message}");
        }
    }

    /// <summary>La pestaña QR debe mostrar siempre <see cref="vQrPuesto"/>, no mapas apilados.</summary>
    private async Task AsegurarRaizPestanaAsync(string titulo)
    {
        if (!string.Equals(PestanaActiva()?.Title, titulo, StringComparison.OrdinalIgnoreCase)) return;
        var nav = CurrentPage?.Navigation;
        if (nav is null || nav.NavigationStack.Count <= 1) return;

        try { await nav.PopToRootAsync(animated: false); }
        catch (Exception ex)
        {
            Registro.Aviso("Navegacion", $"No se pudo volver al inicio de la pestaña {titulo}: {ex.Message}");
        }
    }

    /// <summary>
    /// Pestaña del parqueadero: el suyo si tiene puesto o reserva, si no el mapa de los 12.</summary>
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

        // Durante el cobro no se tocan las pestañas: quitar la que el usuario está viendo
        // dejaría al Shell sin página y cerraría la aplicación.
        if (_suspendido)
        {
            if (DateTime.Now < _suspensionHasta) return;
            _suspendido = false;
        }

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
        var pestanaVista = PestanaActiva();
        var anterior = _pestanaMapa;
        var nueva = PestanaMapa(sede);

        var indice = anterior is null ? -1 : _barra.Items.IndexOf(anterior);
        if (indice < 0)
        {
            _barra.Items.Insert(0, nueva);
            _pestanaMapa = nueva;
            if (pestanaVista is not null && !ReferenceEquals(pestanaVista, anterior))
                RestaurarPestana(pestanaVista);
            return;
        }

        // Se inserta la nueva y se cambia a ella ANTES de quitar la vieja. Si la pestaña que
        // se elimina aún tiene una página apilada (por ejemplo el QR del puesto), el Shell
        // destruye esa pila y cualquier navegación pendiente sobre ella revienta dentro de MAUI.
        var viendoMapa = CurrentItem?.CurrentItem?.CurrentItem == anterior;

        _barra.Items.Insert(indice + 1, nueva);

        if (viendoMapa && nueva.Parent is ShellSection seccion)
        {
            try { CurrentItem = seccion; }
            catch (Exception ex) { Registro.Aviso("Navegacion", $"No se pudo cambiar a la pestaña del parqueadero: {ex.Message}"); }
        }

        // La pestaña vieja se retira en el siguiente ciclo, ya con la nueva activa.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(80), () =>
        {
            try
            {
                if (_barra.Items.Contains(anterior) && _barra.Items.Count > 1)
                    _barra.Items.Remove(anterior);
            }
            catch (Exception ex)
            {
                Registro.Aviso("Navegacion", $"No se pudo quitar la pestaña anterior: {ex.Message}");
            }
        });

        _pestanaMapa = nueva;
        // No restaurar la pestaña Mapa vieja: ya se sustituyó y se elimina en unos ms.
        if (pestanaVista is not null && !ReferenceEquals(pestanaVista, anterior))
            RestaurarPestana(pestanaVista);
    }

    private void MostrarPestanaQr(bool visible)
    {
        try
        {
            MostrarPestanaQrInterno(visible);
        }
        catch (Exception ex)
        {
            // Manipular las pestañas de un Shell mientras se navega puede fallar:
            // nunca debe terminar el proceso.
            Registro.Aviso("Navegacion", $"No se pudo mostrar la pestaña QR: {ex.Message}");
        }
    }

    private void MostrarPestanaQrInterno(bool visible)
    {
        if (visible)
        {
            if (_pestanaQr is not null && _barra.Items.Contains(_pestanaQr))
            {
                // el pase pudo cambiar (otro puesto): se rehace para que cargue el nuevo
                var indice = _barra.Items.IndexOf(_pestanaQr);
                var estabaEnQr = PestanaActiva() == _pestanaQr;
                _barra.Items.RemoveAt(indice);
                _pestanaQr = PestanaQr();
                _barra.Items.Insert(Math.Min(indice, _barra.Items.Count), _pestanaQr);
                if (estabaEnQr)
                    RestaurarPestana(_pestanaQr);
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
        if (estaEnQr)
        {
            // Nunca se quita la pestaña que el usuario está viendo: primero se navega a otra
            // y solo después se elimina. Si no hay otra, se conserva hasta poder cambiar.
            if (_barra.Items.Count <= 1) return;
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(60), () =>
            {
                try
                {
                    if (CurrentItem?.CurrentItem?.CurrentItem == quitar && _barra.Items.Count > 1)
                    {
                        var destino = _barra.Items.FirstOrDefault(i => !ReferenceEquals(i, quitar));
                        if (destino?.Parent is ShellSection seccion)
                            CurrentItem.CurrentItem = seccion;
                    }
                    if (_barra.Items.Contains(quitar)) _barra.Items.Remove(quitar);
                }
                catch (Exception ex)
                {
                    Registro.Aviso("Navegacion", $"No se pudo mostrar la pestaña QR: {ex.Message}");
                }
            });
            return;
        }

        Dispatcher.DispatchDelayed(TimeSpan.Zero, () =>
        {
            try { if (_barra.Items.Contains(quitar)) _barra.Items.Remove(quitar); }
            catch (Exception ex) { Registro.Aviso("Navegacion", $"No se pudo mostrar la pestaña QR: {ex.Message}"); }
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
        try
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
        catch (Exception ex)
        {
            Registro.Aviso("Navegacion", $"No se pudo actualizar la pestaña Salida: {ex.Message}");
        }
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
            {
                // Solo se asigna si de verdad cambió: escribir el mismo valor cada segundo
                // provoca que el TabBar se vuelva a medir sin necesidad.
                var titulo = TituloSalida(pases);
                if (!string.Equals(_pestanaSalida.Title, titulo, StringComparison.Ordinal))
                    _pestanaSalida.Title = titulo;
            }
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
            try
            {
                if (_barra.Items.Count == 0) return;
                if (CurrentItem?.CurrentItem?.CurrentItem == pestana)
                    CurrentItem.CurrentItem = _barra.Items[0];
                _barra.Items.Remove(pestana);
            }
            catch (Exception ex)
            {
                // Quitar la pestaña visible puede dejar el Shell sin página.
                Registro.Aviso("Navegacion", $"No se pudo quitar la pestaña Salida: {ex.Message}");
            }
        });
    }

    private static string TituloSalida(IReadOnlyList<PaseSalida.Pase> pases)
    {
        // El título NO lleva la cuenta atrás. Con la cuenta, el texto cambiaba cada segundo y
        // el TabBar se volvía a medir continuamente; con muchas pestañas (el admin tiene 7)
        // WinUI entraba en LayoutCycleException y la app se quedaba colgada. La cuenta atrás
        // se ve dentro de la pantalla de salida, que es donde hace falta.
        return pases.Count <= 1 ? "Salida" : $"Salida {pases.Count}";
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
