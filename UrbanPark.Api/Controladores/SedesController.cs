using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrbanPark.Api.Modelos;
using UrbanPark.Api.Servicios;

namespace UrbanPark.Api.Controladores;

/// <summary>
/// Catalogo de parqueaderos (sedes) y recomendacion de cual conviene.
/// Es lo primero que usa el cliente: en el mapa elige la sede y despues el puesto.
/// </summary>
[Route("api/sedes")]
[Authorize]
public sealed class SedesController : ControladorBase
{
    private readonly ServicioSedes _sedes;

    public SedesController(ServicioSedes sedes) => _sedes = sedes;

    /// <summary>
    /// Lista de parqueaderos con su ocupacion y la distancia a la ubicacion del cliente.
    /// lat/lng son las coordenadas del celular (opcionales: sin ellas no se calcula la distancia).
    /// tipo filtra por disponibilidad de ese tipo de plaza (AUTO, MOTO, CAMIONETA...).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] double? lat, [FromQuery] double? lng,
        [FromQuery] string? tipo, [FromQuery] string? preferencia) =>
        Ok(await _sedes.ListarAsync(lat, lng, tipo, preferencia));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Obtener(long id, [FromQuery] double? lat, [FromQuery] double? lng)
    {
        var detalle = await _sedes.DetalleAsync(id, lat, lng);
        return detalle is null ? NoEncontrado("Parqueadero no encontrado") : Ok(detalle);
    }

    /// <summary>
    /// La IA elige el parqueadero que mas le conviene al cliente (distancia, plazas libres,
    /// precio y servicios) y explica el motivo en lenguaje natural.
    /// </summary>
    [HttpPost("recomendar")]
    [Authorize(Roles = "CLIENTE,ADMIN")]
    public async Task<IActionResult> Recomendar([FromBody] RecomendarSedeRequest peticion)
    {
        var (respuesta, error) = await _sedes.RecomendarAsync(peticion);
        return respuesta is null
            ? NoEncontrado(error ?? "No se encontró un parqueadero para recomendar.")
            : Ok(respuesta);
    }
}
