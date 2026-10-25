using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// «Mi perfil» (todos los roles): datos personales, contraseña y huella.
/// Replica el formulario #tab-perfil del frontend original: el nombre de usuario
/// es informativo y no se puede modificar.
/// </summary>
public partial class vPerfil : ContentPage
{
    private bool _cargando;

    public vPerfil()
    {
        InitializeComponent();
        lblPie.Text = $"API: {ApiCliente.BaseUrl}";
        txtBaseUrl.Text = ApiCliente.BaseUrl;
        lblSugerenciasUrl.Text = "Emulador: http://10.0.2.2:8090 · Celular en la misma WiFi: http://192.168.100.38:8090";
        PintarIdentidad();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CargarPerfilAsync();
        await ActualizarTotpAsync();
    }

    // ================================================================ autenticador de Google
    private async Task ActualizarTotpAsync()
    {
        try
        {
            var estado = await ApiCliente.TotpEstadoAsync();
            lblTotp.Text = estado.Activo
                ? "Activado: al entrar tendrás que escribir el código de 6 dígitos que muestra Google Authenticator."
                : "Desactivado. Actívalo para entrar con el código de 6 dígitos de Google Authenticator.";
            btnTotp.Text = estado.Activo ? "Desactivar autenticador" : "Activar autenticador";
            btnTotp.Style = estado.Activo ? Ui.EstiloDe("BotonContornoMarca") : Ui.EstiloDe("BotonPrimario");
        }
        catch (Exception)
        {
            lblTotp.Text = "No se pudo consultar el estado del autenticador.";
        }
    }

    private async void OnTotpClicked(object? sender, EventArgs e)
    {
        btnTotp.IsEnabled = false;
        try
        {
            var estado = await ApiCliente.TotpEstadoAsync();
            if (estado.Activo)
            {
                var codigo = await DisplayPromptAsync("Desactivar autenticador",
                    "Escribe el código de 6 dígitos que muestra Google Authenticator ahora.",
                    "Desactivar", "Cancelar", "123456", 6, Keyboard.Numeric);

                if (string.IsNullOrWhiteSpace(codigo)) return;

                var respuesta = await ApiCliente.TotpDesactivarAsync(codigo.Trim());
                await Ui.Exito(this, respuesta.Mensaje);
            }
            else
            {
                await Navigation.PushAsync(new vTotp());
            }
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            btnTotp.IsEnabled = true;
            await ActualizarTotpAsync();
        }
    }

    // ================================================================ carga
    private async void OnRecargarClicked(object? sender, EventArgs e) => await CargarPerfilAsync();

    private async Task CargarPerfilAsync()
    {
        if (_cargando) return;

        _cargando = true;
        spinnerPerfil.IsVisible = spinnerPerfil.IsRunning = true;
        try
        {
            var perfil = await ApiCliente.MiPerfilAsync();

            if (perfil is null)
            {
                await Ui.Aviso(this, "El servidor no devolvió los datos del perfil.");
                return;
            }

            lblUsuario.Text = $"Usuario: {UsuarioMostrado(perfil)} · Roles: {RolesMostrados(perfil)}";
            txtNombre.Text = perfil.FirstName ?? "";
            txtApellido.Text = perfil.LastName ?? "";
            txtEmail.Text = perfil.Email ?? "";

            PintarIdentidad(NombreCompleto(perfil), UsuarioMostrado(perfil));
            await ActualizarHuellaAsync();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            spinnerPerfil.IsRunning = false;
            spinnerPerfil.IsVisible = false;
            _cargando = false;
        }
    }

    /// <summary>Avatar con iniciales y nombre de la sesión mientras no llegan los datos del API.</summary>
    private void PintarIdentidad(string? nombreCompleto = null, string? usuario = null)
    {
        var nombre = (nombreCompleto ?? "").Trim();
        if (nombre.Length == 0) nombre = Sesion.Username;

        var login = (usuario ?? Sesion.Username).Trim();

        lblNombre.Text = nombre.Length == 0 ? "—" : nombre;
        lblAvatar.Text = Iniciales(nombre.Length == 0 ? login : nombre);
    }

    private static string NombreCompleto(UsuarioDto perfil) => string.Join(" ",
        new[] { perfil.FirstName, perfil.LastName }.Where(p => !string.IsNullOrWhiteSpace(p))).Trim();

    /// <summary>Hasta dos iniciales del nombre (o del usuario si aún no hay nombre).</summary>
    private static string Iniciales(string texto)
    {
        var partes = texto.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length == 0) return "—";

        if (partes.Length == 1)
            return partes[0][..Math.Min(2, partes[0].Length)].ToUpperInvariant();

        return string.Concat(partes[0][..1], partes[^1][..1]).ToUpperInvariant();
    }

    /// <summary>Usuario del perfil; si el API no lo envía, el de la sesión.</summary>
    private static string UsuarioMostrado(UsuarioDto perfil)
    {
        var usuario = (perfil.Username ?? "").Trim();
        if (usuario.Length == 0) usuario = Sesion.Username;
        return usuario.Length == 0 ? "—" : usuario;
    }

    /// <summary>Roles unidos por ", " o "—" si no hay ninguno.</summary>
    private static string RolesMostrados(UsuarioDto perfil)
    {
        var roles = perfil.Roles is { Length: > 0 } ? perfil.Roles : Sesion.Roles;
        if (roles is null || roles.Length == 0) return "—";

        var texto = string.Join(", ", roles.Where(r => !string.IsNullOrWhiteSpace(r))).Trim();
        return texto.Length == 0 ? "—" : texto;
    }

    // ================================================================ formulario A
    private async void OnGuardarPerfilClicked(object? sender, EventArgs e)
    {
        var nombre = (txtNombre.Text ?? "").Trim();
        var apellido = (txtApellido.Text ?? "").Trim();
        var email = (txtEmail.Text ?? "").Trim();

        if (nombre.Length == 0 || apellido.Length == 0)
        {
            await Ui.Aviso(this, "El nombre y el apellido son obligatorios.");
            return;
        }

        if (!email.Contains('@') || email.Length < 5)
        {
            await Ui.Aviso(this, "Escribe un email válido.");
            return;
        }

        btnGuardarPerfil.IsEnabled = false;
        try
        {
            await ApiCliente.ActualizarPerfilAsync(email, nombre, apellido);
            await Ui.Exito(this, "Perfil actualizado");
            await CargarPerfilAsync();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            btnGuardarPerfil.IsEnabled = true;
        }
    }

    private async Task ActualizarHuellaAsync()
    {
        try
        {
            var reconocida = await BiometriaServicio.HashRegistradoAsync() is not null;
            if (!reconocida)
            {
                var lista = await ApiCliente.ListarBiometriasAsync() ?? [];
                reconocida = lista.Any(b => string.Equals(b.Tipo, "HUELLA", StringComparison.OrdinalIgnoreCase));
            }

            lblHuella.Text = reconocida
                ? "El lector solo acepta las huellas guardadas en este equipo. Para usar otro dedo, quita la anterior en la configuración y agrega la nueva."
                : "Aún no hay una huella tuya en la base de datos. Regístrala para entrar y abrir el QR.";
            btnHuella.Text = reconocida ? "Cambiar huella" : "Registrar huella";
        }
        catch (Exception)
        {
            lblHuella.Text = "No se pudo consultar la huella. Intenta de nuevo.";
        }
    }

    private async void OnHuellaClicked(object? sender, EventArgs e)
    {
        btnHuella.IsEnabled = false;
        try
        {
            var cambiar = btnHuella.Text.StartsWith("Cambiar", StringComparison.OrdinalIgnoreCase);
            if (cambiar)
            {
                var abrir = await DisplayAlertAsync("Cambiar huella",
                    "El lector no puede aprender un dedo nuevo desde la app. Abre la configuración, quita la huella anterior y agrega la que quieres usar. Después vuelve y confírmala.",
                    "Abrir configuración",
                    "Cancelar");
                if (!abrir) return;

                await BiometriaServicio.AbrirAjustesHuellaAsync();
                var listo = await DisplayAlertAsync("Cambiar huella",
                    "Cuando el dedo nuevo ya esté guardado en el equipo, confírmalo para dejarlo en Urban Park.",
                    "Confirmar huella",
                    "Ahora no");
                if (!listo) return;
            }

            var ok = await BiometriaServicio.EnrolarSesionAsync(this,
                "Huella actualizada. Con ella entras al sistema y abres el QR de salida.",
                cambiar);
            if (ok)
                await ActualizarHuellaAsync();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            btnHuella.IsEnabled = true;
        }
    }

    // ================================================================ formulario B
    // ================================================================ conexión con el API
    private async void OnAplicarUrlClicked(object? sender, EventArgs e)
    {
        var url = (txtBaseUrl.Text ?? "").Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            await Ui.Aviso(this, "La dirección debe empezar por http:// o https://");
            return;
        }

        ApiCliente.BaseUrl = url.TrimEnd('/');
        lblPie.Text = $"API: {ApiCliente.BaseUrl}";
        await Ui.Exito(this, "Dirección del API actualizada: " + ApiCliente.BaseUrl);
    }
    private async void OnCambiarPasswordClicked(object? sender, EventArgs e)
    {
        var actual = txtPasswordActual.Text ?? "";
        var nueva = txtPasswordNueva.Text ?? "";

        if (actual.Length == 0 || nueva.Length == 0)
        {
            await Ui.Aviso(this, "Escribe la contraseña actual y la nueva.");
            return;
        }

        if (nueva.Length < 6)
        {
            await Ui.Aviso(this, "La nueva contraseña debe tener al menos 6 caracteres.");
            return;
        }

        btnCambiarPassword.IsEnabled = false;
        try
        {
            // Si la contraseña actual es incorrecta, el API devuelve el mensaje y
            // Ui.MostrarExcepcion lo muestra tal cual (ApiExcepcion).
            await ApiCliente.CambiarPasswordAsync(actual, nueva);

            txtPasswordActual.Text = "";
            txtPasswordNueva.Text = "";

            await Ui.Exito(this, "Contraseña cambiada");
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            btnCambiarPassword.IsEnabled = true;
        }
    }
}
