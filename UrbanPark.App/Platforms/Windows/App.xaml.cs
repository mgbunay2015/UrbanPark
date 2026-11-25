using Microsoft.UI.Xaml;
using UrbanPark.App.Servicios;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace UrbanPark.App.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();

		// Red de seguridad: una excepción en un manejador "async void" (los clics de botones)
		// se escapaba hasta WinUI y cerraba la app con el código 0xFFFFFFFF. Aquí se marca
		// como atendida para que la ventana siga abierta y el fallo quede en el registro.
		UnhandledException += OnExcepcionNoControlada;
	}

	private static void OnExcepcionNoControlada(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
	{
		Registro.Fatal("WinUI", "Excepción no controlada en la interfaz (WinUI).", e.Exception);

		// Además de la pila, se anotan los tipos de las páginas abiertas: ayuda a saber qué
		// pantalla disparó el fallo cuando no hay mensaje útil (LayoutCycleException no lo trae).
		try
		{
			var raiz = Microsoft.Maui.Controls.Application.Current?.Windows.Count > 0
				? Microsoft.Maui.Controls.Application.Current.Windows[0].Page
				: null;

			var abiertas = raiz switch
			{
				Microsoft.Maui.Controls.Shell shell => $"Shell[{shell.CurrentPage?.GetType().Name}]",
				Microsoft.Maui.Controls.NavigationPage nav =>
					"Nav[" + string.Join(" > ", nav.Navigation.NavigationStack.Select(p => p.GetType().Name)) + "]",
				null => "sin página",
				_ => raiz.GetType().Name
			};
			Registro.Error("WinUI", $"Pantalla donde ocurrió: {abiertas}");
		}
		catch (Exception)
		{
			// el detalle es opcional
		}

		// La excepción se marca como atendida para que el proceso no muera, pero NO se muestra
		// ningún diálogo desde aquí: mostrar uno durante el paso de layout vuelve a disparar el
		// mismo ciclo (LayoutCycleException) y deja la app colgada consumiendo memoria. El fallo
		// queda en el archivo de registro, que es donde debe mirarse.
		e.Handled = true;
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
