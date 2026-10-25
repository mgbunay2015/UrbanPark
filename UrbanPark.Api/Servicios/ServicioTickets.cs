using UrbanPark.Api.Modelos;
using UrbanPark.Api.Repositorios;

namespace UrbanPark.Api.Servicios;

/// <summary>
/// Reglas de tickets en un solo lugar, para que el flujo manual (TicketsController),
/// el flujo con IA (IaController) y la reconciliacion se comporten exactamente igual.
/// </summary>
public sealed class ServicioTickets
{
    private readonly TicketRepositorio _tickets;
    private readonly EspacioRepositorio _espacios;
    private readonly ServicioEspacios _servicioEspacios;
    private readonly VehiculoRepositorio _vehiculos;
    private readonly ILogger<ServicioTickets> _log;

    public ServicioTickets(TicketRepositorio tickets, EspacioRepositorio espacios, ServicioEspacios servicioEspacios,
        VehiculoRepositorio vehiculos, ILogger<ServicioTickets> log)
    {
        _tickets = tickets;
        _espacios = espacios;
        _servicioEspacios = servicioEspacios;
        _vehiculos = vehiculos;
        _log = log;
    }

    public sealed record Resultado(bool Exito, int Codigo, string? Mensaje, Ticket? Ticket);

    /// <summary>
    /// Apertura de ticket con el orden exacto del backend original:
    /// validar placa -> validar plaza -> conflicto por placa -> conflicto por plaza -> ocupar -> crear.
    /// Si se pasan inicioReserva/finReserva el ticket queda RESERVADO para esa franja (y el puesto
    /// se guarda desde ya, para que nadie más lo tome).
    /// </summary>
    public async Task<Resultado> AbrirAsync(string? placaCruda, long? espacioIdCrudo, string username,
        DateTime? inicioReserva = null, DateTime? finReserva = null)
    {
        var (valida, placa, error) = Reglas.ValidarPlaca(placaCruda, Reglas.MensajePlacaInvalidaTickets);
        if (!valida) return new Resultado(false, 400, error, null);

        if (espacioIdCrudo is null or <= 0)
            return new Resultado(false, 400, "Debes indicar una plaza válida", null);

        var esReserva = inicioReserva is not null;
        var entrada = inicioReserva ?? DateTime.Now;

        if (esReserva)
        {
            if (finReserva is null)
                return new Resultado(false, 400, "Para reservar indica la fecha y la hora de salida.", null);
            if (finReserva.Value <= entrada)
                return new Resultado(false, 400, "La hora de salida debe ser posterior a la de ingreso.", null);
            if (entrada < DateTime.Now.AddMinutes(-5))
                return new Resultado(false, 400, "La fecha de la reserva ya pasó. Elige una fecha y hora futuras.", null);
            if (entrada > DateTime.Now.AddDays(60))
                return new Resultado(false, 400, "Solo se puede reservar con 60 días de anticipación.", null);
        }

        var espacioId = espacioIdCrudo.Value;

        var abiertoMismaPlaca = await _tickets.AbiertoPorPlacaAsync(placa);
        if (abiertoMismaPlaca is not null)
            return new Resultado(false, 409,
                $"Ya existe un ticket abierto para la placa {placa} (ticket {abiertoMismaPlaca.Codigo}). Ciérralo antes de abrir otro.", null);

        var registrado = await _vehiculos.PorPlacaAsync(placa);
        if (registrado is not null && !registrado.Activo)
            return new Resultado(false, 409, $"El vehículo {placa} está de baja. Actívalo en Vehículos para volver a usarlo.", null);

        var abiertoMismaPlaza = await _tickets.AbiertoPorEspacioAsync(espacioId);
        if (abiertoMismaPlaza is not null)
            return new Resultado(false, 409,
                $"La plaza ya tiene un ticket abierto ({abiertoMismaPlaza.Codigo} · {abiertoMismaPlaza.Placa}). Elige otra plaza libre o cierra ese ticket.", null);

        ResultadoOcupar resultado;
        Espacio? espacio;
        try
        {
            (resultado, espacio) = await _servicioEspacios.OcuparAsync(espacioId);
        }
        catch (EspaciosNoDisponibleException ex)
        {
            return new Resultado(false, 503,
                "ms-espacios no disponible (Resilience/Feign). ¿Está caído el servicio? Detalle: " + ex.Message, null);
        }

        if (resultado == ResultadoOcupar.NoExiste)
            return new Resultado(false, 404, $"No existe la plaza ID {espacioId}. Revisa el ID en Plazas.", null);

        if (resultado == ResultadoOcupar.YaOcupada)
            return new Resultado(false, 409, $"La plaza ID {espacioId} ya está ocupada. Elige una plaza LIBRE en la pestaña Plazas.", null);

        var id = await _tickets.CrearAsync(placa, espacioId, espacio!.Codigo, espacio.Zona, entrada,
            espacio.TarifaHora, username, esReserva ? "RESERVA" : "AHORA", esReserva ? finReserva : null);

        return new Resultado(true, 200, null, await _tickets.PorIdAsync(id));
    }

    /// <summary>
    /// Cierre y cobro. El cobro nunca depende del servicio de espacios: si la liberacion falla,
    /// el ticket queda cerrado y marcado con liberacionPendiente.
    /// </summary>
    public async Task<Resultado> CerrarAsync(long id, string username, bool esClienteSolo)
    {
        var ticket = await _tickets.PorIdAsync(id);
        if (ticket is null) return new Resultado(false, 404, "Ticket no encontrado", null);

        if (esClienteSolo && !Pertenece(ticket, username))
            return new Resultado(false, 403, "Solo puedes ver o cerrar tus propios tickets", null);

        if (ticket.Estado == "CERRADO")
            return new Resultado(false, 409, "El ticket ya está cerrado", null);

        var salida = DateTime.Now;
        var descuentoPct = await DescuentoAsync(ticket.Placa);
        var bruto = Reglas.CalcularMonto(ticket.Entrada, salida, ticket.TarifaHora);
        var monto = Reglas.CalcularMonto(ticket.Entrada, salida, ticket.TarifaHora, descuentoPct);
        var descuento = Math.Round(Math.Max(0, bruto - monto), 2, MidpointRounding.AwayFromZero);

        var liberacionPendiente = false;
        try
        {
            await _servicioEspacios.LiberarAsync(ticket.EspacioId);
        }
        catch (EspaciosNoDisponibleException)
        {
            liberacionPendiente = true;
            _log.LogWarning("Ticket {Codigo} cerrado con plaza pendiente de liberar", ticket.Codigo);
        }

        await _tickets.CerrarAsync(id, salida, monto, descuento, liberacionPendiente);
        return new Resultado(true, 200, null, await _tickets.PorIdAsync(id));
    }

    private async Task<int> DescuentoAsync(string placa)
    {
        var vehiculo = await _vehiculos.PorPlacaAsync(placa);
        if (vehiculo is null) return 0;
        return Math.Max(vehiculo.Descuento, Reglas.DescuentoDe(vehiculo.Propietario));
    }

    /// <summary>
    /// Alinea el estado "ocupado" de las plazas con los tickets abiertos.
    /// Devuelve null en Respuesta cuando el servicio de espacios no responde (el llamante decide el 503).
    /// </summary>
    public async Task<(ReconciliarResponse? Respuesta, string? Error)> ReconciliarAsync()
    {
        var detalle = new List<string>();
        var abiertos = await _tickets.AbiertosAsync();

        // 1) cerrar duplicados por plaza (se conserva el mas antiguo)
        var duplicados = 0;
        foreach (var grupo in abiertos.GroupBy(t => t.EspacioId).Where(g => g.Count() > 1))
        {
            foreach (var sobrante in grupo.OrderBy(t => t.Entrada).Skip(1))
            {
                var pct = await DescuentoAsync(sobrante.Placa);
                var bruto = Reglas.CalcularMonto(sobrante.Entrada, DateTime.Now, sobrante.TarifaHora);
                var monto = Reglas.CalcularMonto(sobrante.Entrada, DateTime.Now, sobrante.TarifaHora, pct);
                var descuento = Math.Round(Math.Max(0, bruto - monto), 2, MidpointRounding.AwayFromZero);
                await _tickets.CerrarAsync(sobrante.Id, DateTime.Now, monto, descuento, sobrante.LiberacionPendiente);
                duplicados++;
                detalle.Add($"Ticket duplicado cerrado: {sobrante.Codigo} (plaza {sobrante.EspacioCodigo})");
            }
        }

        abiertos = await _tickets.AbiertosAsync();
        var plazasConTicket = abiertos.Select(t => t.EspacioId).ToHashSet();

        // 2) listar plazas
        List<Espacio> plazas;
        try
        {
            plazas = await _servicioEspacios.ListarAsync();
        }
        catch (EspaciosNoDisponibleException ex)
        {
            return (null, "No se pudieron listar plazas para reconciliar. " + ex.Message);
        }

        // 3) alinear el flag "ocupado"
        var ajustesOcupar = 0;
        var ajustesLiberar = 0;
        var sinCambio = 0;

        foreach (var plaza in plazas)
        {
            var debeEstarOcupada = plazasConTicket.Contains(plaza.Id);

            if (debeEstarOcupada && !plaza.Ocupado)
            {
                await _espacios.OcuparAsync(plaza.Id);
                ajustesOcupar++;
                detalle.Add($"Plaza {plaza.Codigo} marcada como ocupada (tenía ticket abierto).");
            }
            else if (!debeEstarOcupada && plaza.Ocupado)
            {
                await _espacios.LiberarAsync(plaza.Id);
                await _tickets.LimpiarPendientesDePlazaAsync(plaza.Id);
                ajustesLiberar++;
                detalle.Add($"Plaza {plaza.Codigo} liberada (no tenía ticket abierto).");
            }
            else
            {
                sinCambio++;
            }
        }

        // 4) recalcular ocupacion
        OcupacionDto? ocupacion = null;
        try
        {
            ocupacion = EspacioRepositorio.CalcularOcupacion(await _servicioEspacios.ListarAsync());
        }
        catch (EspaciosNoDisponibleException)
        {
            detalle.Add("No se pudo recalcular la ocupación.");
        }

        var cuadran = ocupacion is not null
                      && ocupacion.Ocupados == plazasConTicket.Count
                      && abiertos.Count == plazasConTicket.Count;

        return (new ReconciliarResponse(
            abiertos.Count, plazasConTicket.Count,
            ocupacion?.Ocupados ?? 0, ocupacion?.Libres ?? 0, ocupacion?.Total ?? 0, ocupacion?.PorcentajeGlobal ?? 0,
            ocupacion, ajustesOcupar, ajustesLiberar, duplicados, sinCambio, cuadran, detalle,
            cuadran ? "Las plazas y los tickets abiertos cuadran." : "Se aplicaron ajustes; revisa el detalle."), null);
    }

    public static bool Pertenece(Ticket t, string username) =>
        !string.IsNullOrWhiteSpace(t.Usuario) &&
        t.Usuario.Equals(username, StringComparison.OrdinalIgnoreCase);
}
