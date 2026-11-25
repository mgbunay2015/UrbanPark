using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>Pestaña inferior para cerrar la sesión y volver al login.</summary>
public class vSalir : ContentPage
{
    public vSalir()
    {
        Title = "Salir";
        Shell.SetNavBarIsVisible(this, false);
        BackgroundColor = Color.FromArgb("#0A0E13");

        var salir = new Button
        {
            Text = "Cerrar sesión",
            Style = Ui.EstiloDe("BotonPeligro"),
            HeightRequest = 36
        };
        salir.Clicked += async (_, _) => await SalirAsync();

        // Acción aparte: cerrar sesión nunca borra datos, porque son los que permiten
        // seguir usando la app sin internet.
        var borrar = new Button
        {
            Text = "Borrar los datos de este equipo",
            Style = Ui.EstiloDe("BotonContornoMarca"),
            HeightRequest = 34,
            FontSize = 11
        };
        borrar.Clicked += async (_, _) => await BorrarDatosLocalesAsync();

        Content = new VerticalStackLayout
        {
            Padding = new Thickness(12, 12),
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            MaximumWidthRequest = 360,
            Children =
            {
                new Label
                {
                    Text = "Salir",
                    Style = Ui.EstiloDe("TituloPagina"),
                    HorizontalTextAlignment = TextAlignment.Center
                },
                new Label
                {
                    Text = "Cierra esta cuenta y vuelve a la pantalla de ingreso. Los datos guardados se conservan para poder entrar sin internet.",
                    Style = Ui.EstiloDe("SubtituloPagina"),
                    HorizontalTextAlignment = TextAlignment.Center
                },
                salir,
                borrar
            }
        };
    }

    private async Task SalirAsync()
    {
        if (!await Ui.Confirmar(this, "Salir", "¿Cerrar sesión y volver al ingreso?"))
            return;

        var usuarioAnterior = Sesion.Username;

        Sesion.Cerrar();
        PaseSalida.Limpiar();

        // El cierre de sesión NO borra la base local: los vehículos, los tickets y las plazas
        // guardados son justo lo que permite volver a entrar y operar sin internet. Antes se
        // llamaba a LimpiarUsuario y la app quedaba sin datos en el siguiente arranque sin red.
        Registro.Info("Salir", $"Cerró sesión {usuarioAnterior}; los datos locales se conservan.");

        // Se cambia la página fuera del evento del botón: si no, WinUI se cae al
        // reemplazar el Shell mientras procesa el clic.
        await Task.Delay(60);
        if (Application.Current?.Windows.Count > 0)
        {
            var ventana = Application.Current.Windows[0];
            ventana.Page = Ui.NavegacionConMarca(new vLogin());
            IndicadorConexion.EngancharVentana(ventana);
        }
    }

    /// <summary>
    /// Borra los datos de esa cuenta del equipo. Es una acción aparte, explícita: si se
    /// hiciera al cerrar sesión se perdería la copia que permite usar la app sin internet.
    /// </summary>
    private async Task BorrarDatosLocalesAsync()
    {
        var usuario = Sesion.Username;
        if (string.IsNullOrWhiteSpace(usuario)) return;

        if (!await Ui.Confirmar(this, "Borrar datos del equipo",
                $"Se eliminarán de este equipo los vehículos, tickets y pases de «{usuario}». " +
                "Tendrás que conectarte a internet para volver a descargarlos. ¿Continuar?"))
            return;

        try
        {
            AlmacenLocal.LimpiarUsuario(usuario);
            CredencialLocal.Limpiar(usuario);
            Registro.Info("Salir", $"Se borraron los datos locales de {usuario}.");
            await Ui.Exito(this, $"Se borraron los datos de «{usuario}» en este equipo.");
        }
        catch (Exception ex)
        {
            Registro.Error("Salir", $"No se pudieron borrar los datos locales de {usuario}.", ex);
            await Ui.MostrarExcepcion(this, ex);
        }
    }
}
