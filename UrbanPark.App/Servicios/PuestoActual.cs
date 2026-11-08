using UrbanPark.App.Modelos;

namespace UrbanPark.App.Servicios;

/// <summary>
/// El puesto ocupado o la reserva vigente del usuario. Es la única fuente de verdad para
/// saber si el cliente "ya tiene parqueadero": lo consultan la pestaña Mapa (que en ese caso
/// muestra su parqueadero en lugar del mapa), la pestaña QR y las pantallas de pago.
/// </summary>
public static class PuestoActual
{
    /// <summary>Pase del puesto vigente, o null si el cliente no tiene nada reservado ni ocupado.</summary>
    public static async Task<PasePuestoDto?> CargarAsync()
    {
        try
        {
            var abiertos = await ApiCliente.TicketsAbiertosAsync() ?? [];

            // Con el rol de cliente el API ya devuelve solo los suyos; con OPERADOR/ADMIN
            // se filtra por el usuario de la sesión y, si no hay, se usa el ticket suelto.
            var mias = abiertos
                .Where(t => string.Equals(t.Usuario, Sesion.Username, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Un puesto ya ocupado manda sobre una reserva para más tarde.
            var ticket = mias.FirstOrDefault(t => !t.EsReserva)
                         ?? mias.FirstOrDefault()
                         ?? abiertos.FirstOrDefault(t => string.IsNullOrWhiteSpace(t.Usuario));

            return ticket is null ? null : await PorTicketAsync(ticket);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Datos completos del pase; sin conexión se arman con el ticket y el monto local.</summary>
    public static async Task<PasePuestoDto> PorTicketAsync(TicketDto ticket)
    {
        try
        {
            var pase = await ApiCliente.PaseAsync(ticket.Id);
            if (pase is not null) return pase;
        }
        catch (Exception)
        {
            // sin conexión se muestran los datos del ticket y el monto se calcula en el equipo
        }

        return new PasePuestoDto
        {
            TicketId = ticket.Id,
            TicketCodigo = ticket.Codigo,
            Placa = ticket.Placa,
            EspacioId = ticket.EspacioId,
            EspacioCodigo = ticket.EspacioCodigo ?? "",
            Zona = ticket.Zona ?? "",
            SedeNombre = ticket.Sede ?? "",
            TarifaHora = ticket.TarifaHora ?? 0,
            Entrada = ticket.Entrada,
            Estado = ticket.Estado,
            Usuario = ticket.Usuario ?? "",
            Modo = ticket.Modo,
            SalidaPrevista = ticket.SalidaPrevista
        };
    }

    /// <summary>Sede (parqueadero) del pase, con dirección y coordenadas.</summary>
    public static async Task<SedeDto?> SedeAsync(PasePuestoDto pase)
    {
        if (pase.SedeId <= 0) return null;

        try
        {
            var detalle = await ApiCliente.SedeAsync(pase.SedeId);
            return detalle?.Sede;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>True si el pase ya está dentro del parqueadero (y no es una reserva futura).</summary>
    public static bool EsReserva(PasePuestoDto pase) =>
        string.Equals(pase.Estado, "RESERVADO", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(pase.Modo, "RESERVA", StringComparison.OrdinalIgnoreCase);
}
