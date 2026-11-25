using UrbanPark.App.Views;

namespace UrbanPark.App.Servicios;

/// <summary>
/// Pone el indicador de conexión (punto verde/rojo) en todas las pantallas sin editar cada vista.
///
/// La app navega de dos formas: con <see cref="NavigationPage"/> (login y cambios de sesión) y
/// con <see cref="Shell"/> (las pestañas con sus páginas apiladas). Se cubren las dos:
///   · NavigationPage: eventos Pushed / Popped / PoppedToRoot.
///   · Shell: evento Navigated, que se dispara al cambiar de pestaña y al apilar una página.
/// Además, cada página repinta su indicador al aparecer, por si se creó mientras el estado
/// de conexión aún no se había comprobado.
/// </summary>
public static class IndicadorConexion
{
    private static bool _instalado;

    /// <summary>Engancha el indicador a la navegación.</summary>
    public static void Instalar()
    {
        if (_instalado) return;
        _instalado = true;

        try
        {
            if (Application.Current?.Windows.Count > 0)
                EngancharVentana(Application.Current.Windows[0]);

            // El indicador de cada página se actualiza solo cuando cambia la conexión (evento
            // Cambio de MonitorConexion). Aquí NO se vuelve a tocar el árbol visual en cada
            // sondeo: hacerlo mientras WinUI calcula el layout lanzaba LayoutCycleException
            // y dejaba la aplicación colgada.

            Registro.Info("Conexion", "Indicador de conexión listo.");
        }
        catch (Exception ex)
        {
            Registro.Aviso("Conexion", $"No se pudo instalar el indicador: {ex.Message}");
        }
    }

    /// <summary>
    /// Ejecuta una acción en el hilo de la interfaz. El sondeo de conexión corre en segundo
    /// plano y tocar controles desde ahí corrompe la vista: siempre se pasa por el despachador.
    /// </summary>
    private static void Despachar(Action accion)
    {
        try
        {
            var pagina = Application.Current?.Windows.Count > 0
                ? Application.Current.Windows[0].Page
                : null;

            if (pagina is not null)
            {
                pagina.Dispatcher.Dispatch(accion);
                return;
            }

            // Sin ventana todavía: se ejecuta en el hilo principal de MAUI.
            MainThread.BeginInvokeOnMainThread(accion);
        }
        catch (Exception ex)
        {
            Registro.Aviso("Conexion", $"No se pudo despachar al hilo de la interfaz: {ex.Message}");
        }
    }

    /// <summary>
    /// Se engancha a la raíz actual. Se llama al arrancar y cada vez que se cambia la página
    /// principal (login → menú, cerrar sesión…).
    /// </summary>
    public static void EngancharVentana(Window? ventana)
    {
        if (ventana is null) return;

        Preparar(ventana.Page);

        if (ventana.Page is Shell shell)
        {
            shell.Navigated -= OnNavegadoShell;
            shell.Navigated += OnNavegadoShell;
            OnNavegadoShell(shell, null);
        }
    }

    private static void OnNavegadoShell(object? sender, ShellNavigatedEventArgs? e)
    {
        Despachar(() =>
        {
            try
            {
                if (sender is not Shell shell) return;

                // La página visible y las que tenga apiladas dentro de la pestaña.
                if (shell.CurrentPage is { } actual) BarraConexion.Superponer(actual);

                var pila = shell.CurrentPage?.Navigation?.NavigationStack;
                if (pila is null) return;
                foreach (var pagina in pila)
                    BarraConexion.Superponer(pagina);
            }
            catch (Exception ex)
            {
                Registro.Aviso("Conexion", $"No se pudo poner el indicador tras navegar: {ex.Message}");
            }
        });
    }

    /// <summary>Prepara una página raíz o de pila: indicador y escucha de nuevas páginas.</summary>
    private static void Preparar(Page? pagina)
    {
        if (pagina is null) return;

        try
        {
            BarraConexion.Superponer(pagina);

            if (pagina is NavigationPage nav)
            {
                nav.Pushed -= OnCambioPila;
                nav.Pushed += OnCambioPila;
                nav.Popped -= OnCambioPila;
                nav.Popped += OnCambioPila;
                nav.PoppedToRoot -= OnCambioPila;
                nav.PoppedToRoot += OnCambioPila;

                foreach (var p in nav.Navigation.NavigationStack)
                    BarraConexion.Superponer(p);
            }
        }
        catch (Exception ex)
        {
            Registro.Aviso("Conexion", $"No se pudo preparar {pagina.GetType().Name}: {ex.Message}");
        }
    }

    private static void OnCambioPila(object? sender, NavigationEventArgs e)
    {
        // La página que se acaba de mostrar lleva su propio indicador.
        if (e.Page is not null) BarraConexion.Superponer(e.Page);
    }
}
