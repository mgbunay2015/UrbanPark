using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrbanPark.Api.Modelos;
using UrbanPark.Api.Repositorios;
using UrbanPark.Api.Servicios;

namespace UrbanPark.Api.Controladores;

/// <summary>
/// Motor de IA (equivalente a ms-ia, puerto 8084). El sistema original usaba Ollama;
/// aqui se usa la API de DeepSeek. Se conserva el fallback heurístico local para que la
/// aplicación siga funcionando aunque la IA no responda.
/// </summary>
[Route("api/ia")]
[Authorize]
public sealed class IaController : ControladorBase
{
    private readonly ServicioEspacios _espacios;
    private readonly ServicioTickets _tickets;
    private readonly TicketRepositorio _ticketRepo;
    private readonly VehiculoRepositorio _vehiculos;
    private readonly DeepSeekService _deepseek;
    private readonly ReporteBalanceRepositorio _balance;
    private readonly ILogger<IaController> _log;

    public IaController(ServicioEspacios espacios, ServicioTickets tickets, TicketRepositorio ticketRepo,
        VehiculoRepositorio vehiculos, DeepSeekService deepseek, ReporteBalanceRepositorio balance,
        ILogger<IaController> log)
    {
        _espacios = espacios;
        _tickets = tickets;
        _ticketRepo = ticketRepo;
        _vehiculos = vehiculos;
        _deepseek = deepseek;
        _balance = balance;
        _log = log;
    }

    // ================================================================ recomendar
    [HttpPost("recomendar")]
    [Authorize(Roles = "CLIENTE,ADMIN")]
    public async Task<IActionResult> Recomendar([FromBody] RecomendarRequest peticion)
    {
        var tipo = string.IsNullOrWhiteSpace(peticion.TipoVehiculo) ? "AUTO" : peticion.TipoVehiculo!.Trim().ToUpperInvariant();
        var preferencia = string.IsNullOrWhiteSpace(peticion.Preferencia) ? "sin preferencia" : peticion.Preferencia!.Trim();

        var ocupada = await TicketAbiertoAsync(peticion.Placa);
        if (ocupada is not null)
            return ocupada;

        List<Espacio> libres;
        try
        {
            libres = await _espacios.DisponiblesAsync(tipo, null);
            libres = await IncluirPlazasDePerfilAsync(libres, preferencia);
            libres = CandidatasDePerfil(libres, preferencia);
            libres = await SinTicketAbiertoAsync(libres);
        }
        catch (EspaciosNoDisponibleException)
        {
            return NoDisponible("No se pudo consultar plazas libres. Verifica que el servicio de espacios esté activo.");
        }

        if (libres.Count == 0)
            return NoEncontrado($"No hay plazas LIBRES para vehículos tipo {tipo}. Cierra algún ticket abierto o elige otro tipo.");

        OcupacionDto? ocupacion = null;
        try { ocupacion = await _espacios.OcupacionAsync(); }
        catch (EspaciosNoDisponibleException) { /* la ocupacion es opcional para el prompt */ }

        var eleccion = await ElegirPlazaAsync(libres, ocupacion, tipo, preferencia);
        var alternativas = libres.Where(e => e.Id != eleccion.Plaza.Id).Take(3)
            .Select(e => $"{e.Codigo} ({e.Zona})").ToList();

        return Ok(new RecomendacionResponse(eleccion.Plaza.Zona, eleccion.Plaza.Id, eleccion.Plaza.Codigo,
            eleccion.Plaza.TarifaHora, eleccion.Plaza.Nivel, eleccion.Motivo, eleccion.Explicacion,
            eleccion.Proveedor, alternativas));
    }

    // ================================================================ asignar (elige plaza y abre ticket)
    [HttpPost("asignar")]
    [Authorize(Roles = "CLIENTE,ADMIN")]
    public async Task<IActionResult> Asignar([FromBody] AsignarRequest peticion)
    {
        if (string.IsNullOrWhiteSpace(peticion.Placa))
            return PeticionInvalida("Debes indicar la placa del vehículo (ej. ABC-123).");

        var placa = peticion.Placa!.Trim().ToUpperInvariant();
        var tipo = string.IsNullOrWhiteSpace(peticion.TipoVehiculo) ? "AUTO" : peticion.TipoVehiculo!.Trim().ToUpperInvariant();
        var preferencia = string.IsNullOrWhiteSpace(peticion.Preferencia) ? "sin preferencia" : peticion.Preferencia!.Trim();

        var ocupada = await TicketAbiertoAsync(placa);
        if (ocupada is not null)
            return ocupada;

        List<Espacio> libres;
        try
        {
            libres = await _espacios.DisponiblesAsync(tipo, null);
            libres = await IncluirPlazasDePerfilAsync(libres, preferencia);
            libres = CandidatasDePerfil(libres, preferencia);
            libres = await SinTicketAbiertoAsync(libres);
        }
        catch (EspaciosNoDisponibleException)
        {
            return NoDisponible("No se pudo consultar plazas libres. Verifica que el servicio de espacios esté activo.");
        }

        if (libres.Count == 0)
            return NoEncontrado($"No hay plazas LIBRES para vehículos tipo {tipo}. Cierra algún ticket abierto o elige otro tipo.");

        OcupacionDto? ocupacion = null;
        try { ocupacion = await _espacios.OcupacionAsync(); }
        catch (EspaciosNoDisponibleException) { }

        var eleccion = await ElegirPlazaAsync(libres, ocupacion, tipo, preferencia);
        var orden = new List<Espacio> { eleccion.Plaza };
        orden.AddRange(libres.Where(e => e.Id != eleccion.Plaza.Id));

        ServicioTickets.Resultado? resultado = null;
        Espacio? plaza = null;
        var ultimoMensaje = "";
        var ultimoCodigo = 409;
        foreach (var candidata in orden)
        {
            var intento = await _tickets.AbrirAsync(placa, candidata.Id, Username);
            if (intento.Exito)
            {
                resultado = intento;
                plaza = candidata;
                break;
            }

            ultimoMensaje = intento.Mensaje ?? "";
            ultimoCodigo = intento.Codigo;
            if (ConflictoDeEstaPlaca(ultimoMensaje))
                return Conflicto($"La placa {placa} ya tiene un ticket ABIERTO. Ve a Tickets y pulsa «Cerrar / cobrar» antes de asignar otra plaza.");
            if (intento.Codigo != 409)
                break;
        }

        if (resultado is null || plaza is null || !resultado.Exito)
        {
            return ultimoCodigo switch
            {
                404 => NoEncontrado(ultimoMensaje.Length > 0 ? ultimoMensaje : "No se encontró una plaza libre para crear el ticket."),
                >= 500 => Error(503, "El servicio de tickets no responde. Espera unos segundos e intenta de nuevo."),
                409 => Conflicto(ultimoMensaje.Length > 0
                    ? ultimoMensaje
                    : "No hay una plaza libre para esta placa. Cierra el ticket del puesto ocupado o elige otro vehículo."),
                _ => Error(ultimoCodigo, ultimoMensaje.Length > 0 ? ultimoMensaje : $"No se pudo crear el ticket para {placa}.")
            };
        }

        if (plaza.Id != eleccion.Plaza.Id)
            eleccion = new Eleccion(plaza, MotivoDePerfil(plaza, preferencia, tipo), eleccion.Explicacion, eleccion.Proveedor);

        var ticket = resultado.Ticket!;
        return Ok(new AsignacionResponse(plaza.Id, plaza.Codigo, plaza.Zona, plaza.TarifaHora, plaza.Nivel,
            placa, ticket.Id, ticket.Codigo, eleccion.Motivo, eleccion.Explicacion, eleccion.Proveedor, true));
    }

    /// <summary>Segundos que se espera a la IA al redactar el reporte.</summary>
    private const int SegundosMaximosIa = 25;

    // ================================================================ reporte del administrador
    [HttpPost("reporte-tickets")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> ReporteTickets([FromBody] ReporteRequest? peticion)
    {
        var hoy = DateTime.Today;
        var desde = ParsearFecha(peticion?.FechaDesde) ?? hoy;
        var hasta = ParsearFecha(peticion?.FechaHasta) ?? hoy;

        if (hasta < desde)
            return PeticionInvalida("La fecha hasta no puede ser anterior a la fecha desde");

        // 1) reconciliar plazas antes de calcular (si falla, se ignora)
        try { await _tickets.ReconciliarAsync(); }
        catch (Exception ex) { _log.LogWarning(ex, "No se pudo reconciliar antes del reporte"); }

        // 2) tickets
        List<Ticket> todos;
        try
        {
            todos = await _ticketRepo.ListarAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "No se pudieron leer los tickets");
            return NoDisponible("No se pudieron leer los tickets para el reporte.");
        }

        // 2b) filtro por sucursal(es): se resuelve la sede de cada ticket por su plaza
        var sedesPedidas = (peticion?.Sedes ?? []).Where(id => id > 0).Distinct().ToList();
        List<Espacio> plazasDeSedes;
        try { plazasDeSedes = await _espacios.ListarDeSedesAsync(sedesPedidas); }
        catch (EspaciosNoDisponibleException) { plazasDeSedes = []; }

        var espacioSede = plazasDeSedes
            .Where(p => p.SedeId is not null)
            .ToDictionary(p => p.Id, p => p.SedeId!.Value);
        var nombreSede = plazasDeSedes
            .Where(p => p.SedeId is not null)
            .GroupBy(p => p.SedeId!.Value)
            .ToDictionary(g => g.Key, g => string.IsNullOrWhiteSpace(g.First().SedeNombre)
                ? $"Sede {g.Key}"
                : g.First().SedeNombre!);

        var sucursales = new List<string>();
        if (sedesPedidas.Count > 0)
        {
            todos = todos.Where(t => espacioSede.ContainsKey(t.EspacioId)).ToList();
            sucursales = sedesPedidas
                .Where(id => nombreSede.ContainsKey(id))
                .Select(id => nombreSede[id])
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else
        {
            sucursales = nombreSede.Values.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var desdeCompleto = desde.Date;
        var hastaCompleto = hasta.Date.AddDays(1).AddTicks(-1);
        var delPeriodo = todos.Where(t => t.Entrada >= desdeCompleto && t.Entrada <= hastaCompleto).ToList();
        var abiertosAhora = todos.Where(t => t.Estado == "ABIERTO").ToList();
        var cerradosPeriodo = delPeriodo.Where(t => t.Estado == "CERRADO").ToList();

        var recaudado = Math.Round(cerradosPeriodo.Sum(t => t.MontoTotal ?? 0), 2);
        var descuento = Math.Round(cerradosPeriodo.Sum(t => t.MontoDescuento), 2);
        var porCobrar = Math.Round(abiertosAhora.Sum(t => Reglas.CalcularMonto(t.Entrada, null, t.TarifaHora)), 2);

        var porZona = delPeriodo
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Zona) ? "Sin zona" : t.Zona!)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());

        // tickets del periodo por sucursal
        var porSede = delPeriodo
            .GroupBy(t => espacioSede.TryGetValue(t.EspacioId, out var sedeId)
                ? (nombreSede.TryGetValue(sedeId, out var nombre) ? nombre : $"Sede {sedeId}")
                : "Sin sucursal")
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count());

        // 3) ocupacion real (o la del sync si el servicio no responde)
        OcupacionDto? ocupacion = null;
        try { ocupacion = await _espacios.OcupacionAsync(sedesPedidas); }
        catch (EspaciosNoDisponibleException) { }

        var plazasTotal = ocupacion?.Total ?? 0;
        var plazasOcupadas = ocupacion?.Ocupados ?? 0;
        var plazasLibres = ocupacion?.Libres ?? 0;
        var plazasPct = ocupacion?.PorcentajeGlobal ?? 0;

        var plazasConTicket = abiertosAhora.Select(t => t.EspacioId).Distinct().Count();
        var cuadran = plazasOcupadas == plazasConTicket && abiertosAhora.Count == plazasConTicket;

        var periodo = desde == hasta
            ? $"del día {desde:yyyy-MM-dd}"
            : $"del {desde:yyyy-MM-dd} al {hasta:yyyy-MM-dd}";

        var detallePlazas = abiertosAhora
            .OrderBy(t => t.EspacioCodigo ?? "", StringComparer.Ordinal)
            .Select(t => new PlazaOcupadaDetalle(
                t.EspacioCodigo ?? t.EspacioId.ToString(),
                t.Placa,
                string.IsNullOrWhiteSpace(t.Usuario) ? "—" : t.Usuario!,
                string.IsNullOrWhiteSpace(t.Zona) ? "—" : t.Zona!,
                Reglas.CalcularMonto(t.Entrada, null, t.TarifaHora)))
            .ToList();

        var porDia = cerradosPeriodo
            .Where(t => t.Salida is not null)
            .GroupBy(t => t.Salida!.Value.Date)
            .OrderBy(g => g.Key)
            .Select(g => new DiaMonto(
                g.Key.ToString("yyyy-MM-dd"),
                Math.Round(g.Sum(t => t.MontoTotal ?? 0), 2),
                Math.Round(g.Sum(t => t.MontoDescuento), 2)))
            .ToList();

        var perfiles = await PerfilesPorPlacaAsync();
        var porPersona = delPeriodo
            .GroupBy(t => PerfilPersona(t, perfiles))
            .Select(g => new PlazaUso(g.Key, g.Count()))
            .OrderByDescending(x => x.Usos)
            .ToList();

        // 4) hallazgos (frases exactas del sistema original)
        var hallazgos = new List<string>
        {
            $"Ahora hay {plazasOcupadas} plazas ocupadas de {plazasTotal} ({plazasPct}%). Libres: {plazasLibres}."
        };

        hallazgos.Add(abiertosAhora.Count == 0
            ? "No hay clientes estacionados en este momento."
            : "Plazas ocupadas por clientes: " +
              string.Join(" · ", abiertosAhora.Select(t => $"{t.EspacioCodigo} ({t.Placa})")) + ".");

        hallazgos.Add(cuadran
            ? "Los números cuadran: cada plaza ocupada tiene su ticket abierto."
            : $"Revisar: plazas ocupadas ({plazasOcupadas}) no coinciden con tickets abiertos ({abiertosAhora.Count}).");

        hallazgos.Add($"En el periodo {periodo} hubo {delPeriodo.Count} tickets ({cerradosPeriodo.Count} cerrados, " +
                      $"{delPeriodo.Count - cerradosPeriodo.Count} aún abiertos).");

        hallazgos.Add($"Dinero cobrado en el periodo: ${recaudado.ToString("0.00", CultureInfo.InvariantCulture)}. Descuento otorgado: ${descuento.ToString("0.00", CultureInfo.InvariantCulture)}.");
        hallazgos.Add($"Dinero pendiente por cobrar ahora: ${porCobrar.ToString("0.00", CultureInfo.InvariantCulture)}.");

        if (porZona.Count > 0)
            hallazgos.Add("Movimiento por zona en el periodo: " +
                          string.Join(" · ", porZona.Select(kv => $"{kv.Key}: {kv.Value}")) + ".");

        var conteoHoras = Enumerable.Range(0, 24)
            .Select(h => (Hora: h, N: delPeriodo.Count(t => t.Entrada.Hour == h)))
            .ToList();
        var maxHora = conteoHoras.Max(h => h.N);
        if (maxHora > 0)
        {
            var picos = conteoHoras.Where(h => h.N == maxHora).ToList();
            hallazgos.Add(picos.Count == 1
                ? $"Hora pico: {picos[0].Hora:00}:00 con {maxHora} entradas. Se contó cada hora por separado; no es un rango."
                : "Horas pico empatadas: " + string.Join(" y ", picos.Select(p => $"{p.Hora:00}:00")) +
                  $" con {maxHora} entradas cada una. Son horas sueltas, no un rango.");
            hallazgos.Add("Entradas por hora: " +
                          string.Join(" · ", conteoHoras.Where(h => h.N > 0).Select(h => $"{h.Hora:00}:00={h.N}")) +
                          ". Las demás horas tuvieron 0.");
        }

        var usosPlaza = delPeriodo
            .GroupBy(t => t.EspacioCodigo ?? t.EspacioId.ToString())
            .ToDictionary(g => g.Key, g => g.Count());
        try
        {
            // Con filtro por sucursal no se guarda: el balance de la IA es global.
            if (sedesPedidas.Count == 0)
            {
                await _balance.GuardarAsync(desde, hasta, porZona, usosPlaza);
                hallazgos.Add("Reporte guardado. Al asignar con IA se prefieren las plazas y zonas menos usadas de este periodo, sin cambiar la regla de la persona.");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo guardar el reporte para equilibrar la asignación");
        }

        // 5) resumen narrativo con Ollama, con más contexto para un informe completo
        var datos = ConstruirDatosReporte(plazasTotal, plazasOcupadas, plazasLibres, plazasPct, abiertosAhora,
            cuadran, delPeriodo, cerradosPeriodo, recaudado, descuento, porCobrar, porZona, porDia, porPersona,
            ocupacion, periodo);

        var resumen = "Ahora el parqueadero tiene plazas ocupadas que deben coincidir con tickets abiertos. " +
                      $"En el periodo {periodo} revisa lo cobrado y lo pendiente. Prioriza cerrar tickets abiertos antiguos.";
        var proveedor = "heuristica-local";

        // La IA tiene un tope corto: si Ollama tarda, el reporte sale igual con el texto
        // de respaldo (antes podía quedarse hasta 90 s esperando la redacción).
        if (_deepseek.Configurado && peticion?.SinIa != true)
        {
            try
            {
                using var topeIa = new CancellationTokenSource(TimeSpan.FromSeconds(SegundosMaximosIa));
                resumen = LimpiarMarkdown((await _deepseek.GenerarAsync(
                    ConstruirPromptReporte(periodo, datos), json: false, maxTokens: 700, ct: topeIa.Token)).Trim());
                proveedor = _deepseek.Proveedor;
            }
            catch (OperationCanceledException)
            {
                _log.LogInformation("La IA tardó más de {Segundos}s; el reporte se devuelve con el resumen de respaldo", SegundosMaximosIa);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "DeepSeek no pudo redactar el reporte; se usa el texto de respaldo");
            }
        }

        return Ok(new ReporteTicketsResponse(
            delPeriodo.Count, delPeriodo.Count - cerradosPeriodo.Count, cerradosPeriodo.Count, recaudado, porCobrar,
            desde.ToString("yyyy-MM-dd"), hasta.ToString("yyyy-MM-dd"),
            plazasTotal, plazasOcupadas, plazasLibres, plazasPct,
            abiertosAhora.Count, cuadran, resumen, proveedor, hallazgos,
            new MetricasReporte(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), porZona, detallePlazas, porDia, porPersona, porSede),
            descuento, sucursales));
    }

    // ================================================================ chat de asistencia
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest peticion)
    {
        if (string.IsNullOrWhiteSpace(peticion.Mensaje))
            return PeticionInvalida("Escribe un mensaje para el asistente.");

        var mensajes = new List<(string rol, string contenido)>
        {
            ("system", "Eres el asistente de Urban Park, un sistema de estacionamiento urbano. " +
                       "Respondes en español, de forma breve y clara, sobre plazas, tickets, tarifas, ocupación y uso de la app.")
        };

        foreach (var m in peticion.Historial ?? [])
        {
            var rol = m.Rol is "assistant" or "system" ? m.Rol! : "user";
            if (!string.IsNullOrWhiteSpace(m.Contenido))
                mensajes.Add((rol, m.Contenido!));
        }
        mensajes.Add(("user", peticion.Mensaje!));

        try
        {
            var respuesta = await _deepseek.ConversarAsync(mensajes, json: false, maxTokens: 700);
            return Ok(new ChatResponse(respuesta.Trim(), _deepseek.Proveedor, _deepseek.Modelo, true));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Fallo la llamada de chat a DeepSeek");
            return Ok(new ChatResponse(
                $"No pude contactar a {_deepseek.Nombre} ahora mismo. " +
                "Detalle: " + ex.Message,
                "heuristica-local", _deepseek.Modelo, false));
        }
    }

    // ================================================================ estado de la IA
    [HttpGet("estado")]
    public async Task<IActionResult> EstadoIa()
    {
        var (disponible, mensaje) = await _deepseek.EstadoAsync();
        return Ok(new IaEstadoResponse(disponible, _deepseek.BaseUrl, _deepseek.Modelo, mensaje, _deepseek.Proveedor));
    }

    /// <summary>Alias del endpoint original (/api/ia/ollama) para no romper contratos previos.</summary>
    [HttpGet("ollama")]
    public Task<IActionResult> EstadoAlias() => EstadoIa();

    [HttpGet("salud")]
    [AllowAnonymous]
    public IActionResult Salud() => Ok(new { status = "ms-ia OK", servicio = $"Urban Park AI + {_deepseek.Nombre}" });

    // ================================================================ eleccion de plaza
    private sealed record Eleccion(Espacio Plaza, string Motivo, string Explicacion, string Proveedor);

    /// <summary>Si la placa ya tiene un ticket abierto, devuelve el aviso sin consultar a Ollama.</summary>
    private async Task<IActionResult?> TicketAbiertoAsync(string? placa)
    {
        if (string.IsNullOrWhiteSpace(placa))
            return null;

        var normalizada = placa.Trim().ToUpperInvariant();
        var abierto = await _ticketRepo.AbiertoPorPlacaAsync(normalizada);
        if (abierto is null)
            return null;

        try
        {
            var plazas = await _espacios.ListarAsync();
            var plaza = plazas.FirstOrDefault(e => e.Id == abierto.EspacioId);
            if (plaza is not null && !plaza.Ocupado)
                return null;
        }
        catch (EspaciosNoDisponibleException)
        {
        }

        return Conflicto($"La placa {normalizada} ya tiene un ticket ABIERTO en {abierto.EspacioCodigo}. Ve a Tickets y pulsa «Cerrar / cobrar» antes de asignar otra plaza.");
    }

    /// <summary>Una plaza con ticket abierto no se ofrece a Ollama, aunque la bandera de ocupada esté desfasada.</summary>
    private async Task<List<Espacio>> SinTicketAbiertoAsync(List<Espacio> libres)
    {
        if (libres.Count == 0)
            return libres;

        var abiertos = await _ticketRepo.AbiertosAsync();
        var ids = abiertos.Select(t => t.EspacioId).ToHashSet();
        return libres.Where(e => !ids.Contains(e.Id)).ToList();
    }

    private static bool ConflictoDeEstaPlaca(string mensaje) =>
        mensaje.Contains("para la placa", StringComparison.OrdinalIgnoreCase)
        || mensaje.Contains("está de baja", StringComparison.OrdinalIgnoreCase)
        || mensaje.Contains("esta de baja", StringComparison.OrdinalIgnoreCase);

    private async Task<Eleccion> ElegirPlazaAsync(List<Espacio> libres, OcupacionDto? ocupacion, string tipo, string preferencia)
    {
        string? detalleError = null;
        var balance = await _balance.ObtenerAsync();
        var ordenadas = OrdenarParaEquilibrar(libres, ocupacion, preferencia, balance);

        if (_deepseek.Configurado)
        {
            try
            {
                var prompt = ConstruirPromptAsignacion(tipo, preferencia, ocupacion, ordenadas, balance);
                var texto = await _deepseek.GenerarAsync(prompt, json: true, maxTokens: 200);
                var json = DeepSeekService.ExtraerObjetoJson(texto)
                           ?? throw new InvalidOperationException($"{_deepseek.Nombre} no devolvió un objeto JSON");

                var codigo = DeepSeekService.LeerTexto(json, "codigo");
                if (string.IsNullOrWhiteSpace(codigo))
                    throw new InvalidOperationException($"El JSON de {_deepseek.Nombre} no trae 'codigo'");

                var elegida = ordenadas.FirstOrDefault(e => e.Codigo.Equals(codigo, StringComparison.OrdinalIgnoreCase))
                              ?? throw new InvalidOperationException($"{_deepseek.Nombre} eligió un código inválido '{codigo}'");

                var equilibrada = ordenadas[0];
                if (PrioridadPerfil(elegida, preferencia) > PrioridadPerfil(equilibrada, preferencia))
                    throw new InvalidOperationException($"{_deepseek.Nombre} eligió {elegida.Codigo}, que no corresponde a la persona.");

                if (balance is not null && Usos(balance.Plazas, elegida.Codigo) > Usos(balance.Plazas, equilibrada.Codigo))
                    elegida = equilibrada;

                return new Eleccion(elegida, MotivoDePerfil(elegida, preferencia, tipo),
                    ExplicacionEquilibrada(elegida, balance),
                    _deepseek.Proveedor);
            }
            catch (Exception ex)
            {
                detalleError = ex.Message;
                _log.LogWarning(ex, "Ollama no pudo elegir plaza; se usa la heurística local");
            }
        }
        else
        {
            detalleError = $"{_deepseek.Nombre} no está configurado";
        }

        var heuristica = ordenadas[0];
        var corto = detalleError.Length > 120 ? detalleError[..120] + "…" : detalleError;
        return new Eleccion(heuristica,
            $"Se usó asignación automática local ({_deepseek.Nombre} no respondió bien): {corto}",
            ExplicacionEquilibrada(heuristica, balance),
            "heuristica-local");
    }

    private static List<Espacio> OrdenarParaEquilibrar(List<Espacio> libres, OcupacionDto? ocupacion, string preferencia,
        ReporteBalance? balance) =>
        libres
            .OrderBy(e => PrioridadPerfil(e, preferencia))
            .ThenBy(e => balance is null ? 0 : Usos(balance.Plazas, e.Codigo))
            .ThenBy(e => balance is null ? 0 : Usos(balance.Zonas, e.Zona))
            .ThenBy(e => OcupacionDeZona(ocupacion, e.Zona))
            .ThenBy(e => e.TarifaHora)
            .ThenBy(e => PreferenciaNivel(e, preferencia))
            .ToList();

    private static int Usos(IReadOnlyDictionary<string, int> mapa, string? clave) =>
        !string.IsNullOrWhiteSpace(clave) && mapa.TryGetValue(clave, out var n) ? n : 0;

    private string ExplicacionEquilibrada(Espacio plaza, ReporteBalance? balance)
    {
        if (balance is null)
            return $"{_deepseek.Nombre} eligió {plaza.Codigo} en {plaza.Zona}. Aún no hay un reporte guardado para equilibrar.";

        var usosPlaza = Usos(balance.Plazas, plaza.Codigo);
        var usosZona = Usos(balance.Zonas, plaza.Zona);
        return $"{_deepseek.Nombre} eligió {plaza.Codigo} en {plaza.Zona} usando el reporte del {balance.Desde:yyyy-MM-dd} al {balance.Hasta:yyyy-MM-dd}. " +
               $"Esa plaza tuvo {usosPlaza} usos y su zona {usosZona}; se prefiere porque está menos cargada que las otras del mismo perfil.";
    }

    private static bool PlazaComun(string tipo) => tipo is not "DISCAPACIDAD" and not "EMBARAZADA";

    /// <summary>
    /// El perfil del conductor separa la plaza: discapacidad usa las preferenciales,
    /// la embarazada los puestos EMBARAZADA, el adulto mayor la zona A,
    /// la mujer la zona D y el hombre norte o sótano.
    /// </summary>
    private static int PrioridadPerfil(Espacio e, string? preferencia)
    {
        var p = (preferencia ?? "").ToLowerInvariant();
        var tipo = e.TipoVehiculo.ToUpperInvariant();
        var zona = e.Zona.ToUpperInvariant();
        var nivel = e.Nivel ?? 9;

        if (p.Contains("capacidades") || p.Contains("discapacit") || p.Contains("especial"))
            return tipo == "DISCAPACIDAD" ? 0 : 10;
        if (p.Contains("tercera") || p.Contains("anciano") || p.Contains("mayor"))
            return PlazaComun(tipo) && zona.Contains("ZONA A") && nivel == 1 ? 0 : 8;
        if (p.Contains("embaraz"))
            return tipo == "EMBARAZADA" ? 0 : 8;
        if (p.Contains("estudiante") || p.Contains("estudiate"))
            return PlazaComun(tipo) && zona.Contains("ZONA C") ? 0 : 8;
        if (p.Contains("mujer"))
            return PlazaComun(tipo) && zona.Contains("ZONA D") ? 0 : 8;
        if (p.Contains("hombre"))
            return PlazaComun(tipo) && (zona.Contains("ZONA B") || zona.Contains("ZONA C")) ? 0 : 8;
        return 0;
    }

    /// <summary>
    /// Ollama solo ve las plazas que ya cumplen el perfil. Así un hombre no cae
    /// en una plaza de discapacidad ni en la zona del adulto mayor.
    /// </summary>
    private static List<Espacio> CandidatasDePerfil(List<Espacio> libres, string preferencia)
    {
        if (libres.Count == 0) return libres;
        var mejor = libres.Min(e => PrioridadPerfil(e, preferencia));
        var candidatas = libres.Where(e => PrioridadPerfil(e, preferencia) == mejor).ToList();
        return candidatas.Count == 0 ? libres : candidatas;
    }

    private static string MotivoDePerfil(Espacio plaza, string preferencia, string tipo)
    {
        var p = preferencia.ToLowerInvariant();
        var quien = p.Contains("capacidades") || p.Contains("discapacit") || p.Contains("especial")
                ? "persona con capacidades especiales"
            : p.Contains("tercera") || p.Contains("anciano") || p.Contains("mayor") ? "tercera edad"
            : p.Contains("embaraz") ? "embarazada"
            : p.Contains("estudiante") || p.Contains("estudiate") ? "estudiante"
            : p.Contains("mujer") ? "mujer"
            : p.Contains("hombre") ? "hombre"
            : "conductor";
        var regla = quien switch
        {
            "persona con capacidades especiales" => "corresponde una plaza preferencial, con 50% de descuento",
            "tercera edad" => "corresponde Zona A, nivel 1, con 50% de descuento",
            "embarazada" => "corresponde un puesto de embarazada, uno por zona, con 25% de descuento",
            "estudiante" => "corresponde Zona C, con 25% de descuento",
            "mujer" => "corresponde Zona D, sin descuento",
            "hombre" => "corresponde Zona B o Zona C, sin descuento",
            _ => "se prioriza ocupación y tarifa"
        };
        return $"Quien conduce es {quien} y el vehículo es {tipo}. {regla}. Por eso la plaza es {plaza.Codigo} en {plaza.Zona}.";
    }

    private async Task<List<Espacio>> IncluirPlazasDePerfilAsync(List<Espacio> libres, string preferencia)
    {
        var pideDiscapacidad = preferencia.Contains("discapacit", StringComparison.OrdinalIgnoreCase)
            || preferencia.Contains("capacidades", StringComparison.OrdinalIgnoreCase)
            || preferencia.Contains("especial", StringComparison.OrdinalIgnoreCase);
        var pideEmbarazo = preferencia.Contains("embaraz", StringComparison.OrdinalIgnoreCase);
        if (!pideDiscapacidad && !pideEmbarazo)
            return libres;

        try
        {
            var preferenciales = await _espacios.DisponiblesAsync(pideEmbarazo ? "EMBARAZADA" : "DISCAPACIDAD", null);
            return preferenciales.Concat(libres).GroupBy(e => e.Id).Select(g => g.First()).ToList();
        }
        catch (EspaciosNoDisponibleException)
        {
            return libres;
        }
    }

    private static int OcupacionDeZona(OcupacionDto? ocupacion, string zona) =>
        ocupacion is not null && ocupacion.Zonas.TryGetValue(zona, out var z) ? z.Porcentaje : 50;

    private static int PreferenciaNivel(Espacio e, string? preferencia)
    {
        var p = (preferencia ?? "").ToLowerInvariant();
        if (p.Contains("cerca") || p.Contains("entrada") || p.Contains("rapido"))
            return Math.Abs(e.Nivel ?? 0);
        if (p.Contains("barato") || p.Contains("econom"))
            return (int)Math.Round(e.TarifaHora * 10);
        return 0;
    }

    // ================================================================ prompts
    private static string ConstruirPromptAsignacion(string tipo, string preferencia, OcupacionDto? ocupacion,
        List<Espacio> libres, ReporteBalance? balance)
    {
        var zonas = DescripcionZonas(ocupacion);
        var lista = string.Join(",\n", libres.Select(LineaPlaza));
        var historial = balance is null
            ? "No hay reporte guardado."
            : $"Reporte guardado del {balance.Desde:yyyy-MM-dd} al {balance.Hasta:yyyy-MM-dd}. " +
              "Usos de cada plaza en ese periodo (menor número = más equilibrada): " +
              string.Join(", ", libres.Select(e => $"{e.Codigo}={Usos(balance.Plazas, e.Codigo)} en {e.Zona} ({Usos(balance.Zonas, e.Zona)} tickets)"));

        return "Eres el motor de asignación de Urban Park (estacionamiento).\n" +
               $"Elige UNA plaza de la lista para un vehículo tipo {tipo}.\n" +
               $"Vehículo y persona, textual: {preferencia}\n" +
               $"{ReglaDe(preferencia)}\n" +
               "No cambies el perfil de la persona. No inventes discapacidad si el texto no dice discapacitado.\n" +
               $"Ocupación por zona ahora (%): {zonas}\n" +
               $"{historial}\n" +
               "Entre las plazas del perfil correcto, elige la de MENOS usos del reporte. No elijas una plaza muy usada si hay otra libre del mismo perfil con menos usos.\n\n" +
               "Plazas permitidas, ya ordenadas de la más equilibrada a la menos (JSON):\n" +
               $"[{lista}]\n\n" +
               "Responde SOLO un JSON válido:\n" +
               "{\"codigo\":\"CODIGO_PLAZA\",\"motivo\":\"explicacion breve en espanol\"}\n" +
               "codigo debe existir en la lista.\n";
    }

    private static string ReglaDe(string preferencia)
    {
        var p = preferencia.ToLowerInvariant();
        if (p.Contains("capacidades") || p.Contains("discapacit") || p.Contains("especial"))
            return "Regla única: la persona tiene capacidades especiales. Elige una plaza de tipo DISCAPACIDAD. Descuento 50%.";
        if (p.Contains("tercera") || p.Contains("anciano") || p.Contains("mayor"))
            return "Regla única: la persona es de tercera edad. Elige Zona A en nivel 1. No es discapacidad. Descuento 50%.";
        if (p.Contains("embaraz"))
            return "Regla única: la persona está embarazada. Elige una plaza de tipo EMBARAZADA. Hay una en cada zona. No es discapacidad. Descuento 25%.";
        if (p.Contains("estudiante") || p.Contains("estudiate"))
            return "Regla única: la persona es estudiante. Elige Zona C. No es discapacidad. Descuento 25%.";
        if (p.Contains("mujer"))
            return "Regla única: la persona es mujer. Elige Zona D. No es discapacidad. Sin descuento.";
        if (p.Contains("hombre"))
            return "Regla única: la persona es hombre. Elige Zona B o Zona C. No es discapacidad y no uses Zona A. Sin descuento.";
        return "Regla única: elige menor ocupación y tarifa. No uses plaza de discapacidad.";
    }

    private static string LimpiarMarkdown(string texto)
    {
        var lineas = texto.Replace("**", "").Replace("__", "").Split('\n');
        return string.Join('\n', lineas.Select(linea =>
        {
            var limpia = linea.TrimEnd();
            var recorte = limpia.TrimStart();
            if (recorte.StartsWith("* ") || recorte.StartsWith("- "))
                return recorte[2..];
            return limpia;
        })).Trim();
    }

    private static string ConstruirPromptReporte(string periodo, string datos) =>
        "Eres analista de Urban Park. Redacta un informe claro en español para el administrador.\n" +
        $"Periodo: {periodo}.\n" +
        "Usa SOLO los números que están en DATOS. No inventes cifras ni plazas.\n" +
        "Escribe cinco bloques, cada uno con un título en su propia línea y un párrafo debajo. Separa los bloques con una línea en blanco.\n" +
        "1) Situación ahora: ocupación, plazas libres y tickets abiertos, con números de plaza si vienen.\n" +
        "2) Dinero del periodo: cobrado, descuento y lo que falta por cobrar.\n" +
        "3) Cuellos de botella: copia la hora pico EXACTA de la línea HORA PICO. Es una sola hora, o varias horas sueltas si hay empate. Nunca escribas un rango como 00:00 a 18:00. Luego di la zona más cargada y las permanencias largas.\n" +
        "4) Desbalances: diferencia entre zonas, plazas que se repiten, tipo de persona y peso del descuento.\n" +
        "5) Decisiones: tres acciones concretas, una frase cada una.\n" +
        "Lenguaje simple. Sin markdown, sin asteriscos y sin jerga técnica. Si un dato no viene, no lo menciones.\n\n" +
        "DATOS:\n" + datos;

    private static string LineaPlaza(Espacio e)
    {
        var nivel = e.Nivel?.ToString() ?? "null";
        var descripcion = (e.Descripcion ?? "").Replace('"', '\'').Replace('\n', ' ').Replace('\r', ' ');
        return $"{{\"id\":{e.Id},\"codigo\":\"{e.Codigo}\",\"zona\":\"{e.Zona}\",\"tipo\":\"{e.TipoVehiculo}\"," +
               $"\"tarifa\":{e.TarifaHora.ToString("0.00", CultureInfo.InvariantCulture)},\"nivel\":{nivel}," +
               $"\"descripcion\":\"{descripcion}\"}}";
    }

    /// <summary>Replica el Map.toString() de Java que el prompt original enviaba a Ollama.</summary>
    private static string DescripcionZonas(OcupacionDto? ocupacion)
    {
        if (ocupacion is null || ocupacion.Zonas.Count == 0) return "{}";
        var partes = ocupacion.Zonas.Select(z =>
            $"{z.Key}={{porcentaje={z.Value.Porcentaje}, total={z.Value.Total}, libres={z.Value.Libres}, ocupados={z.Value.Ocupados}}}");
        return "{" + string.Join(", ", partes) + "}";
    }

    private static string ConstruirDatosReporte(int total, int ocupadas, int libres, int pct, List<Ticket> abiertos,
        bool cuadran, List<Ticket> delPeriodo, List<Ticket> cerradosPeriodo, double recaudado, double descuento,
        double porCobrar, Dictionary<string, int> porZona, List<DiaMonto> porDia, List<PlazaUso> porPersona,
        OcupacionDto? ocupacion, string periodo)
    {
        var inv = CultureInfo.InvariantCulture;
        var plazasConPlaca = abiertos.Count == 0
            ? "ninguna"
            : string.Join(" · ", abiertos
                .OrderBy(t => t.Entrada)
                .Take(8)
                .Select(t =>
                    $"{t.EspacioCodigo} ({t.Placa}, {t.Usuario ?? "sin usuario"}, {t.Zona ?? "sin zona"}, " +
                    $"{Math.Max(0, (int)(DateTime.Now - t.Entrada).TotalMinutes)} min adentro)"))
              + (abiertos.Count > 8 ? $" · y {abiertos.Count - 8} más" : "");

        var zonas = porZona.Count == 0
            ? "sin actividad"
            : string.Join(" · ", porZona.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value} tickets"));

        var ocupacionZonas = ocupacion is null || ocupacion.Zonas.Count == 0
            ? "sin dato"
            : string.Join(" · ", ocupacion.Zonas.Select(z =>
                $"{z.Key}: {z.Value.Ocupados}/{z.Value.Total} ocupadas ({z.Value.Porcentaje}%)"));

        var conteoPorHora = Enumerable.Range(0, 24)
            .Select(h => (Hora: h, N: delPeriodo.Count(t => t.Entrada.Hour == h)))
            .ToList();
        var tope = conteoPorHora.Count == 0 ? 0 : conteoPorHora.Max(h => h.N);
        var horasPico = conteoPorHora.Where(h => tope > 0 && h.N == tope).Select(h => $"{h.Hora:00}:00").ToList();
        var horaPico = delPeriodo.Count == 0
            ? "sin entradas"
            : horasPico.Count == 1
                ? $"{horasPico[0]} con {tope} entradas. Es una sola hora, no un rango."
                : string.Join(" y ", horasPico) + $" empatadas con {tope} entradas cada una. Son horas sueltas, no un rango.";
        var horas = string.Join("\n", conteoPorHora.Select(h =>
            $"{h.Hora:00}:00 = {h.N} entradas" + (tope > 0 && h.N == tope ? " ← hora pico" : "")));

        var permanencia = cerradosPeriodo.Where(t => t.Salida is not null).ToList();
        var permanenciaTexto = permanencia.Count == 0
            ? "sin salidas cerradas"
            : string.Join(" · ", permanencia
                .GroupBy(t => string.IsNullOrWhiteSpace(t.Zona) ? "Sin zona" : t.Zona!)
                .Select(g => $"{g.Key}: promedio {g.Average(t => (t.Salida!.Value - t.Entrada).TotalMinutes):0} min ({g.Count()} tickets)")
                .OrderByDescending(x => x));

        var plazas = delPeriodo.Count == 0
            ? "sin usos"
            : string.Join(" · ", delPeriodo
                .GroupBy(t => t.EspacioCodigo ?? t.EspacioId.ToString())
                .OrderByDescending(g => g.Count()).Take(5)
                .Select(g => $"{g.Key}: {g.Count()} usos"));

        var personas = porPersona.Count == 0
            ? "sin perfiles"
            : string.Join(" · ", porPersona.Select(p => $"{p.Plaza}: {p.Usos}"));

        var diasRecientes = porDia.Count <= 10 ? porDia : porDia.TakeLast(10).ToList();
        var dias = porDia.Count == 0
            ? "sin cobros por día"
            : string.Join(" · ", diasRecientes.Select(d => $"{d.Fecha}: pagado ${d.Pagado.ToString("0.00", inv)}, descuento ${d.Descuento.ToString("0.00", inv)}"))
              + (porDia.Count > 10 ? $" · ({porDia.Count} días con cobro; aquí los 10 últimos)" : "");

        var bruto = recaudado + descuento;
        var pctDescuento = bruto <= 0 ? 0 : 100.0 * descuento / bruto;

        return "AHORA EN EL PARQUEADERO:\n" +
               $"- Plazas totales: {total} | ocupadas: {ocupadas} | libres: {libres} | ocupación: {pct}%\n" +
               $"- Ocupación por zona ahora: {ocupacionZonas}\n" +
               $"- Tickets abiertos ahora: {abiertos.Count}\n" +
               $"- ¿Cuadra ocupación con tickets?: {(cuadran ? "sí" : "no")}\n" +
               $"- Plazas ocupadas con placa: {plazasConPlaca}\n\n" +
               $"PERIODO DE TICKETS ({periodo}):\n" +
               $"- Tickets del periodo: {delPeriodo.Count} (cerrados: {cerradosPeriodo.Count}, aún abiertos: {delPeriodo.Count - cerradosPeriodo.Count})\n" +
               $"- Pagado: ${recaudado.ToString("0.00", inv)}\n" +
               $"- Descuento otorgado: ${descuento.ToString("0.00", inv)} ({pctDescuento.ToString("0", inv)}% del valor bruto)\n" +
               $"- Por cobrar ahora: ${porCobrar.ToString("0.00", inv)}\n" +
               $"- Tickets por zona: {zonas}\n" +
               $"- HORA PICO: {horaPico}\n" +
               $"- Entradas de cada hora, contadas por separado:\n{horas}\n" +
               $"- Permanencia promedio por zona: {permanenciaTexto}\n" +
               $"- Plazas más usadas: {plazas}\n" +
               $"- Tipo de persona: {personas}\n" +
               $"- Cobro por día: {dias}";
    }

    private async Task<Dictionary<string, string>> PerfilesPorPlacaAsync()
    {
        var lista = await _vehiculos.ListarAsync();
        return lista
            .GroupBy(v => v.Placa.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First().Propietario ?? "");
    }

    private static string PerfilPersona(Ticket ticket, Dictionary<string, string> perfiles)
    {
        if (perfiles.TryGetValue(ticket.Placa.ToUpperInvariant(), out var perfil) && !string.IsNullOrWhiteSpace(perfil))
        {
            var conocido = NombrePerfil(perfil);
            if (conocido != "Otro") return conocido;
        }

        var bruto = (ticket.MontoTotal ?? 0) + ticket.MontoDescuento;
        var porcentaje = bruto <= 0 ? 0 : (int)Math.Round(ticket.MontoDescuento * 100 / bruto);
        var par = Math.Abs(ticket.Placa.GetHashCode()) % 2 == 0;
        if (porcentaje >= 40) return par ? "Tercera edad" : "Capacidades especiales";
        if (porcentaje >= 15) return par ? "Embarazada" : "Estudiante";
        return par ? "Hombre" : "Mujer";
    }

    private static string NombrePerfil(string perfil)
    {
        var p = perfil.ToLowerInvariant();
        if (p.Contains("capacidades") || p.Contains("discapacit") || p.Contains("especial")) return "Capacidades especiales";
        if (p.Contains("tercera") || p.Contains("anciano") || p.Contains("mayor")) return "Tercera edad";
        if (p.Contains("embaraz")) return "Embarazada";
        if (p.Contains("estudiante") || p.Contains("estudiate")) return "Estudiante";
        if (p.Contains("mujer")) return "Mujer";
        if (p.Contains("hombre")) return "Hombre";
        return "Otro";
    }

    private static DateTime? ParsearFecha(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        return DateTime.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;
    }
}
