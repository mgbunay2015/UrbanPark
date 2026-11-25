using Microsoft.Extensions.Logging;
using UrbanPark.App.Servicios;

namespace UrbanPark.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// Archivo de registro + manejadores globales de excepciones. Sin esto, una excepción
		// en un manejador "async void" (los de los botones) cerraba el proceso con
		// 0xFFFFFFFF sin dejar ningún rastro de la causa.
		Registro.Instalar();

		// Punto verde/rojo de conexión: vigila internet real y el servidor cada pocos segundos,
		// y lo pinta en todas las pantallas.
		MonitorConexion.Iniciar();
		IndicadorConexion.Instalar();

		return builder.Build();
	}
}
