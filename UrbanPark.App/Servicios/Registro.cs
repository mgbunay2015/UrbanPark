using System.Globalization;
using System.Text;

namespace UrbanPark.App.Servicios;

/// <summary>
/// Registro en archivo de todo lo que pasa en la app, para poder diagnosticar un cierre o un
/// fallo sin depender de Visual Studio.
///
/// Escribe un archivo por día dentro de <c>AppDataDirectory/registro</c>
/// (<c>urbanpark-AAAA-MM-DD.log</c>). El registro nunca puede tumbar la app: todos los
/// métodos capturan sus propias excepciones y escriben en segundo plano.
/// </summary>
public static class Registro
{
    private const string Carpeta = "registro";
    private const string Prefijo = "urbanpark-";

    /// <summary>Al pasar de este tamaño el archivo del día rota a un .1, .2…</summary>
    private const long TamanoMaximo = 4L * 1024 * 1024;

    /// <summary>Días de historia que se conservan al limpiar.</summary>
    public const int DiasConservados = 14;

    private static readonly object Candado = new();
    private static readonly List<string> Pendientes = [];
    private static bool _escribiendo;
    private static bool _instalado;

    /// <summary>Carpeta donde están los archivos. Útil para mostrarla al usuario.</summary>
    public static string RutaCarpeta
    {
        get
        {
            try
            {
                return Path.Combine(FileSystem.AppDataDirectory, Carpeta);
            }
            catch (Exception)
            {
                // Antes de que MAUI termine de arrancar no hay AppDataDirectory.
                return Path.Combine(Path.GetTempPath(), Carpeta);
            }
        }
    }

    /// <summary>Archivo del día en curso.</summary>
    public static string RutaActual => Path.Combine(RutaCarpeta, $"{Prefijo}{DateTime.Now:yyyy-MM-dd}.log");

    /// <summary>True si ya se engancharon los manejadores globales.</summary>
    public static bool Instalado => _instalado;

    // ================================================================ enganche global
    /// <summary>
    /// Engancha los manejadores de excepciones no controladas. Se llama una sola vez al
    /// arrancar; si se llama de nuevo no hace nada.
    /// </summary>
    public static void Instalar()
    {
        if (_instalado) return;
        _instalado = true;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Escribir("FATAL", "AppDomain", "Excepción no controlada: el proceso va a terminar.", ex,
                $"Terminando: {e.IsTerminating}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Escribir("ERROR", "Tarea", "Excepción de una tarea no observada.", e.Exception);
            e.SetObserved();
        };

#if ANDROID
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
            Escribir("FATAL", "Android", "Excepción no controlada en Android.", e.Exception);
#endif

        Info("App", $"Registro iniciado · {DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString} " +
                    $"({DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}) · v{AppInfo.Current.VersionString}");
        Limpiar();
    }

    // ================================================================ niveles
    public static void Info(string categoria, string mensaje) => Escribir("INFO", categoria, mensaje, null);

    public static void Aviso(string categoria, string mensaje) => Escribir("AVISO", categoria, mensaje, null);

    public static void Error(string categoria, string mensaje, Exception? ex = null) =>
        Escribir("ERROR", categoria, mensaje, ex);

    public static void Error(string categoria, Exception ex, string? contexto = null) =>
        Escribir("ERROR", categoria, contexto ?? ex.Message, ex);

    public static void Fatal(string categoria, string mensaje, Exception? ex = null) =>
        Escribir("FATAL", categoria, mensaje, ex);

    // ================================================================ escritura
    /// <summary>
    /// Añade una línea al archivo. El trabajo real se hace en un hilo del grupo de trabajo
    /// para no frenar la interfaz; el orden de las líneas se mantiene con una cola.
    /// </summary>
    public static void Escribir(string nivel, string categoria, string mensaje, Exception? ex, string? extra = null)
    {
        string linea;
        try
        {
            linea = Formatear(nivel, categoria, mensaje, ex, extra);
        }
        catch (Exception)
        {
            return;   // si ni siquiera se puede formatear, no hay nada que hacer
        }

        // Además de ir al archivo, se mantiene en la ventana Salida durante el desarrollo.
        System.Diagnostics.Debug.WriteLine(linea.TrimEnd());

        lock (Candado)
        {
            Pendientes.Add(linea);
            if (_escribiendo) return;
            _escribiendo = true;
        }

        _ = Task.Run(Volcar);
    }

    private static string Formatear(string nivel, string categoria, string mensaje, Exception? ex, string? extra)
    {
        var sb = new StringBuilder(256);
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        sb.Append(" [").Append(nivel.PadRight(5)).Append("] ");
        sb.Append(categoria).Append(" | ").Append(mensaje);
        if (!string.IsNullOrWhiteSpace(extra)) sb.Append(" · ").Append(extra);
        sb.AppendLine();

        if (ex is not null)
        {
            sb.AppendLine("    " + ex.GetType().FullName + ": " + ex.Message);
            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                sb.AppendLine(Indentar(ex.StackTrace!, "      "));

            var interior = ex.InnerException;
            var profundidad = 0;
            while (interior is not null && profundidad < 5)
            {
                sb.AppendLine("    ---> " + interior.GetType().FullName + ": " + interior.Message);
                if (!string.IsNullOrWhiteSpace(interior.StackTrace))
                    sb.AppendLine(Indentar(interior.StackTrace!, "      "));
                interior = interior.InnerException;
                profundidad++;
            }
        }

        return sb.ToString();
    }

    private static string Indentar(string texto, string sangria) =>
        string.Join(Environment.NewLine, texto.Split('\n').Select(l => sangria + l.TrimEnd()));

    /// <summary>Vacía la cola en el archivo. Si falla, se descarta: el registro no debe interrumpir la app.</summary>
    private static void Volcar()
    {
        while (true)
        {
            string[] lote;
            lock (Candado)
            {
                if (Pendientes.Count == 0)
                {
                    _escribiendo = false;
                    return;
                }

                lote = [.. Pendientes];
                Pendientes.Clear();
            }

            try
            {
                Directory.CreateDirectory(RutaCarpeta);
                RotarSiHaceFalta();
                File.AppendAllText(RutaActual, string.Concat(lote), Encoding.UTF8);
            }
            catch (Exception)
            {
                // Disco lleno, permisos, archivo bloqueado… se pierde este lote y se sigue.
            }
        }
    }

    /// <summary>Si el archivo del día pasó del tope, se renombra para no crecer sin límite.</summary>
    private static void RotarSiHaceFalta()
    {
        try
        {
            var actual = RutaActual;
            var info = new FileInfo(actual);
            if (!info.Exists || info.Length < TamanoMaximo) return;

            // urbanpark-2026-11-21.1.log, .2, .3…
            for (var i = 1; i < 100; i++)
            {
                var destino = Path.Combine(RutaCarpeta,
                    $"urbanpark-{DateTime.Now:yyyy-MM-dd}.{i}.log");
                if (File.Exists(destino)) continue;
                File.Move(actual, destino);
                return;
            }
        }
        catch (Exception)
        {
            // si no se puede rotar se sigue escribiendo en el mismo archivo
        }
    }

    // ================================================================ lectura
    /// <summary>Archivos de registro del más nuevo al más viejo.</summary>
    public static IReadOnlyList<FileInfo> Archivos()
    {
        try
        {
            var carpeta = new DirectoryInfo(RutaCarpeta);
            if (!carpeta.Exists) return [];
            return carpeta.GetFiles("*.log")
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Últimas <paramref name="lineas"/> líneas, leyendo del archivo más reciente hacia atrás.</summary>
    public static string Ultimas(int lineas = 200)
    {
        try
        {
            var archivos = Archivos();
            if (archivos.Count == 0)
                return "Todavía no hay registro: la app aún no ha escrito nada.";

            var sb = new StringBuilder();
            var restantes = lineas;

            foreach (var archivo in archivos)
            {
                if (restantes <= 0) break;
                using var stream = new FileStream(archivo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var lector = new StreamReader(stream, Encoding.UTF8);

                var todas = new List<string>();
                while (lector.ReadLine() is { } linea) todas.Add(linea);

                var tomar = todas.Skip(Math.Max(0, todas.Count - restantes)).ToList();
                restantes -= tomar.Count;

                sb.Insert(0, $"=== {archivo.Name} ==={Environment.NewLine}" +
                             string.Join(Environment.NewLine, tomar) + Environment.NewLine + Environment.NewLine);
            }

            return sb.Length == 0 ? "El registro está vacío." : sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return "No se pudo leer el registro: " + ex.Message;
        }
    }

    /// <summary>Ruta del archivo más reciente (o la del día si aún no existe).</summary>
    public static string ArchivoReciente()
    {
        var archivos = Archivos();
        return archivos.Count > 0 ? archivos[0].FullName : RutaActual;
    }

    // ================================================================ mantenimiento
    /// <summary>Borra los archivos de más de <see cref="DiasConservados"/> días.</summary>
    public static void Limpiar()
    {
        try
        {
            var carpeta = new DirectoryInfo(RutaCarpeta);
            if (!carpeta.Exists) return;

            var limite = DateTime.Now.AddDays(-DiasConservados);
            foreach (var archivo in carpeta.GetFiles("*.log"))
            {
                if (archivo.LastWriteTime >= limite) continue;
                try { archivo.Delete(); } catch (Exception) { /* en uso */ }
            }
        }
        catch (Exception)
        {
            // la limpieza es opcional
        }
    }

    /// <summary>Borra todo el registro.</summary>
    public static int BorrarTodo()
    {
        var borrados = 0;
        try
        {
            foreach (var archivo in Archivos())
            {
                try { archivo.Delete(); borrados++; } catch (Exception) { /* en uso */ }
            }
        }
        catch (Exception)
        {
            // se informa con lo que sí se pudo borrar
        }

        Info("App", "Registro limpiado por el usuario.");
        return borrados;
    }
}
