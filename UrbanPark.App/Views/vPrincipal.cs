using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Barra inferior: Mapa de parqueaderos, Autos, Cómo Llegar (o Reportería si es ADMIN), Perfil y Salir.
/// La primera pestaña es el mapa con las sedes: el cliente elige el parqueadero antes que nada.
/// Mientras hay un pase de salida vigente aparece la pestaña "Salida" con la cuenta atrás.
/// </summary>
public class vPrincipal : Shell
{
    private readonly TabBar _barra = new();
    private ShellContent? _pestanaSalida;
    private ShellContent? _pestanaSalir;
    private IDispatcherTimer? _reloj;

    public vPrincipal()
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;

        Shell.SetBackgroundColor(this, Color.FromArgb("#0A0E13"));
        Shell.SetTabBarBackgroundColor(this, Color.FromArgb("#141B24"));
        Shell.SetTabBarForegroundColor(this, Color.FromArgb("#F2C51A"));
        Shell.SetTabBarTitleColor(this, Color.FromArgb("#F2C51A"));
        Shell.SetTabBarUnselectedColor(this, Color.FromArgb("#7C8A9C"));

        // Lo primero es elegir el parqueadero: el mapa con las 12 sedes abre la aplicación.
        _barra.Items.Add(Pestana("Mapa", "tab_mapa.png", typeof(vSedes)));
        _barra.Items.Add(Pestana("Autos", "tab_auto.png", typeof(vVehiculos)));
        if (Sesion.EsAdmin)
            _barra.Items.Add(Pestana("Reportería", "tab_reporte.png", typeof(vReporte)));
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
    /// </summary>
    private async void OnNavegado(object? sender, ShellNavigatedEventArgs e)
    {
        var nav = CurrentPage?.Navigation;
        if (nav is null) return;
        if (CurrentPage is vVehiculos) return;
        if (nav.NavigationStack.OfType<vVehiculos>().Any())
            await nav.PopToRootAsync();
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
