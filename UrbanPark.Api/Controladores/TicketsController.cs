using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrbanPark.Api.Modelos;
using UrbanPark.Api.Repositorios;
using UrbanPark.Api.Servicios;

namespace UrbanPark.Api.Controladores;

/// <summary>
/// Tickets de entrada/salida y facturacion (equivalente a ms-tickets, puerto 8083).
/// Incluye la demostracion de Circuit Breaker + Retry sobre el servicio de espacios.
/// </summary>
[Route("api/tickets")]
[Authorize(Roles = "CLIENTE,OPERADOR,ADMIN")]
public sealed class TicketsController : ControladorBase
{
    private readonly TicketRepositorio _tickets;
    private readonly EspacioRepositorio _espacios;
    private readonly ServicioEspacios _servicioEspacios;
    private readonly ServicioTickets _servicioTickets;
    private readonly ResilienciaService _resiliencia;
    private readonly EstadoDemoService _demo;
    private readonly ILogger<TicketsController> _log;

    public TicketsController(TicketRepositorio tickets, EspacioRepositorio espacios, ServicioEspacios servicioEspacios,
        ServicioTickets servicioTickets, ResilienciaService resiliencia, EstadoDemoService demo, ILogger<TicketsController> log)
    {
        _tickets = tickets;
        _espacios = espacios;
        _servicioEspacios = servicioEspacios;
        _servicioTickets = servicioTickets;
        _resiliencia = resiliencia;
        _demo = demo;
        _log = log;
    }

    private static TicketDto A(Ticket t) => new(t.Id, t.Codigo, t.Placa, t.EspacioId, t.EspacioCodigo, t.Zona,
        t.Entrada, t.Salida, t.Estado, t.TarifaHora, t.MontoTotal, t.Usuario, t.LiberacionPendiente, t.MontoDescuento,
        t.Modo, t.SalidaPrevista);

    private bool Pertenece(Ticket t) =>
        !string.IsNullOrWhiteSpace(t.Usuario) && t.Usuario.Equals(Username, StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ consultas
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var lista = EsClienteSolo ? await _tickets.ListarPorUsuarioAsync(Username) : await _tickets.ListarAsync();
        return Ok(lista.Select(A));
    }

    [HttpGet("abiertos")]
    public async Task<IActionResult> Abiertos()
    {
        var lista = EsClienteSolo ? await _tickets.AbiertosPorUsuarioAsync(Username) : await _tickets.AbiertosAsync();
        return Ok(lista.Select(A));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Obtener(long id)
    {
        var ticket = await _tickets.PorIdAsync(id);
        if (ticket is null) return NoEncontrado("Ticket no encontrado");
        if (EsClienteSolo && !Pertenece(ticket))
            return Prohibido("Solo puedes ver o cerrar tus propios tickets");

        return Ok(A(ticket));
    }

    // ------------------------------------------------------------------ Resilience: CB + Retry
    /// <summary>
    /// Unico endpoint protegido con Circuit Breaker + Retry. SIEMPRE responde 200:
    /// si el servicio de espacios falla, devuelve el fallback (igual que Resilience4j).
    /// </summary>
    [HttpGet("ocupacion")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> OcupacionRemota()
    {
        var resultado = await _resiliencia.EjecutarAsync(
            () => _servicioEspacios.OcupacionAsync(),
            _ => new OcupacionDto(0, 0, 0, 0, new Dictionary<string, OcupacionZonaDto>()));

        if (!resultado.Fallback)
        {
            var o = resultado.Valor;
            return Ok(new Dictionary<string, object?>
            {
                ["origen"] = "ms-espacios (live)",
                ["resilience"] = "Circuit Breaker + Retry OK",
                ["total"] = o.Total,
                ["ocupados"] = o.Ocupados,
                ["libres"] = o.Libres,
                ["porcentajeGlobal"] = o.PorcentajeGlobal,
                ["zonas"] = o.Zonas,
                ["ms"] = resultado.Milisegundos,
                ["estadoCircuito"] = _resiliencia.Estado
            });
        }

        return Ok(new Dictionary<string, object?>
        {
            ["fallback"] = true,
            ["origen"] = "ms-tickets fallback",
            ["mensaje"] = "Resilience4j activó fallback: " + resultado.Mensaje,
            ["total"] = 0,
            ["ocupados"] = 0,
            ["libres"] = 0,
            ["ms"] = resultado.Milisegundos,
            ["estadoCircuito"] = _resiliencia.Estado
        });
    }

    [HttpGet("resilience/estado")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult EstadoResiliencia() => Ok(new
    {
        estado = _resiliencia.Estado,
        llamadasEnVentana = _resiliencia.LlamadasEnVentana,
        fallosEnVentana = _resiliencia.FallosEnVentana,
        tasaFallo = _resiliencia.TasaFallo,
        transiciones = _resiliencia.Transiciones,
        espaciosCaido = _demo.EspaciosCaido,
        parametros = new
        {
            slidingWindowSize = _resiliencia.Ventana,
            failureRateThreshold = _resiliencia.UmbralPorcentaje,
            retryMaxAttempts = _resiliencia.IntentosMaximos
        }
    });

    // ------------------------------------------------------------------ demo de caida (antes: Docker)
    [HttpGet("demo/espacios/status")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult DemoEstado() => Ok(new DemoEstadoResponse(
        "ms-espacios",
        !_demo.EspaciosCaido,
        _demo.ContenedorId,
        _demo.EspaciosCaido ? "ms-espacios está DETENIDO (simulado)." : "ms-espacios está EN EJECUCIÓN."));

    [HttpPost("demo/espacios/stop")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult DemoDetener()
    {
        _demo.Detener();
        _log.LogWarning("Demo: servicio de espacios marcado como CAÍDO");
        return Ok(new DemoAccionResponse("stop", "ms-espacios", _demo.ContenedorId,
            "ms-espacios detenido. Prueba ahora Resilience (debería activar fallback).", true));
    }

    [HttpPost("demo/espacios/start")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult DemoIniciar()
    {
        _demo.Iniciar();
        _log.LogInformation("Demo: servicio de espacios marcado como ACTIVO");
        return Ok(new DemoAccionResponse("start", "ms-espacios", _demo.ContenedorId,
            "ms-espacios iniciado. Espera unos segundos y prueba de nuevo.", true));
    }

    // ------------------------------------------------------------------ reconciliacion
    [HttpPost("reconciliar-plazas")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Reconciliar()
    {
        var (respuesta, error) = await _servicioTickets.ReconciliarAsync();
        return respuesta is null ? NoDisponible(error ?? "No se pudieron reconciliar las plazas.") : Ok(respuesta);
    }

    // ------------------------------------------------------------------ sincronizacion de liberaciones pendientes
    [HttpPost("sincronizar-liberaciones")]
    public async Task<IActionResult> SincronizarLiberaciones()
    {
        var pendientes = await _tickets.PendientesLiberacionAsync();

        if (pendientes.Count == 0)
        {
            return Ok(new Dictionary<string, object?>
            {
                ["pendientes"] = 0,
                ["liberadas"] = 0,
                ["fallidas"] = 0,
                ["mensaje"] = "No hay liberaciones pendientes",
                ["detalle"] = Array.Empty<string>()
            });
        }

        var liberadas = 0;
        var fallidas = 0;
        var sinCambio = 0;
        var detalle = new List<string>();

        foreach (var ticket in pendientes)
        {
            var abiertoEnPlaza = await _tickets.AbiertoPorEspacioAsync(ticket.EspacioId);
            if (abiertoEnPlaza is not null)
            {
                await _tickets.MarcarPendienteAsync(ticket.Id, false);
                sinCambio++;
                detalle.Add($"Ticket {ticket.Codigo}: la plaza sigue con ticket abierto, se cancela el pendiente.");
                continue;
            }

            try
            {
                await _servicioEspacios.LiberarAsync(ticket.EspacioId);
                await _tickets.MarcarPendienteAsync(ticket.Id, false);
                liberadas++;
                detalle.Add($"Plaza del ticket {ticket.Codigo} liberada.");
            }
            catch (EspaciosNoDisponibleException)
            {
                fallidas++;
                detalle.Add($"Ticket {ticket.Codigo}: el servicio de espacios sigue caído.");
            }
        }

        var restantes = await _tickets.PendientesLiberacionAsync();

        string mensaje;
        if (liberadas > 0)
            mensaje = $"Sincronizado: {liberadas} plaza(s) marcada(s) como disponible(s)";
        else if (fallidas > 0)
            mensaje = "Aún no se pudo sincronizar (¿ms-espacios sigue caído?)";
        else
            mensaje = "Sin cambios";

        return Ok(new Dictionary<string, object?>
        {
            ["pendientesIniciales"] = pendientes.Count,
            ["liberadas"] = liberadas,
            ["fallidas"] = fallidas,
            ["sinCambio"] = sinCambio,
            ["pendientesRestantes"] = restantes.Count,
            ["mensaje"] = mensaje,
            ["detalle"] = detalle
        });
    }

    // ------------------------------------------------------------------ abrir ticket
    [HttpPost("abrir")]
    public async Task<IActionResult> Abrir([FromBody] AbrirTicketRequest peticion)
    {
        var resultado = await _servicioTickets.AbrirAsync(peticion.Placa, peticion.EspacioId, Username);
        return resultado.Exito ? Ok(A(resultado.Ticket!)) : Error(resultado.Codigo, resultado.Mensaje!);
    }

    // ------------------------------------------------------------------ cerrar y cobrar
    [HttpPost("{id:long}/cerrar")]
    public async Task<IActionResult> Cerrar(long id)
    {
        var resultado = await _servicioTickets.CerrarAsync(id, Username, EsClienteSolo);
        return resultado.Exito ? Ok(A(resultado.Ticket!)) : Error(resultado.Codigo, resultado.Mensaje!);
    }
}
