using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using UrbanPark.App.Modelos;

namespace UrbanPark.App.Servicios;

/// <summary>
/// Cliente HTTP unico de la app. Se encarga de:
///   - la URL base (configurable por plataforma),
///   - enviar el token Bearer en cada llamada,
///   - refrescar el token automaticamente cuando esta por expirar,
///   - traducir los errores del API a mensajes claros en español.
/// </summary>
public static class ApiCliente
{
    /// <summary>
    /// URL base del API.
    ///   - Emulador Android: 10.0.2.2 apunta al localhost del PC.
    ///   - Dispositivo fisico: pon la IP de tu PC en la red WiFi (ej. http://192.168.1.50:8090).
    ///   - Windows/Mac: http://localhost:8090
    /// El puerto se puede cambiar en appsettings.json del API (clave "Urls").
    /// </summary>
    public static string BaseUrl { get; set; } = UrlPorDefecto();

    private static string UrlPorDefecto()
    {
#if ANDROID
        return EsEmulador() ? "http://10.0.2.2:8090" : "http://192.168.100.38:8090";
#else
        return "http://127.0.0.1:8090";
#endif
    }

    private static bool EsEmulador()
    {
        if (DeviceInfo.Current.DeviceType == DeviceType.Virtual)
            return true;

#if ANDROID
        var huella = Android.OS.Build.Fingerprint ?? "";
        var modelo = Android.OS.Build.Model ?? "";
        var hardware = Android.OS.Build.Hardware ?? "";
        return huella.Contains("generic", StringComparison.OrdinalIgnoreCase)
            || huella.Contains("emulator", StringComparison.OrdinalIgnoreCase)
            || modelo.Contains("sdk", StringComparison.OrdinalIgnoreCase)
            || hardware.Contains("goldfish", StringComparison.OrdinalIgnoreCase)
            || hardware.Contains("ranchu", StringComparison.OrdinalIgnoreCase);
#else
        return false;
#endif
    }

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(180)   // el reporte con IA puede tardar
    };

    /// <summary>La última lectura se sirvió desde la copia del equipo, no del servidor.</summary>
    public static bool SinConexion { get; private set; }

    private static int _sincronizando;

    // ================================================================ nucleo
    public static async Task<T> GetAsync<T>(string ruta) => await EnviarAsync<T>(HttpMethod.Get, ruta, null);

    public static async Task<T> PostAsync<T>(string ruta, object? cuerpo = null) =>
        await EnviarAsync<T>(HttpMethod.Post, ruta, cuerpo);

    public static async Task<T> PutAsync<T>(string ruta, object? cuerpo = null) =>
        await EnviarAsync<T>(HttpMethod.Put, ruta, cuerpo);

    public static async Task<T> PatchAsync<T>(string ruta, object? cuerpo = null) =>
        await EnviarAsync<T>(HttpMethod.Patch, ruta, cuerpo);

    public static async Task<T> DeleteAsync<T>(string ruta) => await EnviarAsync<T>(HttpMethod.Delete, ruta, null);

    private static async Task<T> EnviarAsync<T>(HttpMethod metodo, string ruta, object? cuerpo, bool reintento = false, bool encolar = true)
    {
        // refresco preventivo (el original refrescaba si faltaban menos de 15 s)
        if (Sesion.PorExpirar && !ruta.Contains("/api/auth/") && !reintento)
        {
            var refrescado = await RefrescarAsync();
            // sin internet no se cierra la sesión: la app sigue con los datos guardados
            if (!refrescado && HayRed()) Sesion.Cerrar();
        }

        if (!HayRed())
            return await SinRedAsync<T>(metodo, ruta, cuerpo, encolar);

        using var peticion = new HttpRequestMessage(metodo, BaseUrl.TrimEnd('/') + ruta);

        if (cuerpo is not null)
            peticion.Content = new StringContent(JsonSerializer.Serialize(cuerpo, JsonUrbanPark.Opciones),
                Encoding.UTF8, "application/json");

        if (Sesion.Activa)
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Sesion.Actual!.AccessToken);

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await Http.SendAsync(peticion);
        }
        catch (TaskCanceledException)
        {
            throw new ApiExcepcion(0, "El servidor tardó demasiado en responder. La IA puede estar procesando: intenta de nuevo en unos segundos.");
        }
        catch (HttpRequestException)
        {
            return await SinRedAsync<T>(metodo, ruta, cuerpo, encolar);
        }

        using (respuesta)
        {
            var texto = await respuesta.Content.ReadAsStringAsync();

            if (respuesta.IsSuccessStatusCode)
            {
                if (metodo == HttpMethod.Get && !string.IsNullOrWhiteSpace(texto))
                    AlmacenLocal.GuardarCache(ClaveCache(ruta), texto);
                SinConexion = false;
                if (encolar && AlmacenLocal.Pendientes > 0)
                    _ = SincronizarAsync();

                if (typeof(T) == typeof(string)) return (T)(object)texto;
                if (string.IsNullOrWhiteSpace(texto)) return default!;

                var valor = JsonSerializer.Deserialize<T>(texto, JsonUrbanPark.Opciones);
                return valor ?? default!;
            }

            // 401: un solo reintento tras refrescar el token
            if (respuesta.StatusCode == HttpStatusCode.Unauthorized && !reintento)
            {
                if (await RefrescarAsync())
                    return await EnviarAsync<T>(metodo, ruta, cuerpo, reintento: true);
                Sesion.Cerrar();
            }

            throw new ApiExcepcion((int)respuesta.StatusCode, MensajeAmigable((int)respuesta.StatusCode, texto, ruta))
            {
                // 401 con esta marca = el usuario tiene el autenticador de Google activo
                RequiereTotp = (int)respuesta.StatusCode == 401 &&
                               texto.Contains("TOTP_REQUIRED", StringComparison.Ordinal)
            };
        }
    }

    public static async Task SincronizarAsync()
    {
        if (!HayRed() || !Sesion.Activa) return;
        if (Interlocked.CompareExchange(ref _sincronizando, 1, 0) != 0) return;

        try
        {
            foreach (var op in AlmacenLocal.PendientesDe(Sesion.Username))
            {
                if (!HayRed()) return;

                object? cuerpo = string.IsNullOrWhiteSpace(op.Cuerpo)
                    ? null
                    : JsonSerializer.Deserialize<JsonElement>(op.Cuerpo, JsonUrbanPark.Opciones);
                try
                {
                    await EnviarAsync<JsonElement>(new HttpMethod(op.Metodo), op.Ruta, cuerpo, encolar: false);
                    AlmacenLocal.Quitar(op.Id);
                }
                catch (ApiExcepcion ex) when (ex.Status is 0 or 401 or >= 500)
                {
                    return;
                }
                catch (ApiExcepcion)
                {
                    AlmacenLocal.Quitar(op.Id);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _sincronizando, 0);
        }
    }

    private static bool HayRed() =>
        Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    private static string ClaveCache(string ruta) => $"{Sesion.Username}|{ruta}";

    private static bool SeEncola(HttpMethod metodo, string ruta) =>
        metodo != HttpMethod.Get
        && !ruta.Contains("/api/auth/", StringComparison.OrdinalIgnoreCase)
        && !ruta.Contains("/api/ia/", StringComparison.OrdinalIgnoreCase);

    private static Task<T> SinRedAsync<T>(HttpMethod metodo, string ruta, object? cuerpo, bool encolar)
    {
        SinConexion = true;

        if (metodo == HttpMethod.Get
            && AlmacenLocal.TryGet(ClaveCache(ruta), out var copia)
            && !string.IsNullOrWhiteSpace(copia))
        {
            if (typeof(T) == typeof(string))
                return Task.FromResult((T)(object)copia);

            var valor = JsonSerializer.Deserialize<T>(copia, JsonUrbanPark.Opciones);
            return Task.FromResult(valor ?? default!);
        }

        if (encolar && SeEncola(metodo, ruta) && Sesion.Activa)
        {
            var json = cuerpo is null ? null : JsonSerializer.Serialize(cuerpo, JsonUrbanPark.Opciones);
            AlmacenLocal.Encolar(metodo.Method, ruta, json, Sesion.Username);
            throw new ApiExcepcion(0,
                "Sin internet. El cambio quedó guardado en este equipo y se sincronizará al volver la conexión.");
        }

        throw new ApiExcepcion(0, metodo == HttpMethod.Get
            ? "Sin internet y este equipo aún no tiene una copia de estos datos."
            : $"No se pudo conectar con el API en {BaseUrl}. Verifica que el servidor esté encendido y que la URL sea correcta.");
    }

    // ================================================================ refresco
    private static readonly SemaphoreSlim CandadoRefresco = new(1, 1);

    public static async Task<bool> RefrescarAsync()
    {
        if (Sesion.Actual is null || string.IsNullOrWhiteSpace(Sesion.Actual.RefreshToken)) return false;

        await CandadoRefresco.WaitAsync();
        try
        {
            if (!Sesion.PorExpirar && Sesion.Activa) return true;

            using var peticion = new HttpRequestMessage(HttpMethod.Post, BaseUrl.TrimEnd('/') + "/api/auth/refresh");
            peticion.Content = new StringContent(
                JsonSerializer.Serialize(new { refreshToken = Sesion.Actual.RefreshToken }, JsonUrbanPark.Opciones),
                Encoding.UTF8, "application/json");

            using var respuesta = await Http.SendAsync(peticion);
            if (!respuesta.IsSuccessStatusCode) return false;

            var texto = await respuesta.Content.ReadAsStringAsync();
            var nueva = JsonSerializer.Deserialize<SesionDto>(texto, JsonUrbanPark.Opciones);
            if (nueva is null) return false;

            await Sesion.GuardarAsync(nueva);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            CandadoRefresco.Release();
        }
    }

    // ================================================================ traduccion de errores
    /// <summary>
    /// Convierte la respuesta de error del API en un texto claro, replicando la tabla
    /// de traduccion del frontend original.
    /// </summary>
    public static string MensajeAmigable(int status, string? cuerpo, string ruta)
    {
        var mensaje = ExtraerMensaje(cuerpo);

        return status switch
        {
            0 => mensaje ?? "No se pudo conectar con el servidor. Revisa tu conexión y que el API esté encendido.",

            400 => !string.IsNullOrWhiteSpace(mensaje)
                ? mensaje!
                : "Revisa los datos enviados: falta algún campo obligatorio o el formato no es válido.",

            401 => !string.IsNullOrWhiteSpace(mensaje)
                ? mensaje!
                : "Tu sesión expiró o las credenciales no son válidas. Inicia sesión de nuevo.",

            403 => !string.IsNullOrWhiteSpace(mensaje)
                ? mensaje!
                : "No tienes permisos para esta acción con tu perfil.",

            404 => !string.IsNullOrWhiteSpace(mensaje)
                ? mensaje!
                : "No se encontró lo solicitado (plaza, ticket o vehículo). Revisa los datos e intenta de nuevo.",

            409 => !string.IsNullOrWhiteSpace(mensaje)
                ? mensaje!
                : "No se pudo asignar: esa placa ya tiene un ticket abierto, o la plaza está ocupada. Ve a Tickets → Cerrar / cobrar e intenta de nuevo.",

            502 or 503 or 504 => !string.IsNullOrWhiteSpace(mensaje)
                ? mensaje!
                : "No se pudo completar la operación con IA. Puede que la placa ya tenga un ticket abierto o que un servicio no responda. Cierra tickets abiertos en la pestaña Tickets e intenta otra vez.",

            _ => !string.IsNullOrWhiteSpace(mensaje)
                ? mensaje!
                : $"Ocurrió un error inesperado (HTTP {status}). Intenta de nuevo."
        };
    }

    private static string? ExtraerMensaje(string? cuerpo)
    {
        if (string.IsNullOrWhiteSpace(cuerpo)) return null;
        if (cuerpo.Length > 400) return null;   // el original ignoraba cuerpos enormes

        try
        {
            using var documento = JsonDocument.Parse(cuerpo);
            foreach (var campo in new[] { "message", "detail", "error" })
            {
                if (documento.RootElement.TryGetProperty(campo, out var valor) &&
                    valor.ValueKind == JsonValueKind.String)
                {
                    var texto = valor.GetString();
                    if (!string.IsNullOrWhiteSpace(texto)) return texto;
                }
            }
        }
        catch (JsonException)
        {
            return cuerpo.Trim();
        }

        return null;
    }

    // ================================================================ autenticacion
    public static Task<SesionDto> LoginAsync(string usuario, string password, string? codigoTotp = null) =>
        PostAsync<SesionDto>("/api/auth/login", string.IsNullOrWhiteSpace(codigoTotp)
            ? new { username = usuario, password }
            : new { username = usuario, password, codigoTotp });

    /// <summary>¿Hay internet ahora mismo? (para el ingreso sin conexión).</summary>
    public static bool HayInternet => HayRed();

    /// <summary>Entrada desde un equipo ya vinculado: solo el código del autenticador.</summary>
    public static Task<SesionDto> LoginCodigoAsync(string dispositivo, string codigo) =>
        PostAsync<SesionDto>("/api/auth/login-codigo", new { dispositivo, codigo });

    /// <summary>Entrada de un toque desde este equipo (sin código), tras confirmar la huella.</summary>
    public static Task<SesionDto> LoginDispositivoAsync(string dispositivo) =>
        PostAsync<SesionDto>("/api/auth/login-dispositivo", new { dispositivo });

    /// <summary>¿Esa cuenta tiene el autenticador activo? (para mostrar el login corto).</summary>
    public static Task<JsonElement> TotpRequeridoAsync(string usuario) =>
        GetAsync<JsonElement>($"/api/auth/totp/requerido?username={Uri.EscapeDataString(usuario)}");

    /// <summary>Entrada desde un equipo ya vinculado: solo el código del autenticador.</summary>
    public static Task<TotpEstadoDto> TotpEstadoAsync() => GetAsync<TotpEstadoDto>("/api/auth/totp/estado");

    public static Task<TotpPrepararDto> TotpPrepararAsync() =>
        PostAsync<TotpPrepararDto>("/api/auth/totp/preparar");

    public static Task<TotpEstadoDto> TotpConfirmarAsync(string codigo) =>
        PostAsync<TotpEstadoDto>("/api/auth/totp/confirmar", new { codigo });

    public static Task<TotpEstadoDto> TotpDesactivarAsync(string codigo) =>
        PostAsync<TotpEstadoDto>("/api/auth/totp/desactivar", new { codigo });

    public static Task<UsuarioDto> RegistroAsync(string usuario, string email, string nombre, string apellido,
        string password, string rol) =>
        PostAsync<UsuarioDto>("/api/auth/register",
            new { username = usuario, email, firstName = nombre, lastName = apellido, password, role = rol });

    public static Task<SesionDto> LoginGoogleAsync(string idToken) =>
        PostAsync<SesionDto>("/api/auth/google", new { idToken });

    public static Task<SesionDto> LoginBiometriaAsync(string tokenHash) =>
        PostAsync<SesionDto>("/api/auth/biometria", new { tokenHash });

    public static Task<JsonElement> GoogleConfigAsync() => GetAsync<JsonElement>("/api/auth/google/config");

    // ================================================================ perfil
    public static Task<UsuarioDto> MiPerfilAsync() => GetAsync<UsuarioDto>("/api/perfil/me");

    public static Task<UsuarioDto> ActualizarPerfilAsync(string email, string nombre, string apellido) =>
        PutAsync<UsuarioDto>("/api/perfil/me", new { email, firstName = nombre, lastName = apellido });

    public static Task<JsonElement> CambiarPasswordAsync(string actual, string nueva) =>
        PutAsync<JsonElement>("/api/perfil/me/password", new { currentPassword = actual, newPassword = nueva });

    // ================================================================ admin de usuarios
    public static Task<List<UsuarioDto>> ListarUsuariosAsync() => GetAsync<List<UsuarioDto>>("/api/admin/users");

    public static Task<UsuarioDto> CrearUsuarioAsync(string usuario, string email, string nombre, string apellido,
        string password, string rol) =>
        PostAsync<UsuarioDto>("/api/admin/users",
            new { username = usuario, email, firstName = nombre, lastName = apellido, password, role = rol });

    public static Task<UsuarioDto> ActualizarUsuarioAsync(string id, string email, string nombre, string apellido, bool activo) =>
        PutAsync<UsuarioDto>($"/api/admin/users/{id}", new { email, firstName = nombre, lastName = apellido, enabled = activo });

    public static Task<UsuarioDto> ActualizarRolesAsync(string id, string rol) =>
        PutAsync<UsuarioDto>($"/api/admin/users/{id}/roles", new { role = rol });

    public static Task<JsonElement> ResetPasswordAsync(string id, string password) =>
        PutAsync<JsonElement>($"/api/admin/users/{id}/password", new { password });

    // ================================================================ sedes (parqueaderos)
    public static Task<List<SedeDto>> SedesAsync(double? lat = null, double? lng = null, string? tipo = null,
        string? preferencia = null)
    {
        var consulta = new List<string>();
        if (lat is not null && lng is not null)
        {
            consulta.Add($"lat={lat.Value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}");
            consulta.Add($"lng={lng.Value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}");
        }
        if (!string.IsNullOrWhiteSpace(tipo)) consulta.Add($"tipo={Uri.EscapeDataString(tipo)}");
        if (!string.IsNullOrWhiteSpace(preferencia)) consulta.Add($"preferencia={Uri.EscapeDataString(preferencia)}");
        var qs = consulta.Count > 0 ? "?" + string.Join("&", consulta) : "";
        return GetAsync<List<SedeDto>>($"/api/sedes{qs}");
    }

    public static Task<SedeDetalleDto> SedeAsync(long id, double? lat = null, double? lng = null)
    {
        var qs = lat is not null && lng is not null
            ? $"?lat={lat.Value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}" +
              $"&lng={lng.Value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}"
            : "";
        return GetAsync<SedeDetalleDto>($"/api/sedes/{id}{qs}");
    }

    /// <summary>La IA elige el parqueadero que más conviene según ubicación, plazas y precio.</summary>
    public static Task<RecomendacionSedeDto> RecomendarSedeAsync(string tipoVehiculo, double? lat, double? lng,
        string? preferencia, string? placa) =>
        PostAsync<RecomendacionSedeDto>("/api/sedes/recomendar",
            new { tipoVehiculo, latitud = lat, longitud = lng, preferencia, placa });

    public static Task<List<EspacioDto>> EspaciosDeSedeAsync(long sedeId) =>
        GetAsync<List<EspacioDto>>($"/api/espacios?sedeId={sedeId}");

    // ================================================================ pase del puesto (QR)
    /// <summary>
    /// Al elegir el puesto se abre el ticket y vuelve el pase con el QR y el valor.
    /// Sin fechas el cliente entra en el acto (modo AHORA); con fechas el puesto queda
    /// reservado para esa franja (modo RESERVA). En los dos casos se genera el QR.
    /// </summary>
    public static Task<PasePuestoDto> EstacionarAsync(string placa, long espacioId,
        DateTime? inicioReserva = null, DateTime? finReserva = null) =>
        PostAsync<PasePuestoDto>("/api/estacionar", inicioReserva is null || finReserva is null
            ? new { placa, espacioId, modo = "AHORA" }
            : new { placa, espacioId, modo = "RESERVA", inicioReserva, finReserva });

    public static Task<PasePuestoDto> PaseAsync(long ticketId) =>
        GetAsync<PasePuestoDto>($"/api/estacionar/{ticketId}");

    /// <summary>Ya llegó el cliente: la reserva se convierte en estancia activa.</summary>
    public static Task<PasePuestoDto> ActivarPaseAsync(long ticketId) =>
        PostAsync<PasePuestoDto>($"/api/estacionar/{ticketId}/activar");

    public static Task<JsonElement> CancelarPaseAsync(long ticketId) =>
        PostAsync<JsonElement>($"/api/estacionar/{ticketId}/cancelar");

    // ================================================================ espacios
    public static Task<List<EspacioDto>> ListarEspaciosAsync() => GetAsync<List<EspacioDto>>("/api/espacios");

    public static Task<List<EspacioDto>> DisponiblesAsync(string? tipo = null, string? zona = null)
    {
        var consulta = new List<string>();
        if (!string.IsNullOrWhiteSpace(tipo)) consulta.Add($"tipo={Uri.EscapeDataString(tipo)}");
        if (!string.IsNullOrWhiteSpace(zona)) consulta.Add($"zona={Uri.EscapeDataString(zona)}");
        var qs = consulta.Count > 0 ? "?" + string.Join("&", consulta) : "";
        return GetAsync<List<EspacioDto>>($"/api/espacios/disponibles{qs}");
    }

    public static Task<OcupacionDto> OcupacionAsync() => GetAsync<OcupacionDto>("/api/espacios/ocupacion");

    public static Task<EspacioDto> OcuparAsync(long id) => PostAsync<EspacioDto>($"/api/espacios/{id}/ocupar");

    public static Task<EspacioDto> LiberarAsync(long id) => PostAsync<EspacioDto>($"/api/espacios/{id}/liberar");

    public static Task<EspacioDto> CrearEspacioAsync(string codigo, string zona, string tipo, double tarifa,
        int? nivel, string? descripcion) =>
        PostAsync<EspacioDto>("/api/espacios",
            new { codigo, zona, tipoVehiculo = tipo, tarifaHora = tarifa, nivel, descripcion });

    public static Task<EspacioDto> ActualizarEspacioAsync(long id, string codigo, string zona, string tipo,
        double tarifa, int? nivel, string? descripcion) =>
        PutAsync<EspacioDto>($"/api/espacios/{id}",
            new { codigo, zona, tipoVehiculo = tipo, tarifaHora = tarifa, nivel, descripcion });

    // ================================================================ vehiculos
    public static Task<List<VehiculoDto>> ListarVehiculosAsync() => GetAsync<List<VehiculoDto>>("/api/vehiculos");

    public static Task<VehiculoDto> CrearVehiculoAsync(string placa, string tipo, string? marca, string? color,
        string propietario, string? email) =>
        PostAsync<VehiculoDto>("/api/vehiculos",
            new { placa, tipo, marca, color, propietario, propietarioEmail = email });

    public static Task<VehiculoDto> CambiarActivoVehiculoAsync(long id, bool activo) =>
        PatchAsync<VehiculoDto>($"/api/vehiculos/{id}/activo", new { activo });

    public static Task<VehiculoDto> ActualizarVehiculoAsync(long id, string tipo, string? marca, string? color,
        string propietario, string? email) =>
        PutAsync<VehiculoDto>($"/api/vehiculos/{id}",
            new { tipo, marca, color, propietario, propietarioEmail = email });

    public static Task<JsonElement> EliminarVehiculoAsync(long id) => DeleteAsync<JsonElement>($"/api/vehiculos/{id}");

    public static Task<List<BiometriaDto>> ListarBiometriasAsync() =>
        GetAsync<List<BiometriaDto>>("/api/biometrias");

    public static Task<BiometriaDto> GuardarBiometriaAsync(long? vehiculoId, string placa, string tipo,
        string tokenHash, string? fotoBase64, string dispositivo) =>
        PostAsync<BiometriaDto>("/api/biometrias", new
        {
            vehiculoId,
            placa,
            tipo,
            tokenHash,
            fotoBase64,
            dispositivo
        });

    public static Task<JsonElement> VerificarBiometriaAsync(string tokenHash) =>
        PostAsync<JsonElement>("/api/biometrias/verificar", new { tokenHash });

    // ================================================================ tickets
    public static Task<List<TicketDto>> ListarTicketsAsync() => GetAsync<List<TicketDto>>("/api/tickets");

    public static Task<List<TicketDto>> TicketsAbiertosAsync() => GetAsync<List<TicketDto>>("/api/tickets/abiertos");

    public static Task<TicketDto> AbrirTicketAsync(string placa, long espacioId) =>
        PostAsync<TicketDto>("/api/tickets/abrir", new { placa, espacioId });

    public static Task<TicketDto> CerrarTicketAsync(long id) => PostAsync<TicketDto>($"/api/tickets/{id}/cerrar");

    public static Task<SincronizacionDto> SincronizarLiberacionesAsync() =>
        PostAsync<SincronizacionDto>("/api/tickets/sincronizar-liberaciones");

    public static Task<ReconciliacionDto> ReconciliarAsync() =>
        PostAsync<ReconciliacionDto>("/api/tickets/reconciliar-plazas");

    public static Task<OcupacionRemotaDto> OcupacionRemotaAsync() =>
        GetAsync<OcupacionRemotaDto>("/api/tickets/ocupacion");

    public static Task<JsonElement> EstadoResilienciaAsync() => GetAsync<JsonElement>("/api/tickets/resilience/estado");

    // ================================================================ demo de caida (antes Docker)
    public static Task<DemoEstadoDto> DemoEstadoAsync() => GetAsync<DemoEstadoDto>("/api/tickets/demo/espacios/status");

    public static Task<DemoAccionDto> DemoDetenerAsync() => PostAsync<DemoAccionDto>("/api/tickets/demo/espacios/stop");

    public static Task<DemoAccionDto> DemoIniciarAsync() => PostAsync<DemoAccionDto>("/api/tickets/demo/espacios/start");

    // ================================================================ IA
    public static Task<RecomendacionDto> RecomendarAsync(string tipoVehiculo, string? preferencia, string? placa) =>
        PostAsync<RecomendacionDto>("/api/ia/recomendar", new { tipoVehiculo, preferencia, placa });

    public static Task<AsignacionDto> AsignarAsync(string placa, string tipoVehiculo, string? preferencia) =>
        PostAsync<AsignacionDto>("/api/ia/asignar", new { placa, tipoVehiculo, preferencia });

    public static Task<ReporteDto> ReporteAsync(string desde, string hasta) =>
        PostAsync<ReporteDto>("/api/ia/reporte-tickets", new { fechaDesde = desde, fechaHasta = hasta });

    public static Task<IaEstadoDto> EstadoIaAsync() => GetAsync<IaEstadoDto>("/api/ia/estado");

    public static Task<ChatRespuestaDto> ChatAsync(string mensaje, List<object> historial) =>
        PostAsync<ChatRespuestaDto>("/api/ia/chat", new { mensaje, historial });
}
