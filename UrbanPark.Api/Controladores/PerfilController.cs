using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrbanPark.Api.Modelos;
using UrbanPark.Api.Repositorios;
using UrbanPark.Api.Servicios;

namespace UrbanPark.Api.Controladores;

/// <summary>Perfil del usuario autenticado (equivalente a ms-auth /api/perfil).</summary>
[Route("api/perfil")]
[Authorize]
public sealed class PerfilController : ControladorBase
{
    private readonly UsuarioRepositorio _usuarios;
    private readonly PasswordService _password;

    public PerfilController(UsuarioRepositorio usuarios, PasswordService password)
    {
        _usuarios = usuarios;
        _password = password;
    }

    [HttpGet("me")]
    public async Task<IActionResult> MiPerfil()
    {
        var usuario = await _usuarios.PorUsernameAsync(Username);
        return usuario is null ? NoEncontrado("Usuario no encontrado") : Ok(ADto(usuario));
    }

    [HttpPut("me")]
    public async Task<IActionResult> ActualizarPerfil([FromBody] UpdatePerfilDto peticion)
    {
        var usuario = await _usuarios.PorUsernameAsync(Username);
        if (usuario is null) return NoEncontrado("Usuario no encontrado");

        var email = (peticion.Email ?? "").Trim();
        var nombre = (peticion.FirstName ?? "").Trim();
        var apellido = (peticion.LastName ?? "").Trim();

        if (!EmailValido(email)) return PeticionInvalida("El email no es válido");
        if (string.IsNullOrWhiteSpace(nombre)) return PeticionInvalida("El nombre es obligatorio");
        if (string.IsNullOrWhiteSpace(apellido)) return PeticionInvalida("El apellido es obligatorio");

        var otro = await _usuarios.PorEmailAsync(email);
        if (otro is not null && otro.Id != usuario.Id)
            return Conflicto("Email ya en uso");

        await _usuarios.ActualizarAsync(usuario.Id, email, nombre, apellido, usuario.Enabled);
        var actualizado = await _usuarios.PorIdAsync(usuario.Id);
        return Ok(ADto(actualizado!));
    }

    [HttpPut("me/password")]
    public async Task<IActionResult> CambiarPassword([FromBody] ChangePasswordDto peticion)
    {
        var usuario = await _usuarios.PorUsernameAsync(Username);
        if (usuario is null) return NoEncontrado("Usuario no encontrado");

        if (string.IsNullOrWhiteSpace(peticion.CurrentPassword))
            return PeticionInvalida("La contraseña actual es obligatoria");
        if ((peticion.NewPassword ?? "").Length is < 6 or > 100)
            return PeticionInvalida("La nueva contraseña debe tener entre 6 y 100 caracteres");

        if (!_password.Verificar(peticion.CurrentPassword, usuario.PasswordHash))
            return PeticionInvalida("Contraseña actual incorrecta");

        await _usuarios.ActualizarPasswordAsync(usuario.Id, _password.Hashear(peticion.NewPassword!));
        return Ok(new MensajeResponse("Contraseña cambiada"));
    }
}
