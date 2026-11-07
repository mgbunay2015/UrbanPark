using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrbanPark.Api.Servicios;

namespace UrbanPark.Api.Controladores;

/// <summary>Actualización masiva de tarifas de sedes y plazas. Solo ADMIN.</summary>
[Route("api/admin/tarifas")]
[Authorize(Roles = "ADMIN")]
public sealed class AdminTarifasController : ControladorBase
{
    private readonly ServicioTarifasAdmin _tarifas;

    public AdminTarifasController(ServicioTarifasAdmin tarifas) => _tarifas = tarifas;

    [HttpGet("resumen")]
    public async Task<IActionResult> Resumen()
    {
        var filas = await _tarifas.ResumenAsync();
        return Ok(filas);
    }

    [HttpPost("aplicar")]
    public async Task<IActionResult> Aplicar([FromBody] TarifasMasivasPeticion? peticion)
    {
        if (peticion is null)
            return PeticionInvalida("Falta el cuerpo de la petición.");

        try
        {
            var (sedes, plazas, detalle) = await _tarifas.AplicarAsync(peticion);
            return Ok(new { sedes, plazas, detalle, mensaje = detalle });
        }
        catch (ArgumentException ex)
        {
            return PeticionInvalida(ex.Message);
        }
    }
}
