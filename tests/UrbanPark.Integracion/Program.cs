using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using UrbanPark.Api.Controladores;
using UrbanPark.Api.Data;
using UrbanPark.Api.Modelos;
using UrbanPark.Api.Repositorios;
using UrbanPark.Api.Servicios;

// ============================================================================
//  PRUEBA DE INTEGRACIÓN REAL CONTRA MySQL
//
//  Ejercita las MISMAS clases que usa la API (repositorios, servicios y los
//  controladores de Tickets e IA) contra una base MySQL de verdad:
//    inicialización del esquema y semillas, login, plazas, vehículos,
//    apertura y cierre de ticket con cobro, conflictos, reconciliación,
//    liberaciones pendientes, Circuit Breaker y el motor de IA.
//
//  Requisito: MySQL escuchando en localhost:3306 con la base "urbanpark".
//  Ejecutar:  dotnet run --project tests/UrbanPark.Integracion
// ============================================================================

var fallos = 0;
var pruebas = 0;

void Comprobar(string nombre, bool condicion, string? detalle = null)
{
    pruebas++;
    if (condicion) Console.WriteLine($"  OK    {nombre}");
    else { fallos++; Console.WriteLine($"  FALLA {nombre}" + (detalle is null ? "" : $"  -> {detalle}")); }
}

Console.WriteLine("=============================================================");
Console.WriteLine(" PRUEBA DE INTEGRACIÓN CONTRA MySQL - URBAN PARK");
Console.WriteLine("=============================================================\n");

var rutaAppSettings = new[]
{
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "UrbanPark.Api", "appsettings.json"),
    @"C:\moviles\Parqueadero\UrbanPark.Api\appsettings.json"
}.Select(Path.GetFullPath).FirstOrDefault(File.Exists);

if (rutaAppSettings is null)
{
    Console.WriteLine("No se encontró UrbanPark.Api/appsettings.json. Abortando.");
    return 2;
}

var configuracion = new ConfigurationBuilder().AddJsonFile(rutaAppSettings, optional: false).Build();
var cadena = configuracion.GetConnectionString("UrbanPark")!;
Console.WriteLine($"[config] appsettings.json leído: {rutaAppSettings}");
Console.WriteLine($"[config] conexión: {System.Text.RegularExpressions.Regex.Replace(cadena, "Password=[^;]*", "Password=***")}\n");

var fabrica = new FabricaConexion(configuracion);

// ---------------------------------------------------------------- 1) esquema y semillas
Console.WriteLine("[1] Inicialización de la base (esquema + datos semilla, idempotente)");
try
{
    await InicializadorDb.InicializarAsync(configuracion, NullLogger.Instance);
    await InicializadorDb.InicializarAsync(configuracion, NullLogger.Instance);
    Comprobar("InicializadorDb se ejecuta dos veces sin fallar ni duplicar", true);
}
catch (Exception ex)
{
    Comprobar("InicializadorDb se ejecuta dos veces sin fallar ni duplicar", false, ex.Message);
}

var usuarios = new UsuarioRepositorio(fabrica);
var espacios = new EspacioRepositorio(fabrica);
var vehiculos = new VehiculoRepositorio(fabrica);
var tickets = new TicketRepositorio(fabrica);
var password = new PasswordService();
var demo = new EstadoDemoService(configuracion);
var resiliencia = new ResilienciaService(configuracion);
var servicioEspacios = new ServicioEspacios(espacios, demo);
var servicioTickets = new ServicioTickets(tickets, espacios, servicioEspacios, vehiculos, NullLogger<ServicioTickets>.Instance);
var deepSeek = new DeepSeekService(new HttpClient(), configuracion);

Comprobar("hay al menos 5 usuarios demo", (await usuarios.ListarAsync()).Count >= 5);
Comprobar("hay 768 plazas (12 sedes x 64)", (await espacios.ListarAsync()).Count == 768);
Comprobar("hay al menos 4 vehículos", (await vehiculos.ListarAsync()).Count >= 4);

// ---------------------------------------------------------------- 2) login real
Console.WriteLine("\n[2] Login contra los usuarios del script SQL");
var cliente1 = await usuarios.PorUsernameAsync("cliente1");
Comprobar("cliente1 existe en MySQL", cliente1 is not null);
Comprobar("cliente1 / cliente123 valida con el hash de la base",
    cliente1 is not null && password.Verificar("cliente123", cliente1.PasswordHash));
Comprobar("cliente1 con contraseña incorrecta NO valida",
    cliente1 is not null && !password.Verificar("incorrecta", cliente1.PasswordHash));
Comprobar("cliente1 tiene rol CLIENTE (rol principal)", cliente1!.RolPrincipal == "CLIENTE");
Comprobar("cliente1 queda restringido a sus propios datos", Reglas.EsClienteSolo(cliente1.ListaRoles));

var admin1 = await usuarios.PorUsernameAsync("admin1");
Comprobar("admin1 tiene roles ADMIN,OPERADOR en la base", admin1?.Roles == "ADMIN,OPERADOR", admin1?.Roles);
Comprobar("admin1 / admin123 valida", password.Verificar("admin123", admin1!.PasswordHash));
Comprobar("el rol principal de admin1 es ADMIN", admin1.RolPrincipal == "ADMIN");

// ---------------------------------------------------------------- 3) plazas
Console.WriteLine("\n[3] Plazas y ocupación");
var plazas = await espacios.ListarAsync();
var ocupacion = EspacioRepositorio.CalcularOcupacion(plazas);
Comprobar("ocupación total = 768", ocupacion.Total == 768);
Comprobar("agrupa 48 zonas (4 por sede)", ocupacion.Zonas.Count == 48, string.Join(", ", ocupacion.Zonas.Keys));
Comprobar("la Zona A - Centro tiene 16 plazas", ocupacion.Zonas["Zona A - Centro"].Total == 16);
Comprobar("disponibles tipo AUTO devuelve plazas", (await espacios.DisponiblesAsync("AUTO", null)).Count > 0);
// Al pasar "tipo", el backend IGNORA la zona (comportamiento del original).
var motosSinZona = (await espacios.DisponiblesAsync("MOTO", null)).Count;
var motosConZona = (await espacios.DisponiblesAsync("MOTO", "Zona A - Centro")).Count;
Comprobar("hay plazas de MOTO libres", motosSinZona > 0, motosSinZona.ToString());
Comprobar("el tipo tiene precedencia e IGNORA la zona (MOTO + Zona A == MOTO sin zona)",
    motosConZona == motosSinZona, $"conZona={motosConZona} sinZona={motosSinZona}");
var zonaASinTipo = (await espacios.DisponiblesAsync(null, "Zona A - Centro")).Count;
Comprobar("filtrar solo por zona sí respeta la zona (Zona A = 15 libres o menos si hay ocupadas)",
    zonaASinTipo <= 16 && zonaASinTipo >= 10, zonaASinTipo.ToString());
var plazaA01 = plazas.First(p => p.Codigo == "CEN-A-01");
var plazaA02 = plazas.First(p => p.Codigo == "CEN-A-02");

// ---------------------------------------------------------------- 4) vehículos
Console.WriteLine("\n[4] Vehículos");
Comprobar("la placa ABC-123 existe", await vehiculos.PorPlacaAsync("abc-123") is not null);
Comprobar("la búsqueda por placa ignora mayúsculas", (await vehiculos.PorPlacaAsync("abc-123"))?.Placa == "ABC-123");
Comprobar("cliente1 tiene al menos 1 vehículo propio", (await vehiculos.ListarPorPropietarioAsync("cliente1")).Count >= 1);
Comprobar("operador1 tiene 1 vehículo propio", (await vehiculos.ListarPorPropietarioAsync("operador1")).Count == 1);
Comprobar("un usuario inexistente no tiene vehículos", (await vehiculos.ListarPorPropietarioAsync("nadie")).Count == 0);

// ---------------------------------------------------------------- 5) abrir ticket
Console.WriteLine("\n[5] Apertura de ticket (ocupa la plaza y crea el ticket)");
foreach (var t in await tickets.AbiertosAsync())   // limpieza de corridas anteriores
    await servicioTickets.CerrarAsync(t.Id, t.Usuario ?? "admin1", false);

var apertura = await servicioTickets.AbrirAsync("ABC-123", plazaA01.Id, "cliente1");
Comprobar("abre el ticket correctamente", apertura.Exito, apertura.Mensaje);
Comprobar("el ticket queda ABIERTO", apertura.Ticket?.Estado == "ABIERTO");
Comprobar("el código tiene formato TK-XXXXXXXX",
    apertura.Ticket?.Codigo.StartsWith("TK-") == true && apertura.Ticket!.Codigo.Length == 11, apertura.Ticket?.Codigo);
Comprobar("copia código y zona de la plaza",
    apertura.Ticket?.EspacioCodigo == "CEN-A-01" && apertura.Ticket?.Zona == "Zona A - Centro");
Comprobar("copia la tarifa de la plaza", apertura.Ticket?.TarifaHora == plazaA01.TarifaHora, $"{apertura.Ticket?.TarifaHora} vs {plazaA01.TarifaHora}");
Comprobar("el monto es NULL mientras está abierto", apertura.Ticket?.MontoTotal is null);
Comprobar("queda a nombre de cliente1", apertura.Ticket?.Usuario == "cliente1");
Comprobar("la plaza CEN-A-01 queda OCUPADA en la base", (await espacios.PorIdAsync(plazaA01.Id))?.Ocupado == true);

// ---------------------------------------------------------------- 6) conflictos
Console.WriteLine("\n[6] Validaciones y conflictos");
Comprobar("placa vacía -> 400 \"La placa es obligatoria\"",
    (await servicioTickets.AbrirAsync("", plazaA02.Id, "cliente1")).Mensaje == "La placa es obligatoria");
Comprobar("placa inválida -> 400", (await servicioTickets.AbrirAsync("XX", plazaA02.Id, "cliente1")).Codigo == 400);
Comprobar("plaza inválida -> 400 \"Debes indicar una plaza válida\"",
    (await servicioTickets.AbrirAsync("ABC-123", 0, "cliente1")).Mensaje == "Debes indicar una plaza válida");
var mismoPlaca = await servicioTickets.AbrirAsync("abc-123", plazaA02.Id, "cliente1");
Comprobar("no permite 2 tickets abiertos con la misma placa -> 409", mismoPlaca.Codigo == 409, mismoPlaca.Mensaje);
Comprobar("el mensaje de placa duplicada es el del original",
    mismoPlaca.Mensaje?.StartsWith("Ya existe un ticket abierto para la placa ABC-123") == true, mismoPlaca.Mensaje);
var mismaPlaza = await servicioTickets.AbrirAsync("DEF-456", plazaA01.Id, "cliente1");
Comprobar("no permite 2 tickets en la misma plaza -> 409", mismaPlaza.Codigo == 409, mismaPlaza.Mensaje);
Comprobar("plaza inexistente -> 404", (await servicioTickets.AbrirAsync("ZZZ-999", 99999, "cliente1")).Codigo == 404);

// ---------------------------------------------------------------- 7) cerrar y cobrar
Console.WriteLine("\n[7] Cierre y cobro (mínimo 1 hora, redondeo hacia arriba)");
var cierre = await servicioTickets.CerrarAsync(apertura.Ticket!.Id, "cliente1", true);
Comprobar("cierra el ticket", cierre.Exito, cierre.Mensaje);
Comprobar("el ticket queda CERRADO", cierre.Ticket?.Estado == "CERRADO");
Comprobar("cobra exactamente 1 hora de tarifa", cierre.Ticket?.MontoTotal == plazaA01.TarifaHora,
    cierre.Ticket?.MontoTotal?.ToString());
Comprobar("guarda la fecha de salida", cierre.Ticket?.Salida is not null);
Comprobar("el cobro coincide con la fórmula del backend",
    cierre.Ticket?.MontoTotal == Reglas.CalcularMonto(cierre.Ticket!.Entrada, cierre.Ticket.Salida, cierre.Ticket.TarifaHora));
Comprobar("no deja liberación pendiente", cierre.Ticket?.LiberacionPendiente == false);
Comprobar("la plaza CEN-A-01 vuelve a estar LIBRE", (await espacios.PorIdAsync(plazaA01.Id))?.Ocupado == false);
Comprobar("cerrar dos veces -> 409 \"El ticket ya está cerrado\"",
    (await servicioTickets.CerrarAsync(apertura.Ticket.Id, "cliente1", true)).Mensaje == "El ticket ya está cerrado");
Comprobar("un cliente no puede cerrar tickets ajenos -> 403",
    (await servicioTickets.CerrarAsync(apertura.Ticket.Id, "cliente2", true)).Codigo == 403);

// ---------------------------------------------------------------- 8) liberación pendiente
Console.WriteLine("\n[8] Cobro con el servicio de espacios caído (liberación pendiente)");
var apertura2 = await servicioTickets.AbrirAsync("ABC-123", plazaA02.Id, "cliente1");
Comprobar("abre un segundo ticket", apertura2.Exito, apertura2.Mensaje);

demo.Detener();
Comprobar("el servicio de espacios queda caído (demo)", servicioEspacios.Caido);
var abrirCaido = await servicioTickets.AbrirAsync("ZZZ-999", plazaA01.Id, "cliente1");
Comprobar("abrir con el servicio caído -> 503", abrirCaido.Codigo == 503, abrirCaido.Mensaje);
Comprobar("el mensaje 503 menciona ms-espacios", abrirCaido.Mensaje?.Contains("ms-espacios no disponible") == true);

var cierreCaido = await servicioTickets.CerrarAsync(apertura2.Ticket!.Id, "cliente1", true);
Comprobar("cerrar con el servicio caído SÍ cierra y cobra", cierreCaido.Exito, cierreCaido.Mensaje);
Comprobar("queda marcado liberacionPendiente = true", cierreCaido.Ticket?.LiberacionPendiente == true);
Comprobar("cobra el importe igualmente (tarifa de la plaza CEN-A-02)", cierreCaido.Ticket?.MontoTotal == plazaA02.TarifaHora,
    cierreCaido.Ticket?.MontoTotal?.ToString());
Comprobar("la plaza CEN-A-02 sigue ocupada (no se pudo liberar)", (await espacios.PorIdAsync(plazaA02.Id))?.Ocupado == true);

var recCaido = await servicioTickets.ReconciliarAsync();
Comprobar("reconciliar con el servicio caído informa del problema",
    recCaido.Respuesta is null && recCaido.Error?.Contains("No se pudieron listar plazas") == true, recCaido.Error);

demo.Iniciar();
var pendientes = await tickets.PendientesLiberacionAsync();
var liberadas = 0; var fallidas = 0;
foreach (var t in pendientes)
{
    try { await servicioEspacios.LiberarAsync(t.EspacioId); await tickets.MarcarPendienteAsync(t.Id, false); liberadas++; }
    catch (EspaciosNoDisponibleException) { fallidas++; }
}
Comprobar("al volver el servicio, la sincronización libera la plaza",
    liberadas == 1 && fallidas == 0, $"liberadas={liberadas} fallidas={fallidas}");
Comprobar("la plaza CEN-A-02 queda LIBRE tras sincronizar", (await espacios.PorIdAsync(plazaA02.Id))?.Ocupado == false);
Comprobar("ya no quedan liberaciones pendientes", (await tickets.PendientesLiberacionAsync()).Count == 0);

// ---------------------------------------------------------------- 9) reconciliación
Console.WriteLine("\n[9] Reconciliación de plazas");
var reconciliacion = await servicioTickets.ReconciliarAsync();
Comprobar("reconciliar funciona con el servicio arriba", reconciliacion.Respuesta is not null, reconciliacion.Error);
Comprobar("los números cuadran (plazas ocupadas == tickets abiertos)",
    reconciliacion.Respuesta?.Cuadran == true,
    $"abiertos={reconciliacion.Respuesta?.TicketsAbiertos} plazasConTicket={reconciliacion.Respuesta?.PlazasConTicketAbierto}");

// ---------------------------------------------------------------- 10) Circuit Breaker real
Console.WriteLine("\n[10] Circuit Breaker + Retry sobre el servicio real");
resiliencia.Reiniciar();
demo.Detener();
var resultadoCb = await resiliencia.EjecutarAsync(
    () => servicioEspacios.OcupacionAsync(),
    _ => new OcupacionDto(0, 0, 0, 0, new Dictionary<string, OcupacionZonaDto>()));
Comprobar("con el servicio caído devuelve el fallback (sin lanzar excepción)", resultadoCb.Fallback);
Comprobar("el fallback viene marcado con su mensaje", !string.IsNullOrWhiteSpace(resultadoCb.Mensaje));

for (var i = 0; i < 10; i++)
    await resiliencia.EjecutarAsync(() => servicioEspacios.OcupacionAsync(),
        _ => new OcupacionDto(0, 0, 0, 0, new Dictionary<string, OcupacionZonaDto>()));
Comprobar("tras 10 fallos el circuito ABRE", resiliencia.Estado == "OPEN", resiliencia.Estado);

demo.Iniciar();
resiliencia.Reiniciar();
var resultadoOk = await resiliencia.EjecutarAsync(
    () => servicioEspacios.OcupacionAsync(),
    _ => new OcupacionDto(0, 0, 0, 0, new Dictionary<string, OcupacionZonaDto>()));
Comprobar("con el servicio arriba devuelve la ocupación real (768 plazas)",
    !resultadoOk.Fallback && resultadoOk.Valor.Total == 768, $"total={resultadoOk.Valor.Total}");

// ---------------------------------------------------------------- 11) IA
Console.WriteLine("\n[11] Motor de IA con DeepSeek sin configurar (respaldo heurístico)");
var ia = new IaController(servicioEspacios, servicioTickets, tickets, vehiculos, deepSeek,
    new ReporteBalanceRepositorio(fabrica), NullLogger<IaController>.Instance);

// Los controladores leen la identidad de HttpContext.User; al invocarlos directamente
// hay que simular ese contexto (en una petición real lo pone ASP.NET Core).
ia.ControllerContext = new ControllerContext
{
    HttpContext = new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("preferred_username", "cliente1"),
            new Claim("role", "CLIENTE")
        }, "prueba"))
    }
};

var recomendacion = Extraer<RecomendacionResponse>(await ia.Recomendar(new RecomendarRequest("AUTO", "barato")));
Comprobar("recomendar devuelve una plaza válida",
    recomendacion is not null && plazas.Any(p => p.Codigo == recomendacion.EspacioCodigo), recomendacion?.EspacioCodigo);
Comprobar("indica con qué proveedor de IA asignó", ProveedorValido(recomendacion?.ProveedorIa), recomendacion?.ProveedorIa);
Comprobar("el motivo explica la elección",
    !string.IsNullOrWhiteSpace(recomendacion?.Motivo), recomendacion?.Motivo);
Comprobar("devuelve 3 alternativas", recomendacion?.Alternativas.Count == 3, recomendacion?.Alternativas.Count.ToString());
Comprobar("la recomendación NO ocupa la plaza (solo sugiere)",
    (await espacios.PorIdAsync(recomendacion!.EspacioId))?.Ocupado == false);

var libresAntes = (await espacios.DisponiblesAsync("MOTO", null)).Count;
var motoPrueba = await vehiculos.PorPlacaAsync("XYZ-789");
if (motoPrueba is not null) await vehiculos.CambiarActivoAsync(motoPrueba.Id, true);
var asignacion = Extraer<AsignacionResponse>(await ia.Asignar(new AsignarRequest("XYZ-789", "MOTO", "cerca")));
Comprobar("asignar con IA crea el ticket", asignacion?.Asignado == true, asignacion?.Motivo);
Comprobar("el ticket devuelto existe en la base",
    asignacion is not null && await tickets.PorIdAsync(asignacion.TicketId) is not null);
Comprobar("el código devuelto coincide con la plaza del ticket",
    asignacion is not null && (await tickets.PorIdAsync(asignacion.TicketId))?.EspacioCodigo == asignacion.EspacioCodigo);
Comprobar("la asignación SÍ ocupa una plaza", (await espacios.DisponiblesAsync("MOTO", null)).Count == libresAntes - 1);
Comprobar("placa vacía en asignar -> 400",
    await ia.Asignar(new AsignarRequest("", "AUTO", null)) is ObjectResult { StatusCode: 400 });

// ---------------------------------------------------------------- 12) reporte
Console.WriteLine("\n[12] Reporte IA del administrador");
var reporte = Extraer<ReporteTicketsResponse>(await ia.ReporteTickets(
    new ReporteRequest(DateTime.Today.AddDays(-7).ToString("yyyy-MM-dd"), DateTime.Today.ToString("yyyy-MM-dd"))));
Comprobar("el reporte se genera", reporte is not null);
Comprobar("cuenta los tickets del periodo", reporte!.TotalTickets >= 3, $"total={reporte.TotalTickets}");
Comprobar("el recaudado acumula los tickets cerrados del periodo", reporte.Recaudado >= 3,
    reporte.Recaudado.ToString());
Comprobar("informa de las plazas totales (768)", reporte.PlazasTotal == 768, reporte.PlazasTotal.ToString());
Comprobar("el reporte indica su proveedor de IA", ProveedorValido(reporte.ProveedorIa), reporte.ProveedorIa);
Comprobar("genera los hallazgos del periodo", reporte.Hallazgos.Count >= 5, reporte.Hallazgos.Count.ToString());
Comprobar("los hallazgos incluyen el dinero cobrado",
    reporte.Hallazgos.Any(h => h.Contains("Dinero cobrado en el periodo")));
Comprobar("el detalle de plazas ocupadas trae plaza y placa",
    reporte.Metricas.PlazasOcupadasDetalle.All(d => d.Plaza.Length > 0 && d.Placa.Length > 0));
Comprobar("fechas inválidas (hasta < desde) -> 400",
    await ia.ReporteTickets(new ReporteRequest("2025-12-31", "2025-01-01")) is ObjectResult { StatusCode: 400 });

// ---------------------------------------------------------------- 13) usuarios
Console.WriteLine("\n[13] Gestión de usuarios (ADMIN)");
var nombrePrueba = "prueba_" + Random.Shared.Next(1000, 9999);
var nuevoId = await usuarios.CrearAsync(nombrePrueba, $"{nombrePrueba}@test.local", "Usuario", "Prueba",
    password.Hashear("clave123"), "CLIENTE");
Comprobar("crea un usuario en MySQL", nuevoId > 0);
var creado = await usuarios.PorIdAsync(nuevoId);
Comprobar("el usuario creado puede iniciar sesión",
    creado is not null && password.Verificar("clave123", creado.PasswordHash));
await usuarios.ActualizarRolesAsync(nuevoId, "ADMIN,OPERADOR");
Comprobar("cambia los roles a ADMIN,OPERADOR", (await usuarios.PorIdAsync(nuevoId))?.RolPrincipal == "ADMIN");
await usuarios.ActualizarAsync(nuevoId, creado!.Email, "Usuario", "Editado", false);
var editado = await usuarios.PorIdAsync(nuevoId);
Comprobar("edita los datos y desactiva la cuenta", editado?.Enabled == false && editado.LastName == "Editado");
await usuarios.ActualizarPasswordAsync(nuevoId, password.Hashear("nueva456"));
Comprobar("cambia la contraseña", password.Verificar("nueva456", (await usuarios.PorIdAsync(nuevoId))!.PasswordHash));
Comprobar("la contraseña anterior ya no sirve",
    !password.Verificar("clave123", (await usuarios.PorIdAsync(nuevoId))!.PasswordHash));

// ---------------------------------------------------------------- 14) sedes y pase del puesto
Console.WriteLine("\n[14] Parqueaderos (sedes), recomendación y pase con QR");
var sedeRepo = new SedeRepositorio(fabrica);
var espacioRepo = new EspacioRepositorio(fabrica);
var servicioSedes = new ServicioSedes(sedeRepo, espacioRepo, deepSeek, NullLogger<ServicioSedes>.Instance);
var servicioEstacionar = new ServicioEstacionamiento(servicioTickets, tickets, espacioRepo, sedeRepo, vehiculos,
    NullLogger<ServicioEstacionamiento>.Instance);

var sedes = await sedeRepo.ListarAsync();
Comprobar("hay al menos 10 parqueaderos", sedes.Count >= 10, sedes.Count.ToString());
Comprobar("cada parqueadero tiene dirección distinta",
    sedes.Select(s => s.Direccion).Distinct().Count() == sedes.Count);
Comprobar("cada parqueadero tiene coordenadas válidas",
    sedes.All(s => s.Latitud is < 0 and > -1 && s.Longitud is < -78 and > -79));
Comprobar("los parqueaderos tienen plazas: 64 cada uno",
    (await espacios.ListarAsync()).GroupBy(p => p.SedeId).All(g => g.Count() == 64));

var conteos = await sedeRepo.ConteosAsync();
Comprobar("el conteo de plazas cubre las 12 sedes", conteos.Count == sedes.Count, conteos.Count.ToString());

var listadoSedes = await servicioSedes.ListarAsync(-0.2045, -78.49, "AUTO");
Comprobar("el listado calcula la distancia",
    listadoSedes.Count == sedes.Count && listadoSedes.All(s => s.DistanciaKm is > 0));
Comprobar("el listado viene ordenado por cercanía",
    listadoSedes.Zip(listadoSedes.Skip(1), (a, b) => (a.DistanciaKm ?? 0) <= (b.DistanciaKm ?? 0)).All(x => x));

var recomendacionSede = await servicioSedes.RecomendarAsync(
    new RecomendarSedeRequest("AUTO", -0.2045, -78.49, "sin preferencia", null, null, null, null));
Comprobar("la IA recomienda un parqueadero", recomendacionSede.Respuesta is not null, recomendacionSede.Error);
Comprobar("el parqueadero recomendado tiene plazas libres", recomendacionSede.Respuesta?.PlazasLibres > 0);
Comprobar("la recomendación trae motivo y explicación",
    !string.IsNullOrWhiteSpace(recomendacionSede.Respuesta?.Motivo) &&
    !string.IsNullOrWhiteSpace(recomendacionSede.Respuesta?.ExplicacionIa));
Comprobar("la recomendación propone alternativas", recomendacionSede.Respuesta?.Alternativas.Count >= 1);
Comprobar("el puntaje queda entre 0 y 100",
    recomendacionSede.Respuesta?.Puntaje is >= 0 and <= 100);

// pase del puesto: abrir, leer el QR y anular
var libre = (await espacios.DisponiblesAsync("AUTO", null)).First(p => p.SedeId is not null);
var pase = await servicioEstacionar.EstacionarAsync("ABC-123", libre.Id, "cliente1");
Comprobar("estacionar abre el ticket y devuelve el pase", pase.Exito && pase.Pase is not null, pase.Mensaje);
Comprobar("el pase trae la sede y su dirección",
    pase.Pase?.SedeNombre.Length > 0 && pase.Pase?.SedeDireccion.Length > 0);
Comprobar("el pase trae el puesto, la placa y la entrada",
    pase.Pase?.EspacioCodigo == libre.Codigo && pase.Pase?.Placa == "ABC-123" && pase.Pase?.Entrada is not null);
Comprobar("el pase cobra 1 hora de la tarifa del puesto",
    pase.Pase?.MontoAhora == libre.TarifaHora, $"monto={pase.Pase?.MontoAhora} tarifa={libre.TarifaHora}");
Comprobar("el QR lleva el parqueadero, el puesto y el valor",
    pase.Pase?.QrPayload.StartsWith("URBANPARK|ESTACION|") == true &&
    pase.Pase.QrPayload.Contains(libre.Codigo) &&
    pase.Pase.QrPayload.Contains(libre.TarifaHora.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
    pase.Pase?.QrPayload);

// el mismo puesto con otra placa no se puede
var repetido = await servicioEstacionar.EstacionarAsync("DEF-456", libre.Id, "cliente1");
Comprobar("un puesto ocupado no se puede reservar con otra placa", !repetido.Exito && repetido.Codigo == 409,
    repetido.Mensaje);

var paseLeido = await servicioEstacionar.PaseAsync(pase.Pase!.TicketId);
Comprobar("el pase se puede refrescar (QR y valor al momento)",
    paseLeido is not null && paseLeido.QrPayload == pase.Pase.QrPayload);

var anulacion = await servicioEstacionar.CancelarAsync(pase.Pase.TicketId, "cliente1", true);
Comprobar("anular la reserva libera el puesto sin cobrar",
    anulacion.Exito && (await espacios.PorIdAsync(libre.Id))?.Ocupado == false, anulacion.Mensaje);
// modos de entrada: reserva con fecha y hora, y activación al llegar
var libreReserva = (await espacios.DisponiblesAsync("AUTO", null)).First(p => p.SedeId is not null);
var inicio = DateTime.Now.Date.AddDays(1).AddHours(18);
var fin = inicio.AddHours(3);
var reserva = await servicioEstacionar.EstacionarAsync("ABC-123", libreReserva.Id, "cliente1", inicio, fin);
Comprobar("reservar abre el pase con QR", reserva.Exito && reserva.Pase is not null, reserva.Mensaje);
Comprobar("la reserva queda en estado RESERVADO", reserva.Pase?.Estado == "RESERVADO", reserva.Pase?.Estado);
Comprobar("la reserva guarda la franja pedida",
    reserva.Pase?.Entrada == inicio && reserva.Pase?.SalidaPrevista == fin);
Comprobar("la reserva estima las horas y el importe",
    reserva.Pase?.HorasPrevistas == 3 &&
    reserva.Pase?.MontoPrevisto == Math.Round(3 * libreReserva.TarifaHora, 2),
    $"{reserva.Pase?.HorasPrevistas} h / {reserva.Pase?.MontoPrevisto}");
var camposQr = (reserva.Pase?.QrPayload ?? "").Split('|');
var salidaQrOk = camposQr.Length > 2 &&
                 DateTime.TryParse(camposQr[^1], System.Globalization.CultureInfo.InvariantCulture,
                     System.Globalization.DateTimeStyles.None, out var salidaQr) &&
                 Math.Abs((salidaQr - fin).TotalMinutes) < 1;
Comprobar("el QR de la reserva lleva el modo y la hora de salida",
    camposQr.Contains("RESERVA") && salidaQrOk, reserva.Pase?.QrPayload);
Comprobar("el puesto queda guardado mientras es una reserva",
    (await espacios.PorIdAsync(libreReserva.Id))?.Ocupado == true);

var activada = await servicioEstacionar.ActivarAsync(reserva.Pase!.TicketId, "cliente1", true);
Comprobar("activar la reserva la convierte en estancia", activada.Exito && activada.Pase?.Estado == "ABIERTO",
    activada.Mensaje);
Comprobar("al activarla el cobro corre desde ahora", activada.Pase?.MinutosTranscurridos == 0);

var anuladaReserva = await servicioEstacionar.CancelarAsync(reserva.Pase.TicketId, "cliente1", true);
Comprobar("una reserva se puede anular sin costo",
    anuladaReserva.Exito && (await espacios.PorIdAsync(libreReserva.Id))?.Ocupado == false);

var sinSalida = await servicioEstacionar.EstacionarAsync("ABC-123", libreReserva.Id, "cliente1", inicio, null);
Comprobar("reservar sin hora de salida -> 400", !sinSalida.Exito && sinSalida.Codigo == 400, sinSalida.Mensaje);

var pasada = await servicioEstacionar.EstacionarAsync("ABC-123", libreReserva.Id, "cliente1",
    DateTime.Now.AddDays(-1), DateTime.Now.AddDays(-1).AddHours(2));
Comprobar("reservar en el pasado -> 400", !pasada.Exito && pasada.Codigo == 400, pasada.Mensaje);
Comprobar("el ticket anulado no cuenta como abierto",
    (await tickets.AbiertosAsync()).All(t => t.Id != pase.Pase.TicketId));
// ---------------------------------------------------------------- 15) autenticador de Google
Console.WriteLine("\n[15] Autenticador de Google (TOTP) en la base");
var usuarioTotp = await usuarios.PorUsernameAsync("cliente2");
Comprobar("cliente2 existe para las pruebas de TOTP", usuarioTotp is not null);
Comprobar("de fábrica no tiene autenticador", usuarioTotp!.TotpActivo == false && usuarioTotp.TotpSecret is null);

var secretoTotp = ServicioTotp.NuevoSecreto();
await usuarios.GuardarTotpAsync(usuarioTotp.Id, secretoTotp, false);
var preparado = await usuarios.PorIdAsync(usuarioTotp.Id);
Comprobar("el secreto se guarda pero queda inactivo",
    preparado?.TotpSecret == secretoTotp && preparado?.TotpActivo == false);

await usuarios.GuardarTotpAsync(usuarioTotp.Id, secretoTotp, true);
var activo = await usuarios.PorIdAsync(usuarioTotp.Id);
Comprobar("confirmar el código deja el autenticador activo", activo?.TotpActivo == true);
Comprobar("el código que genera el sistema valida para ese secreto",
    ServicioTotp.Verificar(activo?.TotpSecret, ServicioTotp.Codigo(secretoTotp, DateTime.UtcNow)));

await usuarios.GuardarTotpAsync(usuarioTotp.Id, null, false);
var limpio = await usuarios.PorIdAsync(usuarioTotp.Id);
Comprobar("desactivar borra el secreto y deja entrar sin código",
    limpio?.TotpSecret is null && limpio?.TotpActivo == false);
// ---------------------------------------------------------------- 16) limpieza
Console.WriteLine("\n[14] Estado final de la base");
demo.Iniciar();
foreach (var t in await tickets.AbiertosAsync())
    await servicioTickets.CerrarAsync(t.Id, t.Usuario ?? "admin1", false);

var abiertosFinal = (await tickets.AbiertosAsync()).Count;
var ocupadasFinal = (await espacios.ListarAsync()).Count(p => p.Ocupado);
Comprobar("no quedan tickets abiertos", abiertosFinal == 0, abiertosFinal.ToString());
Comprobar("no quedan plazas ocupadas sin ticket", ocupadasFinal == 0, ocupadasFinal.ToString());

Console.WriteLine("\n=============================================================");
Console.WriteLine($" RESULTADO: {pruebas - fallos}/{pruebas} comprobaciones correctas" +
                  (fallos == 0 ? "  ✓ TODO OK" : $"  ({fallos} FALLOS)"));
Console.WriteLine("=============================================================");

return fallos == 0 ? 0 : 1;

static T? Extraer<T>(IActionResult resultado) where T : class =>
    (resultado as ObjectResult)?.Value as T;

// El proveedor de IA puede ser el respaldo local o un modelo real (Ollama/Cursor) si está encendido.
static bool ProveedorValido(string? proveedor) =>
    !string.IsNullOrWhiteSpace(proveedor) &&
    (proveedor!.Contains("heuristica", StringComparison.OrdinalIgnoreCase) ||
     proveedor.Contains("ollama", StringComparison.OrdinalIgnoreCase) ||
     proveedor.Contains("cursor", StringComparison.OrdinalIgnoreCase) ||
     proveedor.Contains("grok", StringComparison.OrdinalIgnoreCase));
