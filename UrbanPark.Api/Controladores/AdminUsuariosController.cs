using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrbanPark.Api.Modelos;
using UrbanPark.Api.Repositorios;
using UrbanPark.Api.Servicios;

namespace UrbanPark.Api.Controladores;

/// <summary>Administracion de usuarios (equivalente a ms-auth /api/admin/users). Solo ADMIN.</summary>
[Route("api/admin/users")]
[Authorize(Roles = "ADMIN")]
public sealed class AdminUsuariosController : ControladorBase
{
    private readonly UsuarioRepositorio _usuarios;
    private readonly PasswordService _password;

    public AdminUsuariosController(UsuarioRepositorio usuarios, PasswordService password)
    {
        _usuarios = usuarios;
        _password = password;
    }

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var usuarios = await _usuarios.ListarAsync();
        return Ok(usuarios.Select(ADto));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Obtener(long id)
    {
        var usuario = await _usuarios.PorIdAsync(id);
        return usuario is null ? NoEncontrado("Usuario no encontrado") : Ok(ADto(usuario));
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CreateUserDto peticion)
    {
        var username = (peticion.Username ?? "").Trim();
        var email = (peticion.Email ?? "").Trim();
        var nombre = (peticion.FirstName ?? "").Trim();
        var apellido = (peticion.LastName ?? "").Trim();
        var password = peticion.Password ?? "";
        var rol = (peticion.Role ?? "").Trim().ToUpperInvariant();

        if (username.Length is < 4 or > 50)
            return PeticionInvalida("El usuario debe tener entre 4 y 50 caracteres");
        if (!EmailValido(email)) return PeticionInvalida("El email no es válido");
        if (string.IsNullOrWhiteSpace(nombre)) return PeticionInvalida("El nombre es obligatorio");
        if (string.IsNullOrWhiteSpace(apellido)) return PeticionInvalida("El apellido es obligatorio");
        if (password.Length is < 6 or > 100)
            return PeticionInvalida("La contraseña debe tener entre 6 y 100 caracteres");
        if (!Reglas.RolesValidos.Contains(rol)) return PeticionInvalida($"Rol no permitido: {rol}");

        if (await _usuarios.PorUsernameAsync(username) is not null)
            return Conflicto($"El usuario {username} ya existe");
        if (await _usuarios.PorEmailAsync(email) is not null)
            return Conflicto("Email ya en uso");

        var id = await _usuarios.CrearAsync(username, email, nombre, apellido, _password.Hashear(password), rol);
        var creado = await _usuarios.PorIdAsync(id);
        return StatusCode(201, ADto(creado!));
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Actualizar(long id, [FromBody] UpdateUserDto peticion)
    {
        var usuario = await _usuarios.PorIdAsync(id);
        if (usuario is null) return NoEncontrado("Usuario no encontrado");

        var email = (peticion.Email ?? "").Trim();
        var nombre = (peticion.FirstName ?? "").Trim();
        var apellido = (peticion.LastName ?? "").Trim();
        var habilitado = peticion.Enabled ?? usuario.Enabled;

        if (!EmailValido(email)) return PeticionInvalida("El email no es válido");
        if (string.IsNullOrWhiteSpace(nombre)) return PeticionInvalida("El nombre es obligatorio");
        if (string.IsNullOrWhiteSpace(apellido)) return PeticionInvalida("El apellido es obligatorio");

        var otro = await _usuarios.PorEmailAsync(email);
        if (otro is not null && otro.Id != id) return Conflicto("Email ya en uso");

        await _usuarios.ActualizarAsync(id, email, nombre, apellido, habilitado);
        var actualizado = await _usuarios.PorIdAsync(id);
        return Ok(ADto(actualizado!));
    }

    /// <summary>
    /// Reemplaza los roles por uno solo. Igual que el sistema original: si el rol es ADMIN
    /// se asigna tambien OPERADOR.
    /// </summary>
    [HttpPut("{id:long}/roles")]
    public async Task<IActionResult> ActualizarRoles(long id, [FromBody] UpdateRolesDto peticion)
    {
        var usuario = await _usuarios.PorIdAsync(id);
        if (usuario is null) return NoEncontrado("Usuario no encontrado");

        var rol = (peticion.Role ?? "").Trim().ToUpperInvariant();
        if (!Reglas.RolesValidos.Contains(rol)) return PeticionInvalida($"Rol no permitido: {rol}");

        var roles = rol == "ADMIN" ? "ADMIN,OPERADOR" : rol;
        await _usuarios.ActualizarRolesAsync(id, roles);

        var actualizado = await _usuarios.PorIdAsync(id);
        return Ok(ADto(actualizado!));
    }

    [HttpPut("{id:long}/password")]
    public async Task<IActionResult> ResetearPassword(long id, [FromBody] ResetPasswordDto peticion)
    {
        var usuario = await _usuarios.PorIdAsync(id);
        if (usuario is null) return NoEncontrado("Usuario no encontrado");

        if ((peticion.Password ?? "").Length is < 6 or > 100)
            return PeticionInvalida("La contraseña debe tener entre 6 y 100 caracteres");

        await _usuarios.ActualizarPasswordAsync(id, _password.Hashear(peticion.Password!));
        return Ok(new MensajeResponse("Contraseña actualizada"));
    }

    /// <summary>
    /// Quita el autenticador de Google de un usuario (si perdió el celular y no puede entrar).
    /// El ADMIN puede dejar la cuenta otra vez en usuario + contraseña.
    /// </summary>
    [HttpPut("{id:long}/totp")]
    public async Task<IActionResult> QuitarTotp(long id)
    {
        var usuario = await _usuarios.PorIdAsync(id);
        if (usuario is null) return NoEncontrado("Usuario no encontrado");

        await _usuarios.GuardarTotpAsync(id, null, false);
        return Ok(new MensajeResponse($"Autenticador de Google desactivado para {usuario.Username}"));
    }
}
