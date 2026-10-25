using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrbanPark.Api.Modelos;
using UrbanPark.Api.Repositorios;
using UrbanPark.Api.Servicios;

namespace UrbanPark.Api.Controladores;

/// <summary>
/// Autenticacion propia (reemplaza a Keycloak) con dos vias:
///   1) usuario/contraseña contra la tabla MySQL "usuarios"
///   2) Google Sign-In (id_token validado contra Google)
/// Ambas emiten el mismo JWT con los claims que ya usaba el sistema original.
/// </summary>
[Route("api/auth")]
public sealed class AuthController : ControladorBase
{
    private readonly UsuarioRepositorio _usuarios;
    private readonly PasswordService _password;
    private readonly TokenService _tokens;
    private readonly GoogleAuthService _google;
    private readonly BiometriaRepositorio _biometrias;
    private readonly DispositivoRepositorio _dispositivos;
    private readonly ILogger<AuthController> _log;

    public AuthController(UsuarioRepositorio usuarios, PasswordService password, TokenService tokens,
        GoogleAuthService google, BiometriaRepositorio biometrias, DispositivoRepositorio dispositivos,
        ILogger<AuthController> log)
    {
        _usuarios = usuarios;
        _password = password;
        _tokens = tokens;
        _google = google;
        _biometrias = biometrias;
        _dispositivos = dispositivos;
        _log = log;
    }

    // ---------------------------------------------------------------- registro publico
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Registrarse([FromBody] RegisterRequest peticion)
    {
        var username = (peticion.Username ?? "").Trim();
        var email = (peticion.Email ?? "").Trim();
        var nombre = (peticion.FirstName ?? "").Trim();
        var apellido = (peticion.LastName ?? "").Trim();
        var password = peticion.Password ?? "";
        var rol = (peticion.Role ?? "").Trim().ToUpperInvariant();

        if (username.Length is < 4 or > 50)
            return PeticionInvalida("El usuario debe tener entre 4 y 50 caracteres");
        if (!EmailValido(email))
            return PeticionInvalida("El email no es válido");
        if (string.IsNullOrWhiteSpace(nombre))
            return PeticionInvalida("El nombre es obligatorio");
        if (string.IsNullOrWhiteSpace(apellido))
            return PeticionInvalida("El apellido es obligatorio");
        if (password.Length is < 6 or > 100)
            return PeticionInvalida("La contraseña debe tener entre 6 y 100 caracteres");
        if (!Reglas.RolesValidos.Contains(rol))
            return PeticionInvalida($"Rol no permitido: {rol}");

        if (await _usuarios.PorUsernameAsync(username) is not null)
            return Conflicto($"El usuario {username} ya existe");
        if (await _usuarios.PorEmailAsync(email) is not null)
            return Conflicto("Email ya en uso");

        var id = await _usuarios.CrearAsync(username, email, nombre, apellido, _password.Hashear(password), rol);
        _log.LogInformation("Usuario registrado: {Usuario} ({Rol})", username, rol);

        return StatusCode(201, new RegisterResponse(id.ToString(), username, rol, $"Usuario registrado como {rol}"));
    }

    // ---------------------------------------------------------------- login local
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> IniciarSesion([FromBody] LoginRequest peticion)
    {
        var username = (peticion.Username ?? "").Trim();
        var password = peticion.Password ?? "";

        var usuario = await _usuarios.PorUsernameAsync(username);
        if (usuario is null || !_password.Verificar(password, usuario.PasswordHash))
            return NoAutorizado("Usuario o contraseña incorrectos");

        if (!usuario.Enabled)
            return Prohibido("La cuenta está desactivada. Contacta al administrador.");

        // Segundo factor: si el usuario activó el autenticador de Google, se pide el código.
        if (usuario.TotpActivo)
        {
            var codigo = (peticion.CodigoTotp ?? "").Trim();
            if (codigo.Length == 0)
                return TotpRequerido();

            if (!ServicioTotp.Verificar(usuario.TotpSecret, codigo))
                return NoAutorizado("El código del autenticador no es válido o ya expiró. Escribe el que muestra la app ahora.");

            // El equipo queda vinculado: la próxima vez basta con el código de 6 dígitos.
            var dispositivo = await VincularDispositivoAsync(usuario);
            return Ok(ConstruirSesion(usuario, dispositivo));
        }

        return Ok(ConstruirSesion(usuario));
    }

    /// <summary>Respuesta 401 que la app reconoce para pedir el código de 6 dígitos.</summary>
    private ObjectResult TotpRequerido() =>
        StatusCode(401, new ErrorResponse(DateTime.Now, 401, "TOTP_REQUIRED",
            "Este usuario tiene el autenticador de Google activo: escribe el código de 6 dígitos.",
            Request.Path.Value ?? ""));

    // ---------------------------------------------------------------- autenticador de Google
    [HttpGet("totp/estado")]
    [Authorize]
    public async Task<IActionResult> EstadoTotp()
    {
        var usuario = await _usuarios.PorUsernameAsync(Username);
        if (usuario is null) return NoEncontrado("Usuario no encontrado");

        return Ok(new TotpEstadoResponse(usuario.TotpActivo,
            usuario.TotpActivo
                ? "El autenticador de Google está activo: al entrar se pedirá el código de 6 dígitos."
                : "El autenticador de Google está desactivado."));
    }

    /// <summary>Genera el secreto y la URI del QR. Todavía no se activa hasta confirmar un código.</summary>
    [HttpPost("totp/preparar")]
    [Authorize]
    public async Task<IActionResult> PrepararTotp()
    {
        var usuario = await _usuarios.PorUsernameAsync(Username);
        if (usuario is null) return NoEncontrado("Usuario no encontrado");

        var secreto = ServicioTotp.NuevoSecreto();
        await _usuarios.GuardarTotpAsync(usuario.Id, secreto, false);

        return Ok(new TotpPrepararResponse(secreto, ServicioTotp.UriProvisioning(usuario.Username, secreto),
            "Escanea el QR con Google Authenticator y escribe el código de 6 dígitos para confirmar."));
    }

    /// <summary>Confirma el código del autenticador y deja el segundo factor activo.</summary>
    [HttpPost("totp/confirmar")]
    [Authorize]
    public async Task<IActionResult> ConfirmarTotp([FromBody] TotpCodigoRequest peticion)
    {
        var usuario = await _usuarios.PorUsernameAsync(Username);
        if (usuario is null) return NoEncontrado("Usuario no encontrado");

        if (string.IsNullOrWhiteSpace(usuario.TotpSecret))
            return PeticionInvalida("Primero pide el QR del autenticador (POST /api/auth/totp/preparar).");

        if (!ServicioTotp.Verificar(usuario.TotpSecret, peticion.Codigo))
            return NoAutorizado("El código no coincide. Revisa la hora del equipo y vuelve a intentar.");

        await _usuarios.GuardarTotpAsync(usuario.Id, usuario.TotpSecret, true);
        _log.LogInformation("Autenticador de Google activado para {Usuario}", usuario.Username);

        return Ok(new TotpEstadoResponse(true, "Autenticador activado: desde ahora se pedirá el código al entrar."));
    }

    /// <summary>Desactiva el segundo factor (pide un código válido para confirmarlo).</summary>
    [HttpPost("totp/desactivar")]
    [Authorize]
    public async Task<IActionResult> DesactivarTotp([FromBody] TotpCodigoRequest peticion)
    {
        var usuario = await _usuarios.PorUsernameAsync(Username);
        if (usuario is null) return NoEncontrado("Usuario no encontrado");

        if (!usuario.TotpActivo) return Ok(new TotpEstadoResponse(false, "El autenticador ya estaba desactivado."));
        if (!ServicioTotp.Verificar(usuario.TotpSecret, peticion.Codigo))
            return NoAutorizado("El código no coincide. Escribe el que muestra la app ahora.");

        await _usuarios.GuardarTotpAsync(usuario.Id, null, false);
        await _dispositivos.BorrarPorUsuarioAsync(usuario.Username);
        _log.LogInformation("Autenticador de Google desactivado para {Usuario}", usuario.Username);

        return Ok(new TotpEstadoResponse(false, "Autenticador desactivado."));
    }

    // ---------------------------------------------------------------- login con huella / rostro ya enrolados
    [HttpPost("biometria")]
    [AllowAnonymous]
    public async Task<IActionResult> IniciarConBiometria([FromBody] LoginBiometriaRequest peticion)
    {
        var hash = (peticion.TokenHash ?? "").Trim();
        if (hash.Length < 32)
            return PeticionInvalida("Falta la huella registrada");

        var bio = await _biometrias.PorTokenHashAsync(hash);
        if (bio is null)
            return NoAutorizado("Esa huella no está en la base de datos. Entra con usuario y contraseña y regístrala.");

        var usuario = await _usuarios.PorUsernameAsync(bio.Usuario);
        if (usuario is null || !usuario.Enabled)
            return Prohibido("La cuenta está desactivada. Contacta al administrador.");

        return Ok(ConstruirSesion(usuario));
    }

    // ---------------------------------------------------------------- Google Sign-In
    [HttpPost("google")]
    [AllowAnonymous]
    public async Task<IActionResult> IniciarConGoogle([FromBody] GoogleLoginRequest peticion)
    {
        if (string.IsNullOrWhiteSpace(peticion.IdToken))
            return PeticionInvalida("Falta el id_token de Google");

        if (!_google.Configurado)
            return NoDisponible("Google no está configurado en el servidor. Revisa la sección Google de appsettings.json.");

        GoogleAuthService.PerfilGoogle perfil;
        try
        {
            perfil = await _google.ValidarAsync(peticion.IdToken);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Token de Google inválido");
            return NoAutorizado("El token de Google no es válido o ha expirado");
        }

        if (string.IsNullOrWhiteSpace(perfil.Email))
            return PeticionInvalida("La cuenta de Google no tiene email verificado");

        // 1) ya vinculado por sub  2) mismo email -> se vincula  3) se crea como CLIENTE
        var usuario = await _usuarios.PorGoogleSubAsync(perfil.Sub)
                      ?? await _usuarios.PorEmailAsync(perfil.Email);

        if (usuario is null)
        {
            var username = await GenerarUsernameAsync(perfil.Email);
            var id = await _usuarios.CrearAsync(username, perfil.Email, perfil.Nombre, perfil.Apellido,
                _password.Hashear(Guid.NewGuid().ToString("N")), "CLIENTE", true, perfil.Sub);
            usuario = await _usuarios.PorIdAsync(id);
            _log.LogInformation("Usuario creado desde Google: {Usuario}", username);
        }
        else if (string.IsNullOrEmpty(usuario.GoogleSub))
        {
            await _usuarios.VincularGoogleAsync(usuario.Id, perfil.Sub);
        }

        if (usuario is null)
            return Error(500, "No se pudo crear el usuario de Google");

        if (!usuario.Enabled)
            return Prohibido("La cuenta está desactivada. Contacta al administrador.");

        return Ok(ConstruirSesion(usuario));
    }

    // ---------------------------------------------------------------- refresco de sesion
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refrescar([FromBody] RefreshRequest peticion)
    {
        var principal = _tokens.Validar(peticion.RefreshToken, TokenService.TipoRefresh);
        var username = principal?.FindFirst("preferred_username")?.Value;
        if (string.IsNullOrWhiteSpace(username))
            return NoAutorizado("Refresh token inválido o expirado");

        var usuario = await _usuarios.PorUsernameAsync(username);
        if (usuario is null || !usuario.Enabled)
            return NoAutorizado("La sesión ya no es válida");

        return Ok(ConstruirSesion(usuario));
    }

    // ---------------------------------------------------------------- Google: configuracion publica
    /// <summary>La app MAUI consulta aqui si el backend tiene Google habilitado y con qué Client ID.</summary>
    [HttpGet("google/config")]
    [AllowAnonymous]
    public IActionResult ConfiguracionGoogle() => Ok(new
    {
        configurado = _google.Configurado,
        clientId = _google.Configurado ? _google.ClientId : ""
    });

    [HttpGet("salud")]
    [AllowAnonymous]
    public IActionResult Salud() => Ok(new { status = "UrbanPark auth OK", servicio = "Autenticación local + Google" });

    // ---------------------------------------------------------------- utilidades
    private SesionResponse ConstruirSesion(Usuario usuario, string? dispositivoToken = null)
    {
        var roles = Reglas.RolesDeApp(usuario.ListaRoles);
        var (token, expira) = _tokens.CrearAcceso(usuario.Username, roles);
        var refresh = _tokens.CrearRefresh(usuario.Username);
        return new SesionResponse(token, refresh, expira, usuario.Username, roles, ADto(usuario), dispositivoToken);
    }

    /// <summary>
    /// Vincula este equipo al usuario: a partir de ahora puede entrar solo con el código
    /// del autenticador. El token se guarda hasheado.
    /// </summary>
    private async Task<string> VincularDispositivoAsync(Usuario usuario)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await _dispositivos.CrearAsync(usuario.Username, Hash(token));
        return token;
    }

    private static string Hash(string valor) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor))).ToLowerInvariant();

    // ---------------------------------------------------------------- login solo con el código
    /// <summary>
    /// Entrada desde un equipo ya vinculado: solo el código de 6 dígitos del autenticador.
    /// Si el equipo no está vinculado o el código no sirve, responde 401.
    /// </summary>
    [HttpPost("login-codigo")]
    [AllowAnonymous]
    public async Task<IActionResult> IniciarConCodigo([FromBody] LoginCodigoRequest peticion)
    {
        var dispositivo = (peticion.Dispositivo ?? "").Trim();
        var codigo = (peticion.Codigo ?? "").Trim();

        if (dispositivo.Length < 32)
            return NoAutorizado("Este equipo no está vinculado. Entra una vez con usuario y contraseña.");
        if (codigo.Length == 0)
            return NoAutorizado("Escribe el código de 6 dígitos que muestra Google Authenticator.");

        var equipo = await _dispositivos.PorTokenAsync(Hash(dispositivo));
        if (equipo is null)
            return NoAutorizado("Este equipo ya no está vinculado. Entra con usuario y contraseña.");

        var usuario = await _usuarios.PorUsernameAsync(equipo.Usuario);
        if (usuario is null || !usuario.Enabled)
            return Prohibido("La cuenta está desactivada. Contacta al administrador.");
        if (!usuario.TotpActivo)
            return NoAutorizado("El autenticador de esta cuenta está desactivado. Entra con usuario y contraseña.");

        if (!ServicioTotp.Verificar(usuario.TotpSecret, codigo))
            return NoAutorizado("El código del autenticador no es válido o ya expiró. Escribe el que muestra la app ahora.");

        await _dispositivos.MarcarUsoAsync(equipo.Id);
        _log.LogInformation("Entrada con autenticador desde un equipo vinculado: {Usuario}", usuario.Username);

        return Ok(ConstruirSesion(usuario, dispositivo));
    }

    /// <summary>
    /// Entrada de un toque desde un equipo ya vinculado: no pide el código del autenticador.
    /// La app confirma antes con la huella/rostro del dueño del teléfono. Si el equipo no
    /// está vinculado o la cuenta ya no usa autenticador, responde 401.
    /// </summary>
    [HttpPost("login-dispositivo")]
    [AllowAnonymous]
    public async Task<IActionResult> IniciarConDispositivo([FromBody] LoginDispositivoRequest peticion)
    {
        var dispositivo = (peticion.Dispositivo ?? "").Trim();
        if (dispositivo.Length < 32)
            return NoAutorizado("Este equipo no está vinculado. Entra con usuario y contraseña.");

        var equipo = await _dispositivos.PorTokenAsync(Hash(dispositivo));
        if (equipo is null)
            return NoAutorizado("Este equipo ya no está vinculado. Entra con usuario y contraseña.");

        var usuario = await _usuarios.PorUsernameAsync(equipo.Usuario);
        if (usuario is null || !usuario.Enabled)
            return Prohibido("La cuenta está desactivada. Contacta al administrador.");
        if (!usuario.TotpActivo)
            return NoAutorizado("El autenticador de esta cuenta está desactivado. Entra con usuario y contraseña.");

        await _dispositivos.MarcarUsoAsync(equipo.Id);
        _log.LogInformation("Entrada de un toque desde un equipo vinculado: {Usuario}", usuario.Username);

        return Ok(ConstruirSesion(usuario, dispositivo));
    }
    /// <summary>Dice si una cuenta ya tiene el autenticador activo (la app lo usa para el login corto).</summary>
    [HttpGet("totp/requerido")]
    [AllowAnonymous]
    public async Task<IActionResult> TotpRequerido([FromQuery] string? username)
    {
        var usuario = await _usuarios.PorUsernameAsync((username ?? "").Trim());
        var activo = usuario?.TotpActivo == true;
        return Ok(new TotpRequeridoResponse((username ?? "").Trim(), activo,
            activo ? "Escribe el código de Google Authenticator." : "Entra con usuario y contraseña."));
    }

    private async Task<string> GenerarUsernameAsync(string email)
    {
        var baseNombre = email.Split('@')[0].ToLowerInvariant();
        var limpio = new string(baseNombre.Where(c => char.IsLetterOrDigit(c) || c is '.' or '_').ToArray());
        if (limpio.Length < 4) limpio = "user" + limpio;

        var candidato = limpio;
        var n = 1;
        while (await _usuarios.PorUsernameAsync(candidato) is not null)
            candidato = limpio + (++n);

        return candidato.Length > 50 ? candidato[..50] : candidato;
    }
}
