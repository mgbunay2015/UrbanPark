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
		// Base local (SQLite): se crea al arrancar para que el QR y los vehículos estén
		// disponibles incluso sin internet.
		try { Servicios.BaseLocal.Iniciar(); }
		catch (Exception ex) { Registro.Error("App", "No se pudo iniciar la base local al arrancar.", ex); }

		var ventana = new Window(Ui.NavegacionConMarca(new Views.vLogin()));
		ventana.Created += (_, _) =>
		{
			Connectivity.ConnectivityChanged += OnCambioConectividad;
			_ = ApiCliente.SincronizarAsync();

			// Punto verde/rojo de conexión en todas las pantallas.
			Servicios.IndicadorConexion.EngancharVentana(ventana);
		};
		return ventana;
	}

	/// <summary>
	/// Al volver la conexión se envían los cambios guardados. Si no había sesión (o era la de
	/// relleno del modo sin conexión) se recupera una real con el equipo vinculado y, si sale
	/// bien, se avisa al usuario de que sus cambios ya se sincronizaron.
	/// </summary>
	private static async void OnCambioConectividad(object? sender, ConnectivityChangedEventArgs e)
	{
		if (e.NetworkAccess != NetworkAccess.Internet) return;

		try
		{
			var habia = AlmacenLocal.Pendientes;
			var resultado = await ApiCliente.SincronizarAsync();
			if (habia == 0) return;

			// Aviso solo si había algo que enviar y hubo un cambio real de estado.
			var pagina = Application.Current?.Windows.Count > 0
				? Application.Current.Windows[0].Page
				: null;

			pagina?.Dispatcher.Dispatch(async () =>
			{
				try
				{
					if (pagina is Page p)
						await Servicios.Ui.Informar(p, resultado.Mensaje);
				}
				catch (Exception)
				{
					// la página pudo cambiar mientras se mostraba el aviso
				}
			});
		}
		catch (Exception ex)
		{
			Registro.Error("App", "Falló la sincronización al volver la conexión.", ex);
		}
	}
}
