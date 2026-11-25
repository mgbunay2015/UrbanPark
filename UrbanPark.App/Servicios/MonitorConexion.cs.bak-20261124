namespace UrbanPark.App.Servicios;

/// <summary>Estado de la conexión tal como lo ve el usuario.</summary>
public enum EstadoConexion
{
    /// <summary>No se llega al servidor: se trabaja con los datos guardados en SQLite.</summary>
    SinConexion,

    /// <summary>Hay internet y el servidor responde: todo va en línea.</summary>
    EnLinea
}

/// <summary>
/// Vigila cada pocos segundos si hay <b>internet de verdad</b> y si el servidor responde, para
/// pintar el punto verde/rojo de las pantallas.
///
/// Antes solo se preguntaba al API local (127.0.0.1:8090), que contesta aunque el equipo esté
/// sin internet: por eso el punto seguía verde sin red. Ahora se comprueban las dos cosas por
/// separado y el punto solo se pone verde cuando ambas responden.
/// </summary>
public static class MonitorConexion
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    /// <summary>Comprobación de internet real: responde 204 sin descargar cuerpo.</summary>
    private static readonly string[] Sondas =
    [
        "https://connectivitycheck.gstatic.com/generate_204",
        "https://clients3.google.com/generate_204",
        "https://cloudflare.com/cdn-cgi/trace"
    ];

    private static int _comprobando;
    private static bool _iniciado;
    private static bool _hayInternet;
    private static bool _servidorResponde;

    /// <summary>Estado actual. Arranca en «sin conexión» hasta la primera comprobación.</summary>
    public static EstadoConexion Estado { get; private set; } = EstadoConexion.SinConexion;

    /// <summary>True solo si hay internet real y el servidor contesta.</summary>
    public static bool EnLinea => Estado == EstadoConexion.EnLinea;

    /// <summary>True si la última comprobación encontró salida a internet.</summary>
    public static bool HayInternet => _hayInternet;

    /// <summary>Texto que acompaña al punto.</summary>
    public static string Descripcion => Estado switch
    {
        EstadoConexion.EnLinea => "En línea",
        _ when !_hayInternet => "Sin internet",
        _ => "Sin servidor"          // hay red, pero el API no responde
    };

    /// <summary>Se dispara cuando cambia el estado (también lo usan los avisos de la app).</summary>
    public static event EventHandler? Cambio;

    /// <summary>Se dispara en cada comprobación, cambie o no el estado.</summary>
    public static event EventHandler? Revisado;

    /// <summary>
    /// Arranca la vigilancia: comprueba al instante y luego cada pocos segundos, además de
    /// reaccionar a los avisos de red del sistema.
    /// </summary>
    public static void Iniciar()
    {
        if (_iniciado) return;
        _iniciado = true;

        try
        {
            Connectivity.ConnectivityChanged += (_, _) => _ = ComprobarAsync();
        }
        catch (Exception ex)
        {
            Registro.Aviso("Conexion", $"No se pudo escuchar los cambios de red: {ex.Message}");
        }

        _ = VigilarAsync();
    }

    /// <summary>Bucle de sondeo continuo, en segundo plano.</summary>
    private static async Task VigilarAsync()
    {
        while (true)
        {
            try
            {
                await ComprobarAsync();

                // Con conexión se revisa cada 8 s (basta para detectar una caída); sin ella
                // cada 5 s, para avisar del regreso lo antes posible sin gastar datos.
                var espera = EnLinea ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(5);
                await Task.Delay(espera);
            }
            catch (Exception ex)
            {
                Registro.Aviso("Conexion", $"El sondeo de conexión falló: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }
    }

    /// <summary>
    /// Comprueba internet real y servidor. Es segura de llamar muchas veces: si ya hay una
    /// comprobación en curso, la nueva se descarta.
    /// </summary>
    public static async Task ComprobarAsync()
    {
        if (Interlocked.Exchange(ref _comprobando, 1) == 1) return;

        try
        {
            var internet = await HayInternetRealAsync();
            var servidor = await ServidorRespondeAsync();

            _hayInternet = internet;
            _servidorResponde = servidor;
        }
        catch (Exception ex)
        {
            Registro.Aviso("Conexion", $"No se pudo comprobar la conexión: {ex.Message}");
            _hayInternet = false;
            _servidorResponde = false;
        }
        finally
        {
            Interlocked.Exchange(ref _comprobando, 0);
        }

        Recalcular();

        // Aviso en cada revisión (cambie o no el estado): sirve para que la red de seguridad
        // coloque el indicador en la página que se esté mostrando.
        try
        {
            Revisado?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Registro.Aviso("Conexion", $"Un aviso de revisión falló: {ex.Message}");
        }
    }

    /// <summary>
    /// Internet real: se pide una URL pública que responde sin cuerpo. Si ninguna contesta,
    /// se considera que no hay salida a internet.
    /// </summary>
    private static async Task<bool> HayInternetRealAsync()
    {
        foreach (var sonda in Sondas)
        {
            try
            {
                using var respuesta = await Http.GetAsync(sonda, HttpCompletionOption.ResponseHeadersRead);
                if (respuesta.IsSuccessStatusCode) return true;
            }
            catch (Exception)
            {
                // se prueba la siguiente sonda
            }
        }

        // Último recurso: si el sistema puede resolver un nombre, es que hay red.
        try
        {
            var nombres = await System.Net.Dns.GetHostAddressesAsync("dns.google");
            return nombres.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>¿El servidor de la app contesta? (aunque no haya internet, si está en el mismo equipo).</summary>
    private static async Task<bool> ServidorRespondeAsync()
    {
        try
        {
            using var respuesta = await Http.GetAsync(ApiCliente.BaseUrl.TrimEnd('/') + "/",
                HttpCompletionOption.ResponseHeadersRead);
            // Cualquier respuesta (incluido 401) significa que el servidor está vivo.
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Recalcula el estado a partir de lo comprobado y avisa si cambió.</summary>
    private static void Recalcular()
    {
        // Verde solo con internet real y servidor vivo: sin internet se trabaja con SQLite.
        var nuevo = _hayInternet && _servidorResponde ? EstadoConexion.EnLinea : EstadoConexion.SinConexion;

        if (Estado == nuevo) return;
        Estado = nuevo;

        Registro.Info("Conexion", nuevo == EstadoConexion.EnLinea
            ? "En línea."
            : $"Sin conexión (internet: {_hayInternet}, servidor: {_servidorResponde}).");

        try
        {
            Cambio?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Registro.Aviso("Conexion", $"Un indicador falló al actualizarse: {ex.Message}");
        }
    }

    /// <summary>
    /// Una petición real del API salió bien: el servidor está vivo. No se pone verde por sí
    /// solo, porque el API local responde aunque no haya internet.
    /// </summary>
    public static void MarcarEnLinea()
    {
        _servidorResponde = true;
        Recalcular();
    }

    /// <summary>Una petición real no pudo conectar: se pinta sin conexión de inmediato.</summary>
    public static void MarcarSinConexion()
    {
        _servidorResponde = false;
        Recalcular();
    }

    /// <summary>Fuerza una comprobación y espera el resultado (para «Reintentar» o al abrir una pantalla).</summary>
    public static async Task<bool> RevisarAhoraAsync()
    {
        await ComprobarAsync();
        return EnLinea;
    }
}
