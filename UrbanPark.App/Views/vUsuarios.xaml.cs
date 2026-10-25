using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Usuarios (solo ADMIN): mantenimiento de usuarios contra /api/admin/users.
/// La tabla y las acciones por fila se generan en el code-behind (sin bindings),
/// igual que el resto de pantallas de la app.
/// </summary>
public partial class vUsuarios : ContentPage
{
    private const string SinUsuarios = "Sin usuarios";
    private const string Cargando = "Cargando…";

    private static readonly string[] Roles = ["CLIENTE", "OPERADOR", "ADMIN"];

    // 6 columnas: Usuario | Nombre | Email | Roles | Estado | Acciones
    private static readonly ColumnDefinitionCollection Columnas =
    [
        new ColumnDefinition(new GridLength(0.9, GridUnitType.Star)),
        new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)),
        new ColumnDefinition(new GridLength(1.3, GridUnitType.Star)),
        new ColumnDefinition(new GridLength(0.9, GridUnitType.Star)),
        new ColumnDefinition(new GridLength(0.7, GridUnitType.Star)),
        new ColumnDefinition(new GridLength(1.6, GridUnitType.Star))
    ];

    private readonly List<UsuarioDto> _usuarios = [];

    private UsuarioDto? _enEdicion;
    private UsuarioDto? _enPassword;

    public vUsuarios()
    {
        InitializeComponent();

        pckNuevoRol.SelectedIndex = 0;
        pckEditarRol.SelectedIndex = 0;

        // Acento visual de los paneles destacados (color del sistema de diseño, no en linea).
        acentoEditar.Color = Ui.ColorDe("Acento");
        acentoPassword.Color = Ui.ColorDe("Primario");

        PintarTabla(Cargando);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (Sesion.EsAdmin) _ = CargarAsync();
    }

    // ================================================================ guardia
    private async Task<bool> SinPermiso()
    {
        if (Sesion.EsAdmin) return false;

        await Ui.Error(this, "Solo ADMIN puede administrar usuarios");
        return true;
    }

    // ================================================================ carga
    private async void OnActualizarClicked(object? sender, EventArgs e)
    {
        if (await SinPermiso()) return;
        await CargarAsync();
    }

    private async Task CargarAsync()
    {
        if (!Sesion.EsAdmin) return;

        PintarTabla(Cargando);

        try
        {
            var lista = await ApiCliente.ListarUsuariosAsync();

            _usuarios.Clear();
            if (lista is not null) _usuarios.AddRange(lista.Where(u => u is not null));

            PintarUsuarios();
        }
        catch (Exception ex)
        {
            _usuarios.Clear();
            PintarTabla("No se pudieron cargar los usuarios.");
            await Ui.MostrarExcepcion(this, ex);
        }
    }

    private void PintarUsuarios()
    {
        if (_usuarios.Count == 0)
        {
            PintarTabla(SinUsuarios);
            return;
        }

        contTabla.Clear();
        contTabla.Add(TarjetaCabecera());

        for (var i = 0; i < _usuarios.Count; i++)
            contTabla.Add(TarjetaUsuario(_usuarios[i], i + 1));
    }

    private void PintarTabla(string mensaje)
    {
        contTabla.Clear();
        contTabla.Add(TarjetaCabecera());

        var celda = new Label
        {
            Text = mensaje,
            Style = Ui.EstiloDe("EstadoVacio"),
            HorizontalOptions = LayoutOptions.Fill
        };
        Grid.SetColumnSpan(celda, 6);

        var fila = NuevaFila();
        fila.Add(celda);
        contTabla.Add(fila);
    }

    // ================================================================ tabla
    private static Grid NuevaFila()
    {
        var fila = new Grid { ColumnDefinitions = Columnas };
        fila.Padding = new Thickness(0, 2);
        return fila;
    }

    /// <summary>Cabecera de la tabla: encabezados claros sobre una tarjeta.</summary>
    private static Border TarjetaCabecera()
    {
        var fila = NuevaFila();
        var titulos = new[] { "Usuario", "Nombre", "Email", "Roles", "Estado", "Acciones" };

        for (var i = 0; i < titulos.Length; i++)
        {
            var celda = new Label
            {
                Text = titulos[i],
                Style = Ui.EstiloDe("EncabezadoTablaClaro")
            };
            Grid.SetColumn(celda, i);
            fila.Add(celda);
        }

        return new Border
        {
            Style = Ui.EstiloDe("Tarjeta"),
            Padding = new Thickness(8, 6),
            Content = fila
        };
    }

    /// <summary>Una fila de usuario: tarjeta blanca con datos, badge de estado y acciones mini.</summary>
    private Border TarjetaUsuario(UsuarioDto usuario, int indice)
    {
        var fila = NuevaFila();

        var celdaUsuario = new Label
        {
            Text = Texto(usuario.Username),
            Style = Ui.EstiloDe("DatoFilaFuerte"),
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(celdaUsuario, 0);
        fila.Add(celdaUsuario);

        var nombreCompleto = string.Join(" ",
            new[] { usuario.FirstName, usuario.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));

        var celdaNombre = new Label
        {
            Text = Texto(nombreCompleto),
            Style = Ui.EstiloDe("DatoFila"),
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(celdaNombre, 1);
        fila.Add(celdaNombre);

        var celdaEmail = new Label
        {
            Text = Texto(usuario.Email),
            Style = Ui.EstiloDe("TextoSuave"),
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(celdaEmail, 2);
        fila.Add(celdaEmail);

        var celdaRoles = new Label
        {
            Text = usuario.Roles is { Length: > 0 } ? string.Join(", ", usuario.Roles) : "—",
            Style = Ui.EstiloDe("DatoFila"),
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(celdaRoles, 3);
        fila.Add(celdaRoles);

        var pastilla = new Border
        {
            Style = Ui.EstiloDe(usuario.Enabled ? "BadgeOk" : "BadgeOcupado"),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = usuario.Enabled ? "Activo" : "Inactivo",
                Style = Ui.EstiloDe(usuario.Enabled ? "BadgeOkTexto" : "BadgeOcupadoTexto")
            }
        };
        Grid.SetColumn(pastilla, 4);
        fila.Add(pastilla);

        var acciones = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        acciones.Add(BotonMini("Editar", "Acento", "TextoFuerte", () => AbrirEdicion(usuario)));
        acciones.Add(BotonMini("Password", "SuperficieAlta", "TextoClaro", () => AbrirPassword(usuario)));
        acciones.Add(BotonMini(
            usuario.Enabled ? "Desactivar" : "Activar",
            usuario.Enabled ? "Peligro" : "Acento",
            usuario.Enabled ? "Blanco" : "TextoFuerte",
            () => _ = CambiarEstadoAsync(usuario)));

        Grid.SetColumn(acciones, 5);
        fila.Add(acciones);

        return new Border
        {
            Style = Ui.EstiloDe("TarjetaLista"),
            Content = fila
        };
    }

    /// <summary>Botón de acción de una fila (estilo BotonMini + colores del sistema de diseño).</summary>
    private static Button BotonMini(string texto, string tokenFondo, string tokenTexto, Action alPulsar)
    {
        var boton = new Button
        {
            Text = texto,
            FontSize = 10,
            HeightRequest = 28,
            Padding = new Thickness(8, 0),
            CornerRadius = 6
        };

        var estilo = Ui.EstiloDe("BotonMini");
        if (estilo is not null) boton.Style = estilo;

        boton.BackgroundColor = Ui.ColorDe(tokenFondo);
        boton.TextColor = Ui.ColorDe(tokenTexto);
        boton.Clicked += (_, _) => alPulsar();

        return boton;
    }

    // ================================================================ crear
    private async void OnCrearClicked(object? sender, EventArgs e)
    {
        if (await SinPermiso()) return;

        var usuario = (txtNuevoUsuario.Text ?? "").Trim();
        var email = (txtNuevoEmail.Text ?? "").Trim();
        var nombre = (txtNuevoNombre.Text ?? "").Trim();
        var apellido = (txtNuevoApellido.Text ?? "").Trim();
        var password = txtNuevoPassword.Text ?? "";
        var rol = pckNuevoRol.SelectedItem?.ToString() ?? "CLIENTE";

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
            await ApiCliente.CrearUsuarioAsync(usuario, email, nombre, apellido, password, rol);

            LimpiarFormularioCrear();
            await Ui.Exito(this, "Usuario creado");

            await CargarAsync();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
    }

    private void LimpiarFormularioCrear()
    {
        txtNuevoUsuario.Text = "";
        txtNuevoEmail.Text = "";
        txtNuevoNombre.Text = "";
        txtNuevoApellido.Text = "";
        txtNuevoPassword.Text = "";
        pckNuevoRol.SelectedIndex = 0;
    }

    // ================================================================ editar
    private void AbrirEdicion(UsuarioDto usuario)
    {
        _enEdicion = usuario;

        lblEditarTitulo.Text = $"Editar · {Texto(usuario.Username)}";
        txtEditarNombre.Text = usuario.FirstName;
        txtEditarApellido.Text = usuario.LastName;
        txtEditarEmail.Text = usuario.Email;
        pckEditarRol.SelectedIndex = IndiceRolPrincipal(usuario.Roles);
        swEditarActivo.IsToggled = usuario.Enabled;

        panelPassword.IsVisible = false;
        _enPassword = null;

        panelEditar.IsVisible = true;
    }

    private void OnCancelarEditarClicked(object? sender, EventArgs e)
    {
        panelEditar.IsVisible = false;
        _enEdicion = null;
    }

    private async void OnGuardarEditarClicked(object? sender, EventArgs e)
    {
        if (await SinPermiso()) return;

        if (_enEdicion is null)
        {
            await Ui.Aviso(this, "Abre primero «Editar» en el usuario que quieres modificar.");
            return;
        }

        var nombre = (txtEditarNombre.Text ?? "").Trim();
        var apellido = (txtEditarApellido.Text ?? "").Trim();
        var email = (txtEditarEmail.Text ?? "").Trim();
        var rol = pckEditarRol.SelectedItem?.ToString() ?? Sesion.RolPrincipal;
        var activo = swEditarActivo.IsToggled;

        if (nombre.Length == 0 || apellido.Length == 0)
        {
            await Ui.Aviso(this, "El nombre y el apellido son obligatorios.");
            return;
        }
        if (!email.Contains('@'))
        {
            await Ui.Aviso(this, "Escribe un email válido.");
            return;
        }

        try
        {
            var id = _enEdicion.Id;

            // Dos llamadas seguidas y en este orden: datos y luego roles.
            await ApiCliente.ActualizarUsuarioAsync(id, email, nombre, apellido, activo);
            await ApiCliente.ActualizarRolesAsync(id, rol);

            panelEditar.IsVisible = false;
            _enEdicion = null;

            await Ui.Exito(this, "Usuario actualizado");

            await CargarAsync();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
    }

    // ================================================================ password
    private void AbrirPassword(UsuarioDto usuario)
    {
        _enPassword = usuario;

        lblPasswordUsuario.Text = "Usuario: " + Texto(usuario.Username);
        txtNuevaPassword.Text = "";

        panelEditar.IsVisible = false;
        _enEdicion = null;

        panelPassword.IsVisible = true;
    }

    private void OnCancelarPasswordClicked(object? sender, EventArgs e)
    {
        panelPassword.IsVisible = false;
        _enPassword = null;
    }

    private async void OnActualizarPasswordClicked(object? sender, EventArgs e)
    {
        if (await SinPermiso()) return;

        if (_enPassword is null)
        {
            await Ui.Aviso(this, "Abre primero «Password» en el usuario que quieres actualizar.");
            return;
        }

        var password = txtNuevaPassword.Text ?? "";
        if (password.Length < 6)
        {
            await Ui.Aviso(this, "La contraseña debe tener al menos 6 caracteres.");
            return;
        }

        try
        {
            await ApiCliente.ResetPasswordAsync(_enPassword.Id, password);

            panelPassword.IsVisible = false;
            _enPassword = null;

            await Ui.Exito(this, "Contraseña actualizada");
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
    }

    // ================================================================ activar / desactivar
    private async Task CambiarEstadoAsync(UsuarioDto usuario)
    {
        if (await SinPermiso()) return;

        var activar = !usuario.Enabled;

        var confirmado = activar
            ? await Ui.Confirmar(this, "Activar usuario", "¿Activar este usuario?")
            : await Ui.Confirmar(this, "Desactivar usuario", "¿Desactivar este usuario?");
        if (!confirmado) return;

        try
        {
            await ApiCliente.ActualizarUsuarioAsync(
                usuario.Id, usuario.Email, usuario.FirstName, usuario.LastName, activar);

            await Ui.Exito(this, activar ? "Usuario activado" : "Usuario desactivado");

            await CargarAsync();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
    }

    // ================================================================ utilidades
    private static string Texto(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? "—" : valor.Trim();

    /// <summary>Rol efectivo del usuario: ADMIN &gt; OPERADOR &gt; CLIENTE.</summary>
    private static int IndiceRolPrincipal(string[]? roles)
    {
        if (roles is null || roles.Length == 0)
            return 0;

        if (roles.Contains("ADMIN")) return Array.IndexOf(Roles, "ADMIN");
        if (roles.Contains("OPERADOR")) return Array.IndexOf(Roles, "OPERADOR");
        if (roles.Contains("CLIENTE")) return Array.IndexOf(Roles, "CLIENTE");

        var primero = Array.IndexOf(Roles, roles[0]);
        return primero >= 0 ? primero : 0;
    }
}
