using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrbanPark.Api.Modelos;
using UrbanPark.Api.Servicios;

namespace UrbanPark.Api.Controladores;

/// <summary>
/// Pase de estacionamiento: el cliente elige un puesto de una sede y en ese instante se abre el
/// ticket y se devuelve el QR con los datos y el valor a pagar en ese momento. Se puede refrescar
/// (el valor sube con el tiempo) y anular si eligió el puesto por error.
/// </summary>
[Route("api/estacionar")]
[Authorize(Roles = "CLIENTE,OPERADOR,ADMIN")]
public sealed class EstacionarController : ControladorBase
{
    private readonly ServicioEstacionamiento _servicio;

    public EstacionarController(ServicioEstacionamiento servicio) => _servicio = servicio;

    /// <summary>
    /// Elige un puesto: abre el ticket y devuelve el pase con su QR.
    /// Con InicioReserva/FinReserva el puesto queda <b>reservado</b> para esa franja;
    /// sin ellos el cliente <b>entra en el acto</b>. En los dos casos se genera el QR.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Estacionar([FromBody] EstacionarRequest peticion)
    {
        var esReserva = string.Equals(peticion.Modo, "RESERVA", StringComparison.OrdinalIgnoreCase) ||
                        peticion.InicioReserva is not null;

        var resultado = await _servicio.EstacionarAsync(peticion.Placa, peticion.EspacioId, Username,
            esReserva ? peticion.InicioReserva : null,
            esReserva ? peticion.FinReserva : null);

        if (!resultado.Exito)
        {
            return resultado.Codigo switch
            {
                400 => PeticionInvalida(resultado.Mensaje ?? "Datos incompletos."),
                403 => Prohibido(resultado.Mensaje ?? "No puedes estacionar en ese puesto."),
                404 => NoEncontrado(resultado.Mensaje ?? "Puesto no encontrado."),
                409 => Conflicto(resultado.Mensaje ?? "El puesto ya está ocupado."),
                503 => NoDisponible(resultado.Mensaje ?? "El servicio de espacios no responde."),
                _ => Error(resultado.Codigo, resultado.Mensaje ?? "No se pudo estacionar.")
            };
        }

        return Ok(resultado.Pase);
    }

    /// <summary>Convierte la reserva en estancia activa: el QR empieza a contar desde ahora.</summary>
    [HttpPost("{ticketId:long}/activar")]
    public async Task<IActionResult> Activar(long ticketId)
    {
        var resultado = await _servicio.ActivarAsync(ticketId, Username, EsClienteSolo);
        if (!resultado.Exito)
        {
            return resultado.Codigo switch
            {
                403 => Prohibido(resultado.Mensaje ?? "No puedes activar esa reserva."),
                404 => NoEncontrado(resultado.Mensaje ?? "Ticket no encontrado."),
                409 => Conflicto(resultado.Mensaje ?? "Ese ticket no es una reserva pendiente."),
                _ => Error(resultado.Codigo, resultado.Mensaje ?? "No se pudo activar la reserva.")
            };
        }

        return Ok(resultado.Pase);
    }

    /// <summary>Refresca el pase: el valor a pagar se recalcula con la hora actual.</summary>
    [HttpGet("{ticketId:long}")]
    public async Task<IActionResult> Pase(long ticketId)
    {
        var pase = await _servicio.PaseAsync(ticketId);
        return pase is null ? NoEncontrado("No se encontró el ticket del puesto.") : Ok(pase);
    }

    /// <summary>Anula la reserva y libera el puesto (no se cobra nada).</summary>
    [HttpPost("{ticketId:long}/cancelar")]
    public async Task<IActionResult> Cancelar(long ticketId)
    {
        var resultado = await _servicio.CancelarAsync(ticketId, Username, EsClienteSolo);
        if (!resultado.Exito)
        {
            return resultado.Codigo switch
            {
                403 => Prohibido(resultado.Mensaje ?? "No puedes anular ese ticket."),
                404 => NoEncontrado(resultado.Mensaje ?? "Ticket no encontrado."),
                409 => Conflicto(resultado.Mensaje ?? "El ticket ya no está abierto."),
                _ => Error(resultado.Codigo, resultado.Mensaje ?? "No se pudo anular la reserva.")
            };
        }

        return Ok(new MensajeResponse(resultado.Mensaje ?? "Reserva anulada."));
    }
}
