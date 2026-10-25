using System.Security.Cryptography;
using System.Text;
using UrbanPark.App.Modelos;

namespace UrbanPark.App.Servicios;

/// <summary>
/// Huella y rostro. La verificación en vivo la hace el sistema
/// (Windows Hello, huella o Face ID). En MySQL se guarda el tipo, el dispositivo,
/// un hash del token y, si eligió rostro, la foto.
/// </summary>
public static class BiometriaServicio
{
    public const string ClaveUltimoUsuario = "urbanpark-ultimo-usuario";
    public const string ClaveTokenDispositivo = "urbanpark-bio-dispositivo";

    private static string ClaveToken => ClaveTokenDe(Sesion.Username);

    public static string ClaveTokenDe(string usuario) => $"urbanpark-bio-{usuario}";

    public static string UltimoUsuario =>
        Preferences.Default.Get(ClaveUltimoUsuario, "");

    public static string Dispositivo =>
        $"{DeviceInfo.Current.Platform} {DeviceInfo.Current.Model}".Trim();

    public static async Task<(bool Ok, string Mensaje)> VerificarDispositivoAsync(string motivo)
    {
        try
        {
            return await VerificarPlataformaAsync(motivo);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static async Task<byte[]?> CapturarRostroAsync()
    {
        if (!MediaPicker.Default.IsCaptureSupported)
            return null;

        var foto = await MediaPicker.Default.CapturePhotoAsync();
        if (foto is null) return null;

        await using var origen = await foto.OpenReadAsync();
        using var memoria = new MemoryStream();
        await origen.CopyToAsync(memoria);
        var bytes = memoria.ToArray();
        return bytes.Length > 2_400_000 ? bytes[..2_400_000] : bytes;
    }

    /// <summary>
    /// Un solo token por celular: cada enrolamiento lo reutiliza para que las
    /// filas ya guardadas en MySQL (login y vehículos) sigan coincidiendo.
    /// </summary>
    public static async Task<string> CrearTokenAsync()
    {
        var token = await SecureStorage.Default.GetAsync(ClaveTokenDispositivo);
        if (string.IsNullOrWhiteSpace(token))
        {
            token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await SecureStorage.Default.SetAsync(ClaveTokenDispositivo, token);
        }
        if (!string.IsNullOrWhiteSpace(Sesion.Username))
        {
            await SecureStorage.Default.SetAsync(ClaveTokenDe(Sesion.Username), token);
            Preferences.Default.Set(ClaveUltimoUsuario, Sesion.Username);
        }
        return HashSolo(token);
    }

    public static async Task<bool> TieneHuellaLocalAsync(string? usuario = null)
    {
        var token = await TokenDispositivoAsync(usuario);
        return !string.IsNullOrWhiteSpace(token);
    }

    public static async Task<string?> HashLocalAsync() =>
        await HashLocalDeAsync(Sesion.Username);

    public static async Task<string?> HashLocalDeAsync(string? usuario = null)
    {
        var token = await TokenDispositivoAsync(usuario);
        return string.IsNullOrWhiteSpace(token) ? null : HashSolo(token);
    }

    public static async Task<string[]> HashesLocalesAsync(string? usuario = null)
    {
        var token = await TokenDispositivoAsync(usuario);
        if (string.IsNullOrWhiteSpace(token))
            return [];

        var hashes = new List<string> { HashSolo(token) };
        var quien = string.IsNullOrWhiteSpace(usuario) ? UltimoUsuario : usuario;
        if (!string.IsNullOrWhiteSpace(quien))
            hashes.Add(HashDe(token, quien));
        return hashes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task<string?> TokenDispositivoAsync(string? usuario)
    {
        var token = await SecureStorage.Default.GetAsync(ClaveTokenDispositivo);
        if (!string.IsNullOrWhiteSpace(token))
            return token;
        var quien = string.IsNullOrWhiteSpace(usuario) ? UltimoUsuario : usuario;
        if (string.IsNullOrWhiteSpace(quien))
            return null;
        return await SecureStorage.Default.GetAsync(ClaveTokenDe(quien));
    }

    public static async Task<bool> EnrolarAsync(Page pagina, long vehiculoId, string placa)
    {
        var (ok, mensaje) = await VerificarDispositivoAsync($"Confirma tu huella para {placa}");
        if (!ok)
        {
            await Ui.Error(pagina, mensaje);
            return false;
        }

        var hash = await CrearTokenAsync();
        await ApiCliente.GuardarBiometriaAsync(vehiculoId, placa, "HUELLA", hash, null, Dispositivo);
        await Ui.Exito(pagina, "Huella registrada. La pedirás para abrir el QR de salida.");
        return true;
    }

    /// <summary>Token nuevo. Al cambiar de dedo no se reutiliza el hash anterior.</summary>
    public static async Task<string> CrearTokenNuevoAsync()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await SecureStorage.Default.SetAsync(ClaveTokenDispositivo, token);
        if (!string.IsNullOrWhiteSpace(Sesion.Username))
        {
            await SecureStorage.Default.SetAsync(ClaveTokenDe(Sesion.Username), token);
            Preferences.Default.Set(ClaveUltimoUsuario, Sesion.Username);
        }
        return HashSolo(token);
    }

    /// <summary>Abre la pantalla del equipo donde se agregan o quitan huellas.</summary>
    public static Task AbrirAjustesHuellaAsync()
    {
#if WINDOWS
        return Launcher.Default.OpenAsync(new Uri("ms-settings:signinoptions"));
#elif ANDROID
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            var actividad = Platform.CurrentActivity;
            if (actividad is null) return;
            Android.Content.Intent intent;
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
            {
                intent = new Android.Content.Intent("android.settings.BIOMETRIC_ENROLL");
                intent.PutExtra("android.provider.extra.BIOMETRIC_AUTHENTICATORS_ALLOWED", 255);
            }
            else
            {
                intent = new Android.Content.Intent(Android.Provider.Settings.ActionSecuritySettings);
            }
            actividad.StartActivity(intent);
        });
#else
        return Task.CompletedTask;
#endif
    }

    public static Task<bool> EnrolarSesionAsync(Page pagina) =>
        EnrolarSesionAsync(pagina, "Huella vinculada. La próxima vez entra con ella, sin contraseña.");

    public static Task<bool> EnrolarSesionAsync(Page pagina, string mensajeExito) =>
        EnrolarSesionAsync(pagina, mensajeExito, false);

    public static async Task<bool> EnrolarSesionAsync(Page pagina, string mensajeExito, bool reemplazar)
    {
        var (ok, mensaje) = await VerificarDispositivoAsync(
            reemplazar
                ? "Confirma con el dedo nuevo, el que acabas de guardar en este equipo."
                : "Pon el dedo en el sensor. En el emulador: ⋯ → Fingerprint → Finger 1 → Touch the sensor.");
        if (!ok)
        {
            await Ui.Error(pagina, mensaje);
            return false;
        }

        var hash = reemplazar ? await CrearTokenNuevoAsync() : await CrearTokenAsync();
        await ApiCliente.GuardarBiometriaAsync(null, "LOGIN", "HUELLA", hash, null, Dispositivo);
        await Ui.Exito(pagina, mensajeExito);
        return true;
    }

    /// <summary>Hash local que la base de datos reconoce para el usuario en sesión, o null.</summary>
    public static async Task<string?> HashRegistradoAsync()
    {
        foreach (var hash in await HashesLocalesAsync(Sesion.Username))
        {
            try
            {
                await ApiCliente.VerificarBiometriaAsync(hash);
                return hash;
            }
            catch (Exception)
            {
            }
        }
        return null;
    }

    public static async Task<bool> DesbloquearQrAsync(Page pagina)
    {
        List<BiometriaDto> lista;
        try
        {
            lista = await ApiCliente.ListarBiometriasAsync() ?? [];
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(pagina, ex);
            return false;
        }

        if (lista.Count == 0)
        {
            var registrar = await pagina.DisplayAlertAsync("QR protegido",
                "Aún no tienes una huella en la base de datos. ¿La registras ahora para abrir el QR?",
                "Registrar huella", "Ahora no");
            if (!registrar) return false;
        }

        var (ok, mensaje) = await VerificarDispositivoAsync(
            "Verifica tu huella para ver el QR de salida");
        if (!ok)
        {
            await Ui.Error(pagina, mensaje);
            return false;
        }

        try
        {
            if (await HashRegistradoAsync() is not null)
                return true;

            // Sesión válida + huella del sistema confirmada: se vuelve a vincular este celular.
            var hash = await CrearTokenAsync();
            await ApiCliente.GuardarBiometriaAsync(null, "LOGIN", "HUELLA", hash, null, Dispositivo);
            await ApiCliente.VerificarBiometriaAsync(hash);
            return true;
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(pagina, ex);
            return false;
        }
    }

    public static string Hash(string token) => HashSolo(token);

    public static string HashSolo(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? "")));

    public static string HashDe(string token, string usuario)
    {
        var datos = Encoding.UTF8.GetBytes(token + "|" + (usuario ?? ""));
        return Convert.ToHexString(SHA256.HashData(datos));
    }

#if WINDOWS
    private static async Task<(bool Ok, string Mensaje)> VerificarPlataformaAsync(string motivo)
    {
        var disponible = await Windows.Security.Credentials.UI.UserConsentVerifier.CheckAvailabilityAsync();
        if (disponible != Windows.Security.Credentials.UI.UserConsentVerifierAvailability.Available)
        {
            return disponible switch
            {
                Windows.Security.Credentials.UI.UserConsentVerifierAvailability.DeviceNotPresent =>
                    (false, "Este equipo no tiene lector de huella ni cámara de Windows Hello."),
                Windows.Security.Credentials.UI.UserConsentVerifierAvailability.NotConfiguredForUser =>
                    (false, "Configura Windows Hello (huella o rostro) en Configuración > Cuentas > Opciones de inicio de sesión."),
                _ => (false, "Windows Hello no está disponible en este equipo.")
            };
        }

        var resultado = await MainThread.InvokeOnMainThreadAsync(async () =>
            await Windows.Security.Credentials.UI.UserConsentVerifier.RequestVerificationAsync(motivo).AsTask());

        return resultado == Windows.Security.Credentials.UI.UserConsentVerificationResult.Verified
            ? (true, "Huella o rostro reconocido")
            : (false, "No se reconoció la huella ni el rostro");
    }
#elif ANDROID
    private static bool EsEmuladorAndroid()
    {
        if (DeviceInfo.Current.DeviceType == DeviceType.Virtual)
            return true;
        var modelo = Android.OS.Build.Model ?? "";
        var huella = Android.OS.Build.Fingerprint ?? "";
        return modelo.Contains("sdk", StringComparison.OrdinalIgnoreCase)
            || huella.Contains("generic", StringComparison.OrdinalIgnoreCase)
            || huella.Contains("emulator", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(bool Ok, string Mensaje)> VerificarPlataformaAsync(string motivo)
    {
        if (Android.OS.Build.VERSION.SdkInt < Android.OS.BuildVersionCodes.P)
            return (false, "Este Android no soporta huella.");

        var actividad = Platform.CurrentActivity;
        if (actividad is null) return (false, "No hay una pantalla activa para pedir la huella.");

        var tcs = new TaskCompletionSource<(bool, string)>();
        var ejecutor = actividad.MainExecutor;
        if (ejecutor is null) return (false, "No se pudo abrir el lector de huella.");

        var descripcion = EsEmuladorAndroid()
            ? "No toques el icono. Abre ⋯ Extended controls → Fingerprint → Finger 1 → Touch the sensor."
            : "Pon el dedo en el sensor.";

        var callback = new HuellaCallback(tcs);
        var cancel = new Android.OS.CancellationSignal();
        var constructor = new Android.Hardware.Biometrics.BiometricPrompt.Builder(actividad)
            .SetTitle("Urban Park")
            .SetSubtitle(motivo)
            .SetDescription(descripcion)
            .SetNegativeButton("Cancelar", ejecutor, new CancelarHuella(tcs));

        // BIOMETRIC_WEAK (255) incluye la huella del emulador; STRONG a veces no la ve.
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
            constructor.SetAllowedAuthenticators(255);

        var prompt = constructor.Build();
        actividad.RunOnUiThread(() => prompt.Authenticate(cancel, ejecutor, callback));
        return await tcs.Task;
    }

    private sealed class HuellaCallback : Android.Hardware.Biometrics.BiometricPrompt.AuthenticationCallback
    {
        private readonly TaskCompletionSource<(bool, string)> _tcs;
        public HuellaCallback(TaskCompletionSource<(bool, string)> tcs) => _tcs = tcs;

        public override void OnAuthenticationSucceeded(Android.Hardware.Biometrics.BiometricPrompt.AuthenticationResult? result) =>
            _tcs.TrySetResult((true, "Huella reconocida"));

        public override void OnAuthenticationError(Android.Hardware.Biometrics.BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString)
        {
            var bruto = errString?.ToString() ?? errorCode.ToString();
            var texto = bruto.Contains("No fingerprints", StringComparison.OrdinalIgnoreCase)
                        || bruto.Contains("No biometrics", StringComparison.OrdinalIgnoreCase)
                        || bruto.Contains("no enrolled", StringComparison.OrdinalIgnoreCase)
                ? "Este celular no tiene una huella registrada. En el emulador: Extended controls (⋯) > Fingerprint > Finger 1, y en Ajustes > Seguridad registra la huella."
                : string.IsNullOrWhiteSpace(bruto) ? "No se reconoció la huella" : bruto;
            _tcs.TrySetResult((false, texto));
        }
    }

    private sealed class CancelarHuella : Java.Lang.Object, Android.Content.IDialogInterfaceOnClickListener
    {
        private readonly TaskCompletionSource<(bool, string)> _tcs;
        public CancelarHuella(TaskCompletionSource<(bool, string)> tcs) => _tcs = tcs;
        public void OnClick(Android.Content.IDialogInterface? dialog, int which) =>
            _tcs.TrySetResult((false, "Cancelado"));
    }
#else
    private static Task<(bool Ok, string Mensaje)> VerificarPlataformaAsync(string motivo) =>
        Task.FromResult((true, "Verificado"));
#endif
}
