using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrbanPark.Api.Modelos;
using UrbanPark.Api.Repositorios;
using UrbanPark.Api.Servicios;

namespace UrbanPark.Api.Controladores;

/// <summary>
/// Catalogo de zonas y plazas (equivalente a ms-espacios, puerto 8081 en el original).
/// Si la demo de Resilience marca el servicio como caído, todos los endpoints responden 503,
/// exactamente como cuando el contenedor estaba detenido.
/// Todas las consultas aceptan sedeId: el mapa trabaja siempre sobre un parqueadero.
/// </summary>
[Route("api/espacios")]
[Authorize]
public sealed class EspaciosController : ControladorBase
{
    private readonly EspacioRepositorio _repo;
    private readonly SedeRepositorio _sedes;
    private readonly ServicioEspacios _servicio;

    public EspaciosController(EspacioRepositorio repo, SedeRepositorio sedes, ServicioEspacios servicio)
    {
        _repo = repo;
        _sedes = sedes;
        _servicio = servicio;
    }

    private ObjectResult? Caido() => _servicio.Caido
        ? NoDisponible("ms-espacios no disponible (demo). Pulsa «Iniciar ms-espacios» en la pestaña Resilience.")
        : null;

    private static EspacioDto A(Espacio e) =>
        new(e.Id, e.Codigo, e.Zona, e.TipoVehiculo, e.Ocupado, e.TarifaHora, e.Nivel, e.Descripcion,
            e.SedeId, e.SedeNombre, e.SedeDireccion);

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] long? sedeId)
    {
        if (Caido() is { } e) return e;
        var plazas = sedeId is > 0 ? await _repo.ListarPorSedeAsync(sedeId.Value) : await _repo.ListarAsync();
        return Ok(plazas.Select(A));
    }

    [HttpGet("disponibles")]
    public async Task<IActionResult> Disponibles([FromQuery] string? tipo, [FromQuery] string? zona,
        [FromQuery] long? sedeId)
    {
        if (Caido() is { } e) return e;
        return Ok((await _repo.DisponiblesAsync(tipo, zona, sedeId)).Select(A));
    }

    [HttpGet("ocupacion")]
    public async Task<IActionResult> Ocupacion([FromQuery] long? sedeId)
    {
        if (Caido() is { } e) return e;
        var plazas = sedeId is > 0 ? await _repo.ListarPorSedeAsync(sedeId.Value) : await _repo.ListarAsync();
        return Ok(EspacioRepositorio.CalcularOcupacion(plazas));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Obtener(long id)
    {
        if (Caido() is { } e) return e;
        var espacio = await _repo.PorIdAsync(id);
        return espacio is null ? NoEncontrado("Espacio no encontrado") : Ok(A(espacio));
    }

    [HttpPost]
    [Authorize(Roles = "OPERADOR,ADMIN")]
    public async Task<IActionResult> Crear([FromBody] GuardarEspacioDto peticion)
    {
        if (Caido() is { } e) return e;

        var codigo = (peticion.Codigo ?? "").Trim();
        var zona = (peticion.Zona ?? "").Trim();
        var tipo = (peticion.TipoVehiculo ?? "").Trim().ToUpperInvariant();

        if (codigo.Length == 0) return PeticionInvalida("El código es obligatorio");
        if (zona.Length == 0) return PeticionInvalida("La zona es obligatoria");
        if (tipo.Length == 0) return PeticionInvalida("El tipo de vehículo es obligatorio");
        if (peticion.TarifaHora is null or < 0) return PeticionInvalida("La tarifa por hora es obligatoria");

        if (await _repo.PorCodigoAsync(codigo) is not null)
            return Conflicto($"Ya existe una plaza con el código {codigo}");

        var sedeId = peticion.SedeId is > 0 ? peticion.SedeId : null;
        if (sedeId is not null && await _sedes.PorIdAsync(sedeId.Value) is null)
            return NoEncontrado("El parqueadero indicado no existe");

        var id = await _repo.CrearAsync(codigo, zona, tipo, peticion.TarifaHora!.Value, peticion.Nivel,
            peticion.Descripcion, sedeId);
        var creado = await _repo.PorIdAsync(id);
        return Ok(A(creado!));
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = "OPERADOR,ADMIN")]
    public async Task<IActionResult> Actualizar(long id, [FromBody] GuardarEspacioDto peticion)
    {
        if (Caido() is { } e) return e;

        var espacio = await _repo.PorIdAsync(id);
        if (espacio is null) return NoEncontrado("Espacio no encontrado");

        var codigo = (peticion.Codigo ?? "").Trim();
        var zona = (peticion.Zona ?? "").Trim();
        var tipo = (peticion.TipoVehiculo ?? "").Trim().ToUpperInvariant();

        if (codigo.Length == 0) return PeticionInvalida("El código es obligatorio");
        if (zona.Length == 0) return PeticionInvalida("La zona es obligatoria");
        if (tipo.Length == 0) return PeticionInvalida("El tipo de vehículo es obligatorio");

        var otro = await _repo.PorCodigoAsync(codigo);
        if (otro is not null && otro.Id != id) return Conflicto($"Ya existe una plaza con el código {codigo}");

        // 'ocupado' NO se toca: se gestiona con ocupar/liberar (igual que el original).
        await _repo.ActualizarAsync(id, codigo, zona, tipo, peticion.TarifaHora ?? espacio.TarifaHora,
            peticion.Nivel, peticion.Descripcion);

        return Ok(A((await _repo.PorIdAsync(id))!));
    }

    [HttpPost("{id:long}/ocupar")]
    [Authorize(Roles = "CLIENTE,OPERADOR,ADMIN")]
    public async Task<IActionResult> Ocupar(long id)
    {
        if (Caido() is { } e) return e;

        var (resultado, espacio) = await _servicio.OcuparAsync(id);
        return resultado switch
        {
            ResultadoOcupar.NoExiste => NoEncontrado("Espacio no encontrado"),
            ResultadoOcupar.YaOcupada => Conflicto("La plaza ya está ocupada"),
            _ => Ok(A(espacio!))
        };
    }

    [HttpPost("{id:long}/liberar")]
    [Authorize(Roles = "CLIENTE,OPERADOR,ADMIN")]
    public async Task<IActionResult> Liberar(long id)
    {
        if (Caido() is { } e) return e;

        var espacio = await _repo.PorIdAsync(id);
        if (espacio is null) return NoEncontrado("Espacio no encontrado");

        await _repo.LiberarAsync(id);
        return Ok(A((await _repo.PorIdAsync(id))!));
    }
}
