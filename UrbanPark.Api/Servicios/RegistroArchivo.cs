using System.Text;
using Microsoft.Extensions.Logging;

namespace UrbanPark.Api.Servicios;

/// <summary>
/// Proveedor de registro que escribe en un archivo por día, además de la consola.
///
/// La API solo escribía en la consola: al cerrar la ventana se perdía todo, así que un fallo
/// en una petición no dejaba rastro. Los archivos quedan en <c>logs/urbanpark-api-AAAA-MM-DD.log</c>
/// junto al ejecutable (o en <c>%LOCALAPPDATA%\UrbanParkApi\logs</c> si esa carpeta no admite
/// escritura, por ejemplo al publicar en Archivos de programa).
/// </summary>
public sealed class RegistroArchivoProvider : ILoggerProvider
{
    private readonly object _candado = new();
    private readonly string _carpeta;
    private readonly LogLevel _minimo;

    /// <summary>Al pasar de este tamaño el archivo del día rota a un .1, .2…</summary>
    private const long TamanoMaximo = 8L * 1024 * 1024;

    public RegistroArchivoProvider(string carpeta, LogLevel minimo = LogLevel.Information)
    {
        _carpeta = carpeta;
        _minimo = minimo;
        try { Directory.CreateDirectory(_carpeta); } catch (Exception) { /* se reintenta al escribir */ }
    }

    /// <summary>Carpeta donde se guardan los archivos.</summary>
    public string Carpeta => _carpeta;

    public ILogger CreateLogger(string categoryName) => new RegistroArchivoLogger(this, categoryName);

    public void Dispose() { }

    internal bool DebeEscribir(LogLevel nivel) => nivel >= _minimo && nivel != LogLevel.None;

    /// <summary>Añade una línea ya formateada al archivo del día.</summary>
    internal void Escribir(string linea)
    {
        lock (_candado)
        {
            try
            {
                Directory.CreateDirectory(_carpeta);
                var ruta = Path.Combine(_carpeta, $"urbanpark-api-{DateTime.Now:yyyy-MM-dd}.log");
                RotarSiHaceFalta(ruta);
                File.AppendAllText(ruta, linea + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception)
            {
                // Disco lleno, permisos o archivo bloqueado: el registro nunca debe tumbar la API.
            }
        }
    }

    private static void RotarSiHaceFalta(string ruta)
    {
        try
        {
            var info = new FileInfo(ruta);
            if (!info.Exists || info.Length < TamanoMaximo) return;

            var sinExtension = Path.Combine(info.DirectoryName!, Path.GetFileNameWithoutExtension(ruta));
            for (var i = 1; i < 100; i++)
            {
                var destino = $"{sinExtension}.{i}.log";
                if (File.Exists(destino)) continue;
                File.Move(ruta, destino);
                return;
            }
        }
        catch (Exception)
        {
            // si no se puede rotar se sigue escribiendo en el mismo archivo
        }
    }

    /// <summary>
    /// Carpeta de registro: junto al ejecutable si se puede escribir; si no, en los datos
    /// locales del usuario.
    /// </summary>
    public static string CarpetaPorDefecto()
    {
        var juntoAlExe = Path.Combine(AppContext.BaseDirectory, "logs");
        try
        {
            Directory.CreateDirectory(juntoAlExe);
            var prueba = Path.Combine(juntoAlExe, ".permiso");
            File.WriteAllText(prueba, "ok");
            File.Delete(prueba);
            return juntoAlExe;
        }
        catch (Exception)
        {
            var datos = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UrbanParkApi", "logs");
            try { Directory.CreateDirectory(datos); } catch (Exception) { /* último recurso */ }
            return datos;
        }
    }

    // ================================================================ logger
    private sealed class RegistroArchivoLogger(RegistroArchivoProvider proveedor, string categoria) : ILogger
    {
        public IDisposable? BeginScope<TEstado>(TEstado estado) where TEstado : notnull => null;

        public bool IsEnabled(LogLevel nivel) => proveedor.DebeEscribir(nivel);

        public void Log<TEstado>(LogLevel nivel, EventId evento, TEstado estado, Exception? excepcion,
            Func<TEstado, Exception?, string> formateador)
        {
            if (!IsEnabled(nivel)) return;

            try
            {
                var sb = new StringBuilder(256);
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.Append(" [").Append(Nombre(nivel).PadRight(5)).Append("] ");
                sb.Append(categoria);

                var mensaje = formateador(estado, excepcion);
                if (!string.IsNullOrWhiteSpace(mensaje)) sb.Append(" | ").Append(mensaje);

                if (excepcion is not null)
                {
                    sb.AppendLine();
                    sb.Append("    ").Append(excepcion.GetType().FullName).Append(": ").Append(excepcion.Message);
                    if (!string.IsNullOrWhiteSpace(excepcion.StackTrace))
                    {
                        sb.AppendLine();
                        sb.Append(string.Join(Environment.NewLine,
                            excepcion.StackTrace!.Split('\n').Select(l => "      " + l.TrimEnd())));
                    }
                }

                proveedor.Escribir(sb.ToString());
            }
            catch (Exception)
            {
                // formatear un mensaje nunca debe interrumpir una petición
            }
        }

        private static string Nombre(LogLevel nivel) => nivel switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "AVISO",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "FATAL",
            _ => "—"
        };
    }
}

/// <summary>Azúcar para registrar el proveedor de archivo en una línea.</summary>
public static class RegistroArchivoExtension
{
    public static ILoggingBuilder AddRegistroArchivo(this ILoggingBuilder builder, string carpeta,
        LogLevel minimo = LogLevel.Information)
    {
        builder.AddProvider(new RegistroArchivoProvider(carpeta, minimo));
        return builder;
    }
}
