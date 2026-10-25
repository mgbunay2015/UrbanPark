using System.Text.Json;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

public partial class vLogin : ContentPage
{
    public vLogin()
    {
        InitializeComponent();




        SizeChanged += (_, _) =>
        {
            cajaLogin.Padding = Width < 420 ? new Thickness(12, 14, 12, 20) : new Thickness(16, 20, 16, 28);
        };

        txtUsuario.TextChanged += async (_, _) => await ActualizarBotonHuellaAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        await ActualizarBotonHuellaAsync();
        await ConfigurarModoEntradaAsync();

        // Al volver del autenticador (app reanudada) se intenta pegar el código copiado.
        EngancharVentana();
        await PegarSiHayCodigoNuevoAsync(enviar: false);   // al abrir solo se rellena; no se envía
    }

    private Window? _ventanaEnlazada;

    /// <summary>
    /// Escucha la reanudación de la ventana: en Android/iOS es "Resumed", en Windows
    /// "Activated". Así, al volver de Google Authenticator con el código copiado, se pega solo.
    /// </summary>
    private void EngancharVentana()
    {
        var ventana = Window;
        if (ventana is null || ReferenceEquals(_ventanaEnlazada, ventana)) return;

        DesengancharVentana();
        _ventanaEnlazada = ventana;
        _ventanaEnlazada.Resumed += OnVentanaReanudada;
        _ventanaEnlazada.Activated += OnVentanaReanudada;
    }

    private void DesengancharVentana()
    {
        if (_ventanaEnlazada is null) return;
        _ventanaEnlazada.Resumed -= OnVentanaReanudada;
        _ventanaEnlazada.Activated -= OnVentanaReanudada;
        _ventanaEnlazada = null;
    }

    private void OnVentanaReanudada(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(async () =>
        {
            // La página pudo cerrarse (por ejemplo al salir de la sesión): se ignora.
            try
            {
                await Task.Delay(250);      // deja que el portapapeles se actualice
                await PegarSiHayCodigoNuevoAsync();
            }
            catch (Exception)
            {
                // sin la pantalla del login no hay nada que pegar
            }
        });

    protected override void OnDisappearing()
    {
        _visible = false;
        DesengancharVentana();
        base.OnDisappearing();
    }

    /// <summary>
    /// Si este equipo ya está vinculado al autenticador de Google, se muestra solo el campo
    /// del código (el usuario ya se conoce). Si no, el login normal de usuario y contraseña.
    /// </summary>
    private async Task ConfigurarModoEntradaAsync()
    {
        var recordado = CredencialLocal.Usuario;
        if (recordado.Length == 0)
        {
            MostrarModoCredenciales();
            return;
        }

        if (txtUsuario.Text is null or "")
            txtUsuario.Text = recordado;

        // Se pregunta al API si la cuenta sigue con el autenticador activo (si no hay red,
        // se usa lo último que se guardó en el equipo).
        try
        {
            if (ApiCliente.HayInternet)
            {
                var estado = await ApiCliente.TotpRequeridoAsync(recordado);
                if (estado.TryGetProperty("activo", out var activo) && activo.ValueKind == JsonValueKind.True)
                    CredencialLocal.ActualizarTotp(true);
                else if (activo.ValueKind == JsonValueKind.False)
                    CredencialLocal.ActualizarTotp(false);
            }
        }
        catch (Exception)
        {
            // sin conexión: se mantiene lo guardado
        }

        if (CredencialLocal.PuedeEntrarConCodigo)
            MostrarModoCodigo(recordado);
        else
            MostrarModoCredenciales();
    }

    /// <summary>
    /// Pantalla corta: solo dos formas de entrar (huella o código del autenticador).
    /// Se ocultan el resto de opciones (Google, crear cuenta, perfiles demo, contraseña).
    /// </summary>
    private void MostrarModoCodigo(string usuario)
    {
        panelCodigo.IsVisible = true;
        panelEscribirCodigo.IsVisible = false;
        txtCodigo.Text = "";

        panelCredenciales.IsVisible = false;
        panelTotp.IsVisible = false;
        btnHuella.IsVisible = false;
        lblHuellaAyuda.IsVisible = false;
        panelOtrasOpciones.IsVisible = false;
        btnHuella.IsVisible = false;
        lblHuellaAyuda.IsVisible = false;

        lblSubLogin.IsVisible = false;
        lblCodigoSaludo.Text = $"Hola, {usuario}";
        lblCodigoAyuda.Text = "¿Cómo quieres entrar?";
    }

    private void MostrarModoCredenciales()
    {
        panelCodigo.IsVisible = false;
        lblSubLogin.IsVisible = true;
        panelCredenciales.IsVisible = true;
        btnHuella.IsVisible = true;
        lblHuellaAyuda.IsVisible = true;
        panelOtrasOpciones.IsVisible = true;
    }

    /// <summary>«Ingreso con código»: muestra el campo de 6 dígitos.</summary>
    private void OnIngresoCodigoClicked(object? sender, EventArgs e)
    {
        panelEscribirCodigo.IsVisible = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), () => txtCodigo.Focus());
    }

    /// <summary>«Ingreso con huella»: confirma el dueño y abre la sesión sin código.</summary>
    private async void OnIngresoHuellaClicked(object? sender, EventArgs e) => await EntrarSinCodigoAsync();

    private void OnUsarCredencialesTapped(object? sender, TappedEventArgs e) => MostrarModoCredenciales();

    /// <summary>«Otro usuario»: vuelve al login completo para entrar con otra cuenta.</summary>
    private void OnOtroUsuarioTapped(object? sender, TappedEventArgs e)
    {
        MostrarModoCredenciales();
        txtUsuario.Text = "";
        txtPassword.Text = "";
        txtTotp.Text = "";
        panelTotp.IsVisible = false;
        btnEntrar.Text = "Iniciar sesión";
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), () => txtUsuario.Focus());
    }

    /// <summary>
    /// Entrada de un toque: el equipo ya está vinculado, así que no se pide el código.
    /// Antes se confirma con la huella/rostro del dueño del teléfono.
    /// </summary>
    private async Task EntrarSinCodigoAsync()
    {
        if (CredencialLocal.Dispositivo.Length < 32)
        {
            MostrarModoCredenciales();
            await Ui.Aviso(this, "Este equipo no está vinculado. Entra con usuario y contraseña.");
            return;
        }

        var confirmado = await ConfirmarDuenoAsync();
        if (!confirmado) return;

        btnIngresoHuella.IsEnabled = false;
        spinnerLogin.IsVisible = spinnerLogin.IsRunning = true;
        try
        {
            var sesion = await ApiCliente.LoginDispositivoAsync(CredencialLocal.Dispositivo);
            await Sesion.GuardarAsync(sesion);
            CredencialLocal.GuardarSesionOffline(sesion);
            IrAlMenu();
        }
        catch (ApiExcepcion ex) when (ex.Status == 401)
        {
            CredencialLocal.OlvidarDispositivo();
            MostrarModoCredenciales();
            await Ui.Aviso(this, ex.Message);
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            btnIngresoHuella.IsEnabled = true;
            spinnerLogin.IsRunning = false;
            spinnerLogin.IsVisible = false;
        }
    }

    /// <summary>Huella/rostro si el equipo la tiene; si no, una confirmación normal.</summary>
    private async Task<bool> ConfirmarDuenoAsync()
    {
        try
        {
            var (ok, mensaje) = await BiometriaServicio.VerificarDispositivoAsync(
                "Confirma tu huella para entrar sin escribir el código.");
            if (ok) return true;

            // Si no hay huella disponible se pide una confirmación simple.
            if (mensaje.Contains("no", StringComparison.OrdinalIgnoreCase) ||
                mensaje.Contains("no disponible", StringComparison.OrdinalIgnoreCase) ||
                mensaje.Contains("no registrad", StringComparison.OrdinalIgnoreCase))
                return await DisplayAlert("Entrar sin código",
                    $"¿Entrar como {CredencialLocal.Usuario} en este equipo?", "Entrar", "Cancelar");

            await Ui.Error(this, mensaje);
            return false;
        }
        catch (Exception)
        {
            return await DisplayAlert("Entrar sin código",
                $"¿Entrar como {CredencialLocal.Usuario} en este equipo?", "Entrar", "Cancelar");
        }
    }
    // ================================================================ pegar el código del autenticador
    private string _ultimoCodigoIntentado = "";

    /// <summary>Abre Google Authenticator para copiar el código de 6 dígitos.</summary>
    private async void OnAbrirAutenticadorClicked(object? sender, EventArgs e)
    {
        try
        {
            // Android/iOS abren la app del autenticador con su propio esquema.
            if (await Launcher.Default.TryOpenAsync("googleauthenticator://"))
                return;

            if (await Launcher.Default.TryOpenAsync("otpauth://totp/Urban%20Park"))
                return;
        }
        catch (Exception)
        {
            // si no se puede abrir, se explica el paso manual
        }

        await Ui.Informar(this,
            "Abre Google Authenticator, mantén pulsado el código de Urban Park para copiarlo y vuelve aquí: " +
            "al regresar se pega solo.");
    }

    /// <summary>Toma el código del portapapeles y entra con él.</summary>
    private async void OnPegarCodigoClicked(object? sender, EventArgs e)
    {
        var codigo = ExtraerCodigo(await LeerPortapapelesAsync());
        if (codigo is null)
        {
            await Ui.Informar(this,
                "No hay un código de 6 dígitos en el portapapeles. En Google Authenticator mantén pulsado el código " +
                "para copiarlo y toca de nuevo «Pegar código».");
            return;
        }

        if (panelCodigo.IsVisible)
        {
            txtCodigo.Text = codigo;
            OnEntrarConCodigoClicked(sender, e);
            return;
        }

        if (panelTotp.IsVisible)
        {
            txtTotp.Text = codigo;
            OnEntrarClicked(sender, e);
            return;
        }

        await Ui.Informar(this, "Primero escribe el usuario y la contraseña; el código se pega en el último paso.");
    }

    /// <summary>Lee el texto del portapapeles (vacío si no se puede).</summary>
    private static async Task<string> LeerPortapapelesAsync()
    {
        try
        {
            return await Clipboard.Default.GetTextAsync() ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>Toma el código de 6 dígitos del texto (Google Authenticator copia solo el número).</summary>
    private static string? ExtraerCodigo(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;

        var digitos = new string(texto.Where(char.IsDigit).ToArray());
        return digitos.Length >= 6 ? digitos[^6..] : null;
    }

    /// <summary>Último texto visto en el portapapeles: solo se reacciona cuando cambia.</summary>
    private string _ultimoPortapapeles = "";

    /// <summary>
    /// Al volver del autenticador se pega el código y se entra solo (si el portapapeles cambió).
    /// Con <paramref name="enviar"/> = false solo rellena el campo (al abrir la app).
    /// </summary>
    private async Task PegarSiHayCodigoNuevoAsync(bool enviar = true)
    {
        if (!_visible || (!panelCodigo.IsVisible && !panelTotp.IsVisible)) return;
        if (_pegandoAutomatico) return;
        if (Window is null || !IsLoaded) return;   // la pantalla ya no está en uso

        var texto = await LeerPortapapelesAsync();
        var cambio = texto != _ultimoPortapapeles;
        _ultimoPortapapeles = texto;

        var codigo = ExtraerCodigo(texto);
        if (codigo is null) return;

        if (!enviar)
        {
            if (panelEscribirCodigo.IsVisible && string.IsNullOrEmpty(txtCodigo.Text)) txtCodigo.Text = codigo;
            else if (panelTotp.IsVisible && string.IsNullOrEmpty(txtTotp.Text)) txtTotp.Text = codigo;
            return;
        }

        if (!cambio || codigo == _ultimoCodigoIntentado) return;

        _pegandoAutomatico = true;
        try
        {
            _ultimoCodigoIntentado = codigo;
            if (panelCodigo.IsVisible)
            {
                panelEscribirCodigo.IsVisible = true;
                txtCodigo.Text = codigo;
                OnEntrarConCodigoClicked(this, EventArgs.Empty);
            }
            else
            {
                txtTotp.Text = codigo;
                OnEntrarClicked(this, EventArgs.Empty);
            }
        }
        finally
        {
            _pegandoAutomatico = false;
        }
    }
    private bool _pegandoAutomatico;
    private bool _visible;

    /// <summary>Login corto: solo el código del autenticador, desde un equipo ya vinculado.</summary>
    private async void OnEntrarConCodigoClicked(object? sender, EventArgs e)
    {
        var codigo = (txtCodigo.Text ?? "").Trim();
        if (codigo.Length != 6)
        {
            await Ui.Aviso(this, "Escribe el código de 6 dígitos que muestra Google Authenticator.");
            txtCodigo.Focus();
            return;
        }

        spinnerLogin.IsVisible = spinnerLogin.IsRunning = true;
        try
        {
            var sesion = await ApiCliente.LoginCodigoAsync(CredencialLocal.Dispositivo, codigo);
            await Sesion.GuardarAsync(sesion);
            await CredencialLocal.GuardarAsync(sesion.Username, null, true, sesion.DispositivoToken);
            IrAlMenu();
        }
        catch (ApiExcepcion ex) when (ex.Status == 401)
        {
            _ultimoCodigoIntentado = "";
            txtCodigo.Text = "";

            // Si el equipo ya no está vinculado se vuelve al login completo;
            // si solo fue el código, se puede reintentar.
            if (ex.Message.Contains("equipo", StringComparison.OrdinalIgnoreCase))
            {
                CredencialLocal.OlvidarDispositivo();
                CredencialLocal.ActualizarTotp(true);
                MostrarModoCredenciales();
                await Ui.Aviso(this, "Este equipo ya no está vinculado. Entra con usuario y contraseña.");
                return;
            }

            await Ui.Aviso(this, ex.Message);
            txtCodigo.Focus();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            spinnerLogin.IsRunning = false;
            spinnerLogin.IsVisible = false;
        }
    }

    private async Task ActualizarBotonHuellaAsync()
    {
        btnHuella.IsVisible = true;
        btnHuella.Text = "Entrar con huella";
        lblHuellaAyuda.Text = "Toca Entrar con huella: te pedirá el sensor y luego buscará tu usuario en la base de datos.";
        await Task.CompletedTask;
    }

    // ================================================================ login
    private async void OnEntrarClicked(object? sender, EventArgs e)
    {
        var usuario = (txtUsuario.Text ?? "").Trim();
        var password = txtPassword.Text ?? "";
        var codigo = (txtTotp.Text ?? "").Trim();

        if (usuario.Length == 0 || password.Length == 0)
        {
            await Ui.Aviso(this, "Escribe el usuario y la contraseña.");
            return;
        }

        if (panelTotp.IsVisible && codigo.Length != 6)
        {
            await Ui.Aviso(this, "Escribe el código de 6 dígitos que muestra Google Authenticator.");
            txtTotp.Focus();
            return;
        }

        // Sin internet se entra con la contraseña guardada en el equipo.
        if (!ApiCliente.HayInternet)
        {
            await EntrarSinInternetAsync(usuario, password);
            return;
        }

        spinnerLogin.IsVisible = spinnerLogin.IsRunning = true;
        try
        {
            var sesion = await ApiCliente.LoginAsync(usuario, password, panelTotp.IsVisible ? codigo : null);
            await Sesion.GuardarAsync(sesion);

            // Se guarda la credencial para poder entrar sin internet y, si la cuenta usa
            // autenticador, el token del equipo para entrar después solo con el código.
            var usaTotp = sesion.Usuario?.TotpActivo == true || panelTotp.IsVisible;
            await CredencialLocal.GuardarAsync(sesion.Username, password, usaTotp, sesion.DispositivoToken);
            CredencialLocal.GuardarSesionOffline(sesion);

            spinnerLogin.IsRunning = false;
            spinnerLogin.IsVisible = false;
            await OfrecerHuellaAsync();
            IrAlMenu();
        }
        catch (ApiExcepcion ex) when (ex.RequiereTotp)
        {
            // La cuenta tiene el autenticador de Google activo: se pide el código.
            MostrarCampoTotp(ex.Message);
        }
        catch (ApiExcepcion ex) when (ex.Status == 0)
        {
            // No se pudo hablar con el servidor: se intenta el ingreso sin internet.
            spinnerLogin.IsRunning = false;
            spinnerLogin.IsVisible = false;
            await EntrarSinInternetAsync(usuario, password, ex.Message);
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            spinnerLogin.IsRunning = false;
            spinnerLogin.IsVisible = false;
        }
    }

    /// <summary>
    /// Ingreso sin internet: comprueba usuario y contraseña contra el hash guardado en el
    /// equipo y abre la app con la sesión y los datos que ya están descargados.
    /// </summary>
    /// <summary>
    /// Ingreso sin conexión: comprueba usuario y contraseña contra el hash guardado en el
    /// equipo y abre la app con la sesión y los datos ya descargados.
    /// Se distingue "no hay internet" de "el API no responde": en el segundo caso el
    /// usuario decide, para no entrar a ciegas cuando el servidor está apagado.
    /// </summary>
    private async Task EntrarSinInternetAsync(string usuario, string password, string? detalle = null)
    {
        var hayRed = ApiCliente.HayInternet;
        var problema = hayRed
            ? $"No se pudo conectar con el API en {ApiCliente.BaseUrl}."
            : "Sin internet.";

        if (!await CredencialLocal.TieneCredencialAsync())
        {
            await Ui.Error(this,
                $"{problema} Este equipo todavía no tiene una contraseña guardada: conéctate una vez para entrar " +
                "y desde ahí también podrás hacerlo sin conexión." + (detalle is null ? "" : $"\n\n{detalle}"));
            return;
        }

        if (!await CredencialLocal.VerificarAsync(usuario, password))
        {
            await Ui.Error(this, $"{problema} La contraseña no coincide con la guardada en este equipo.");
            return;
        }

        // Con red pero sin servidor, se pregunta antes de abrir la app en modo guardado.
        if (hayRed)
        {
            var entrar = await DisplayAlert("No se pudo conectar con el API",
                $"{problema}\n\n¿Quieres entrar con los datos guardados en este equipo? " +
                "Lo que hagas se enviará cuando el servidor vuelva a responder.",
                "Entrar sin conexión", "Reintentar");
            if (!entrar) return;
        }

        if (Sesion.Actual is null)
            await Sesion.CargarAsync();

        // Sin sesión guardada se reconstruye la última con un token de relleno: la app
        // funciona con los datos que ya tiene descargados y sincroniza al volver la red.
        if (!Sesion.Activa)
        {
            var guardada = CredencialLocal.SesionOffline();
            if (guardada is null)
            {
                await Ui.Informar(this,
                    $"{problema} Y no hay datos guardados en este equipo. Conéctate una vez para poder entrar después.");
                return;
            }

            await Sesion.GuardarAsync(guardada);
        }

        await Ui.Informar(this, hayRed
            ? $"Entraste con los datos guardados de {usuario}: el API no responde en este momento. " +
              "Lo que hagas se enviará cuando vuelva."
            : $"Entraste sin internet como {usuario}. Verás los datos guardados y lo que hagas se enviará al volver la conexión.");

        IrAlMenu();
    }
    /// <summary>Muestra el campo del código TOTP y deja el cursor listo para escribirlo.</summary>
    private void MostrarCampoTotp(string? mensaje)
    {
        panelTotp.IsVisible = true;
        lblTotpAyuda.Text = string.IsNullOrWhiteSpace(mensaje)
            ? "Abre Google Authenticator y escribe el código de 6 dígitos de Urban Park."
            : mensaje!;
        btnEntrar.Text = "Entrar con código";
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () => txtTotp.Focus());
    }

    // ================================================================ huella / rostro
    private async void OnHuellaClicked(object? sender, EventArgs e)
    {
        try
        {
            var (ok, mensaje) = await BiometriaServicio.VerificarDispositivoAsync(
                "Confirma tu huella. Se buscará en la base de datos a qué usuario pertenece.");
            if (!ok)
            {
                await Ui.Error(this, mensaje);
                return;
            }

            spinnerLogin.IsVisible = spinnerLogin.IsRunning = true;
            var hashes = await BiometriaServicio.HashesLocalesAsync();
            if (hashes.Length == 0)
            {
                await Ui.Aviso(this,
                    "Tu huella se reconoció, pero este celular aún no está vinculado. Entra con usuario y contraseña y regístrala. Quedará en la base de datos para el próximo ingreso.");
                return;
            }

            Exception? ultimoError = null;
            foreach (var hash in hashes)
            {
                try
                {
                    var sesion = await ApiCliente.LoginBiometriaAsync(hash);
                    await Sesion.GuardarAsync(sesion);
                    IrAlMenu();
                    return;
                }
                catch (Exception ex)
                {
                    ultimoError = ex;
                }
            }

            if (ultimoError is not null)
                await Ui.MostrarExcepcion(this, ultimoError);
            else
                await Ui.Aviso(this, "Esa huella no está en la base de datos. Entra con usuario y contraseña y regístrala.");
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            spinnerLogin.IsRunning = false;
            spinnerLogin.IsVisible = false;
        }
    }

    private async Task OfrecerHuellaAsync()
    {
        if (await BiometriaServicio.HashRegistradoAsync() is not null)
            return;

        var quiere = await DisplayAlertAsync(
            "Huella",
            "¿Quieres entrar la próxima vez con tu huella, sin contraseña? Cuando salga el lector, en el emulador pulsa Finger 1 y Touch the sensor.",
            "Sí, registrar",
            "Ahora no");
        if (quiere)
            await BiometriaServicio.EnrolarSesionAsync(this);
    }


    // ================================================================ paneles
    private void OnVerPerfilesClicked(object? sender, EventArgs e)
    {
        panelPerfiles.IsVisible = !panelPerfiles.IsVisible;
        btnVerPerfiles.Text = panelPerfiles.IsVisible
            ? "Ocultar perfiles de demostración"
            : "Ver perfiles de demostración";
    }

    private void OnMostrarRegistroClicked(object? sender, EventArgs e)
    {
        panelLogin.IsVisible = false;
        panelPerfiles.IsVisible = false;
        panelRegistro.IsVisible = true;
    }

    private void OnCancelarRegistroClicked(object? sender, EventArgs e)
    {
        panelRegistro.IsVisible = false;
        panelLogin.IsVisible = true;
    }

    // ================================================================ registro
    private async void OnRegistrarClicked(object? sender, EventArgs e)
    {
        var usuario = (txtRegUsuario.Text ?? "").Trim();
        var email = (txtRegEmail.Text ?? "").Trim();
        var nombre = (txtRegNombre.Text ?? "").Trim();
        var apellido = (txtRegApellido.Text ?? "").Trim();
        var password = txtRegPassword.Text ?? "";
        var rol = pckRegRol.SelectedItem?.ToString() ?? "CLIENTE";

        if (usuario.Length < 4)
        {
            await Ui.Aviso(this, "El usuario debe tener al menos 4 caracteres.");
            return;
        }
        if (!email.Contains('@'))
        {
            await Ui.Aviso(this, "Escribe un email válido.");
            return;
        }
        if (nombre.Length == 0 || apellido.Length == 0)
        {
            await Ui.Aviso(this, "El nombre y el apellido son obligatorios.");
            return;
        }
        if (password.Length < 6)
        {
            await Ui.Aviso(this, "La contraseña debe tener al menos 6 caracteres.");
            return;
        }

        try
        {
            await ApiCliente.RegistroAsync(usuario, email, nombre, apellido, password, rol);
            var sesion = await ApiCliente.LoginAsync(usuario, password);
            await Sesion.GuardarAsync(sesion);

            var enrolada = await BiometriaServicio.EnrolarSesionAsync(this,
                "Huella registrada. Con ella entras al sistema y abres el QR de salida.");
            if (!enrolada)
            {
                Sesion.Cerrar();
                txtUsuario.Text = usuario;
                txtPassword.Text = password;
                panelRegistro.IsVisible = false;
                panelLogin.IsVisible = true;
                await Ui.Aviso(this,
                    "La cuenta se creó, pero la huella no quedó en la base de datos. Sin ella no puedes entrar ni abrir el QR. Inicia sesión y regístrala cuando te lo pida.");
                return;
            }

            txtRegUsuario.Text = txtRegEmail.Text = txtRegNombre.Text = txtRegApellido.Text = txtRegPassword.Text = "";
            panelRegistro.IsVisible = false;
            panelLogin.IsVisible = true;
            IrAlMenu();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
    }

    // ================================================================ perfiles demo
    private void OnUsarCliente1(object? sender, EventArgs e) => Rellenar("cliente1", "cliente123");
    private void OnUsarCliente2(object? sender, EventArgs e) => Rellenar("cliente2", "cliente123");
    private void OnUsarCliente3(object? sender, EventArgs e) => Rellenar("cliente3", "cliente123");
    private void OnUsarOperador(object? sender, EventArgs e) => Rellenar("operador1", "operador123");
    private void OnUsarAdmin(object? sender, EventArgs e) => Rellenar("admin1", "admin123");

    private void Rellenar(string usuario, string password)
    {
        txtUsuario.Text = usuario;
        txtPassword.Text = password;
    }

    // ================================================================ URL del API

    // ================================================================ navegacion
    private void IrAlMenu()
    {
        if (Application.Current?.Windows.Count > 0)
            Application.Current.Windows[0].Page = new vPrincipal();
    }
}
