using System.Globalization;
using UrbanPark.Api.Modelos;
using UrbanPark.Api.Repositorios;

namespace UrbanPark.Api.Servicios;

/// <summary>
/// Pase de estacionamiento: al elegir un puesto se abre el ticket y se devuelve TODO lo que
/// necesita el QR de la app (sede, dirección, puesto, placa, entrada y el valor a pagar en ese
/// momento). El monto se recalcula cada vez que la app refresca el pase.
/// </summary>
public sealed class ServicioEstacionamiento
{
    private readonly ServicioTickets _tickets;
    private readonly TicketRepositorio _ticketRepo;
    private readonly EspacioRepositorio _espacios;
    private readonly SedeRepositorio _sedes;
    private readonly VehiculoRepositorio _vehiculos;
    private readonly ILogger<ServicioEstacionamiento> _log;

    public ServicioEstacionamiento(ServicioTickets tickets, TicketRepositorio ticketRepo, EspacioRepositorio espacios,
        SedeRepositorio sedes, VehiculoRepositorio vehiculos, ILogger<ServicioEstacionamiento> log)
    {
        _tickets = tickets;
        _ticketRepo = ticketRepo;
        _espacios = espacios;
        _sedes = sedes;
        _vehiculos = vehiculos;
        _log = log;
    }

    public sealed record Resultado(bool Exito, int Codigo, string? Mensaje, PasePuestoDto? Pase);

    // ================================================================ elegir puesto
    /// <summary>Abre el ticket del puesto elegido y devuelve el pase con el QR.</summary>
    public async Task<Resultado> EstacionarAsync(string? placa, long? espacioId, string username,
        DateTime? inicioReserva = null, DateTime? finReserva = null)
    {
        if (espacioId is null or <= 0)
            return new Resultado(false, 400, "Debes elegir un puesto del parqueadero.", null);

        var abierto = await _ticketRepo.AbiertoPorEspacioAsync(espacioId.Value);
        if (abierto is not null)
        {
            // Si el puesto ya es de este conductor y con la misma placa, se devuelve su pase
            // (pulsar dos veces el mismo puesto no debe fallar). Si es otra placa, está ocupado.
            var mismaPlaca = string.IsNullOrWhiteSpace(placa) || PlacaIgual(abierto.Placa, placa);
            if (MismoDueno(abierto, username) && mismaPlaca)
                return new Resultado(true, 200, null, await PaseAsync(abierto.Id));

            var estado = string.Equals(abierto.Estado, "RESERVADO", StringComparison.OrdinalIgnoreCase)
                ? $"reservado para el {abierto.Entrada:dd/MM/yyyy HH:mm}"
                : "ocupado";
            return new Resultado(false, 409,
                $"El puesto ya está {estado} ({abierto.Placa}). Elige otro puesto libre en el mapa.", null);
        }

        var espacio = await _espacios.PorIdAsync(espacioId.Value);
        if (espacio is null)
            return new Resultado(false, 404, "Ese puesto no existe. Actualiza el mapa y elige otro.", null);

        var resultado = await _tickets.AbrirAsync(placa, espacioId, username, inicioReserva, finReserva);
        if (!resultado.Exito || resultado.Ticket is null)
            return new Resultado(false, resultado.Codigo, resultado.Mensaje, null);

        var pase = await PaseAsync(resultado.Ticket.Id);
        return pase is null
            ? new Resultado(false, 500, "El ticket se abrió pero no se pudo armar el pase.", null)
            : new Resultado(true, 200, null, pase);
    }

    /// <summary>Ya llegó el cliente: la reserva pasa a ser una estancia normal y el QR empieza a correr.</summary>
    public async Task<Resultado> ActivarAsync(long ticketId, string username, bool esClienteSolo)
    {
        var ticket = await _ticketRepo.PorIdAsync(ticketId);
        if (ticket is null) return new Resultado(false, 404, "Ticket no encontrado.", null);

        if (esClienteSolo && !ServicioTickets.Pertenece(ticket, username))
            return new Resultado(false, 403, "Solo puedes activar tus propias reservas.", null);

        if (!string.Equals(ticket.Estado, "RESERVADO", StringComparison.OrdinalIgnoreCase))
            return new Resultado(false, 409, "Ese ticket no es una reserva pendiente.", null);

        await _ticketRepo.ActivarAsync(ticket.Id, DateTime.Now);
        var pase = await PaseAsync(ticket.Id);
        return pase is null
            ? new Resultado(false, 500, "No se pudo armar el pase de la reserva.", null)
            : new Resultado(true, 200, "Reserva activada: tu tiempo empieza a correr ahora.", pase);
    }

    /// <summary>Pase del ticket con el valor a pagar en este instante (se usa para refrescar el QR).</summary>
    public async Task<PasePuestoDto?> PaseAsync(long ticketId)
    {
        var ticket = await _ticketRepo.PorIdAsync(ticketId);
        if (ticket is null) return null;

        var espacio = await _espacios.PorIdAsync(ticket.EspacioId);
        var sede = espacio?.SedeId is null ? null : await _sedes.PorIdAsync(espacio.SedeId.Value);

        var descuento = await DescuentoAsync(ticket.Placa);
        var entrada = ticket.Entrada;
        var esReserva = string.Equals(ticket.Estado, "RESERVADO", StringComparison.OrdinalIgnoreCase);
        var fin = esReserva ? ticket.SalidaPrevista ?? entrada.AddHours(1) : ticket.Salida ?? DateTime.Now;

        var minutos = esReserva ? 0 : Math.Max(0, (int)(fin - entrada).TotalMinutes);
        var horas = Math.Max(1, (int)Math.Ceiling(Math.Max(1, esReserva ? (fin - entrada).TotalMinutes : minutos) / 60.0));
        var tarifa = ticket.TarifaHora ?? espacio?.TarifaHora ?? 0;
        var bruto = Reglas.CalcularMonto(entrada, esReserva ? fin : ticket.Salida, tarifa);
        var monto = Reglas.CalcularMonto(entrada, esReserva ? fin : ticket.Salida, tarifa, descuento);
        var porHora = Math.Round(tarifa * (1 - Math.Clamp(descuento, 0, 100) / 100.0), 2, MidpointRounding.AwayFromZero);

        var pase = new PasePuestoDto(
            ticket.Id, ticket.Codigo, ticket.Placa,
            sede?.Id ?? 0, sede?.Codigo ?? "", sede?.Nombre ?? espacio?.SedeNombre ?? "Urban Park",
            sede?.Direccion ?? espacio?.SedeDireccion ?? "", sede?.Sector ?? "",
            sede?.Latitud ?? 0, sede?.Longitud ?? 0,
            espacio?.Id ?? ticket.EspacioId, espacio?.Codigo ?? ticket.EspacioCodigo ?? "",
            espacio?.Zona ?? ticket.Zona ?? "", espacio?.Nivel, espacio?.TipoVehiculo ?? "AUTO", tarifa,
            entrada, minutos, horas, descuento, bruto, monto, porHora, ticket.Estado,
            ticket.Usuario ?? "", "",
            esReserva ? "RESERVA" : "AHORA",
            esReserva ? fin : null,
            esReserva ? horas : 0,
            esReserva ? monto : 0);

        return pase with { QrPayload = Payload(pase) };
    }

    // ================================================================ anular
    /// <summary>Libera el puesto elegido por error: el ticket queda ANULADO y no se cobra nada.</summary>
    public async Task<Resultado> CancelarAsync(long ticketId, string username, bool esClienteSolo)
    {
        var ticket = await _ticketRepo.PorIdAsync(ticketId);
        if (ticket is null) return new Resultado(false, 404, "Ticket no encontrado.", null);

        if (esClienteSolo && !ServicioTickets.Pertenece(ticket, username))
            return new Resultado(false, 403, "Solo puedes anular tus propios tickets.", null);

        if (ticket.Estado is not ("ABIERTO" or "RESERVADO"))
            return new Resultado(false, 409, "El ticket ya no está activo, no se puede anular.", null);

        await _ticketRepo.AnularAsync(ticket.Id, DateTime.Now);

        var liberacionPendiente = false;
        try
        {
            await _espacios.LiberarAsync(ticket.EspacioId);
        }
        catch (Exception ex)
        {
            liberacionPendiente = true;
            _log.LogWarning("No se pudo liberar el puesto {Puesto} al anular el ticket {Codigo}: {Motivo}",
                ticket.EspacioCodigo, ticket.Codigo, ex.Message);
        }

        if (liberacionPendiente)
        {
            await _ticketRepo.MarcarPendienteAsync(ticket.Id, true);
            return new Resultado(true, 200,
                "Reserva anulada. El puesto se liberará cuando el servicio de espacios vuelva.", null);
        }

        return new Resultado(true, 200, $"Puesto {ticket.EspacioCodigo} liberado. No se cobró nada.", null);
    }

    // ================================================================ auxiliares
    private async Task<int> DescuentoAsync(string placa)
    {
        var vehiculo = await _vehiculos.PorPlacaAsync(placa);
        return vehiculo is null ? 0 : Math.Max(vehiculo.Descuento, Reglas.DescuentoDe(vehiculo.Propietario));
    }

    private static bool MismoDueno(Ticket ticket, string username) =>
        !string.IsNullOrWhiteSpace(ticket.Usuario) &&
        ticket.Usuario.Equals(username, StringComparison.OrdinalIgnoreCase);

    private static bool PlacaIgual(string a, string? b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Contenido del QR: legible para una persona y verificable por el lector de la salida.
    /// Lleva la sede, la dirección, el puesto, la placa, la entrada, el valor de este momento
    /// y, si es una reserva, la franja horaria que se guardó.
    /// </summary>
    public static string Payload(PasePuestoDto p) =>
        string.Join('|',
            "URBANPARK",
            "ESTACION",
            p.TicketCodigo,
            p.SedeCodigo,
            p.SedeNombre,
            p.SedeDireccion,
            p.EspacioCodigo,
            p.Zona,
            p.Placa,
            p.Usuario,
            p.Entrada.ToString("O", CultureInfo.InvariantCulture),
            p.TarifaHora.ToString("0.00", CultureInfo.InvariantCulture),
            p.Descuento.ToString(CultureInfo.InvariantCulture),
            p.MontoAhora.ToString("0.00", CultureInfo.InvariantCulture),
            p.SedeLatitud.ToString("0.000000", CultureInfo.InvariantCulture),
            p.SedeLongitud.ToString("0.000000", CultureInfo.InvariantCulture),
            p.Modo,
            p.SalidaPrevista?.ToString("O", CultureInfo.InvariantCulture) ?? "");
}
