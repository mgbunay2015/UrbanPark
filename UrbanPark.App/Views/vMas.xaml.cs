using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

public partial class vMas : ContentPage
{
    public vMas()
    {
        InitializeComponent();
        Construir();
    }

    private void Construir()
    {
        lista.Children.Clear();
        Boton("Cómo llegar", "El estacionamiento está a 500 m", () => Navigation.PushAsync(new vLlegada()));
        Boton("Plazas", "Mapa del parqueadero", () => Navigation.PushAsync(new vPlazas()));
        Boton("Vehículos", "Registrar y dar de baja", () => Navigation.PushAsync(new vVehiculos()));
        if (Sesion.RolPrincipal != "OPERADOR")
            Boton("Asignar con IA", "Elegir plaza según la persona", () => Navigation.PushAsync(new vIa()));
        Boton("Tickets", "Cobrar y cerrar", () => Navigation.PushAsync(new vTickets()));
        Boton("Hoy", "Día, lista y actividad", () => Navigation.PushAsync(new vHoy()));
        if (Sesion.EsAdmin)
        {
            Boton("Reporte", "Dinero, descuentos y gráficos", () => Navigation.PushAsync(new vReporte()));
            Boton("Usuarios", "Cuentas del parqueadero", () => Navigation.PushAsync(new vUsuarios()));
        }
        Boton("Perfil", "Datos de la cuenta", () => Navigation.PushAsync(new vPerfil()));
        Boton("Sesión", "Token y datos de acceso", () => Navigation.PushAsync(new vToken()));
        Boton("Cerrar sesión", "Salir de esta cuenta", SalirAsync);
    }

    private void Boton(string titulo, string detalle, Func<Task> accion)
    {
        var pila = new VerticalStackLayout
        {
            Spacing = 2,
            Padding = new Thickness(8, 6),
            Children =
            {
                new Label
                {
                    Text = titulo,
                    FontFamily = "OpenSansSemibold",
                    FontSize = 13,
                    TextColor = Ui.ColorDe("TextoClaro")
                },
                new Label
                {
                    Text = detalle,
                    FontSize = 12,
                    TextColor = Ui.ColorDe("TextoClaroSuave")
                }
            }
        };
        var tarjeta = new Border
        {
            Style = Ui.EstiloDe("Tarjeta"),
            Content = pila
        };
        tarjeta.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await accion()) });
        lista.Add(tarjeta);
    }

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
