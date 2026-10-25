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
                    Text = "Cierra esta cuenta y vuelve a la pantalla de ingreso.",
                    Style = Ui.EstiloDe("SubtituloPagina"),
                    HorizontalTextAlignment = TextAlignment.Center
                },
                salir
            }
        };
    }

    private async Task SalirAsync()
    {
        if (!await Ui.Confirmar(this, "Salir", "¿Cerrar sesión y volver al ingreso?"))
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
