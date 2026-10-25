using UrbanPark.App.Servicios;

namespace UrbanPark.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	// La app arranca en el login; al autenticarse se reemplaza la raíz por el menú principal
	// (equivalente a showLogin()/showApp() del frontend original).
	// La barra de navegación se pinta con los colores de la marca, no con el tema por defecto.
	protected override Window CreateWindow(IActivationState? activationState)
	{
		var ventana = new Window(Ui.NavegacionConMarca(new Views.vLogin()));
		ventana.Created += (_, _) =>
		{
			Connectivity.ConnectivityChanged += (_, _) => _ = ApiCliente.SincronizarAsync();
			_ = ApiCliente.SincronizarAsync();
		};
		return ventana;
	}
}
