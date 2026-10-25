using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrbanPark.Api.Modelos;
using UrbanPark.Api.Repositorios;

namespace UrbanPark.Api.Controladores;

/// <summary>Enrolamiento de huella o rostro para abrir el QR de salida.</summary>
[Route("api/biometrias")]
[Authorize(Roles = "CLIENTE,OPERADOR,ADMIN")]
public sealed class BiometriasController : ControladorBase
{
    private readonly BiometriaRepositorio _repo;
    private readonly VehiculoRepositorio _vehiculos;

    public BiometriasController(BiometriaRepositorio repo, VehiculoRepositorio vehiculos)
    {
        _repo = repo;
        _vehiculos = vehiculos;
    }

    private static BiometriaDto A(Biometria b) =>
        new(b.Id, b.Usuario, b.VehiculoId, b.Placa, b.Tipo, b.Dispositivo, b.FotoRostro is { Length: > 0 }, b.CreadoEn);

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var lista = await _repo.ListarPorUsuarioAsync(Username);
        return Ok(lista.Select(A));
    }

    [HttpPost]
    public async Task<IActionResult> Guardar([FromBody] GuardarBiometriaDto peticion)
    {
        var tipo = (peticion.Tipo ?? "").Trim().ToUpperInvariant();
        if (tipo is not "HUELLA" and not "ROSTRO")
            return PeticionInvalida("Elige HUELLA o ROSTRO");

        if (string.IsNullOrWhiteSpace(peticion.TokenHash) || peticion.TokenHash.Length < 32)
            return PeticionInvalida("Falta el token biométrico");

        var placa = (peticion.Placa ?? "").Trim().ToUpperInvariant();
        if (placa.Length == 0)
            placa = "LOGIN";

        if (peticion.VehiculoId is > 0)
        {
            var vehiculo = await _vehiculos.PorIdAsync(peticion.VehiculoId.Value);
            if (vehiculo is null) return NoEncontrado("Vehículo no encontrado");
            if (EsClienteSolo &&
                !string.Equals(vehiculo.PropietarioUsername, Username, StringComparison.OrdinalIgnoreCase))
                return Prohibido("Solo puedes enrolar tus propios vehículos");
            placa = vehiculo.Placa;
        }

        byte[]? foto = null;
        if (!string.IsNullOrWhiteSpace(peticion.FotoBase64))
        {
            try
            {
                foto = Convert.FromBase64String(peticion.FotoBase64);
            }
            catch (FormatException)
            {
                return PeticionInvalida("La foto del rostro no es válida");
            }

            if (foto.Length > 2_500_000)
                return PeticionInvalida("La foto del rostro es demasiado grande");
        }

        if (tipo == "ROSTRO" && foto is not { Length: > 0 })
            return PeticionInvalida("Para el rostro hay que capturar una foto");

        var id = await _repo.GuardarAsync(Username, peticion.VehiculoId, placa, tipo,
            peticion.TokenHash.Trim(), peticion.Dispositivo, foto);

        var guardada = (await _repo.ListarPorUsuarioAsync(Username)).First(b => b.Id == id);
        return Ok(A(guardada));
    }

    public sealed record VerificarBiometriaDto(string? TokenHash);

    [HttpPost("verificar")]
    public async Task<IActionResult> Verificar([FromBody] VerificarBiometriaDto peticion)
    {
        if (string.IsNullOrWhiteSpace(peticion.TokenHash))
            return PeticionInvalida("Falta el token biométrico");

        var ok = await _repo.TokenValidoAsync(Username, peticion.TokenHash.Trim());
        return ok
            ? Ok(new MensajeResponse("Biometría verificada"))
            : Prohibido("La huella o el rostro no coinciden con el registro");
    }
}
