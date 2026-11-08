using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrbanPark.Api.Modelos;
using UrbanPark.Api.Repositorios;
using UrbanPark.Api.Servicios;

namespace UrbanPark.Api.Controladores;

/// <summary>Registro de vehiculos y conductores (equivalente a ms-vehiculos, puerto 8082).</summary>
[Route("api/vehiculos")]
[Authorize(Roles = "CLIENTE,OPERADOR,ADMIN")]
public sealed class VehiculosController : ControladorBase
{
    private readonly VehiculoRepositorio _repo;
    private readonly ConsultaVehiculoService _consulta;

    public VehiculosController(VehiculoRepositorio repo, ConsultaVehiculoService consulta)
    {
        _repo = repo;
        _consulta = consulta;
    }

    private static VehiculoDto A(Vehiculo v) =>
        new(v.Id, v.Placa, v.Tipo, v.Marca, v.Color, v.Propietario, v.PropietarioEmail, v.PropietarioUsername, v.Descuento, v.Activo);

    /// <summary>CLIENTE solo ve los suyos; OPERADOR y ADMIN ven todos.</summary>
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var lista = EsClienteSolo
            ? await _repo.ListarPorPropietarioAsync(Username)
            : await _repo.ListarAsync();

        return Ok(lista.Select(A));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Obtener(long id)
    {
        var vehiculo = await _repo.PorIdAsync(id);
        if (vehiculo is null) return NoEncontrado("Vehículo no encontrado");

        if (EsClienteSolo && !Pertenece(vehiculo))
            return Prohibido("Solo puedes gestionar tus propios vehículos");

        return Ok(A(vehiculo));
    }

    [HttpGet("placa/{placa}")]
    public async Task<IActionResult> PorPlaca(string placa)
    {
        var vehiculo = await _repo.PorPlacaAsync(placa);
        if (vehiculo is null) return NoEncontrado("Placa no registrada");

        if (EsClienteSolo && !Pertenece(vehiculo))
            return Prohibido("Solo puedes gestionar tus propios vehículos");

        return Ok(A(vehiculo));
    }

    /// <summary>
    /// Ficha del vehículo en EcuadorAPI: con solo la placa se completan marca, modelo, año,
    /// color y clase. Nunca falla la pantalla: cuando no hay créditos o la placa no existe,
    /// responde 200 con <c>encontrado = false</c> y el motivo para que se escriba a mano.
    /// </summary>
    [HttpGet("consultar/{placa}")]
    public async Task<IActionResult> Consultar(string placa)
    {
        var (datos, error) = await _consulta.ConsultarAsync(placa);

        if (datos is not null) return Ok(datos);

        return Ok(new ConsultaVehiculoDto(false, error, ConsultaVehiculoService.Limpiar(placa),
            null, null, null, null, null, null, null,
            _consulta.Configurado ? "ecuadorapi" : "sin-configurar"));
    }

    /// <summary>
    /// Matrícula registrada del vehículo (año, última matrícula, caducidad y entidad).
    /// Igual que la ficha, responde 200 con <c>encontrado = false</c> cuando no se puede consultar.
    /// </summary>
    [HttpGet("consultar/{placa}/matricula")]
    public async Task<IActionResult> ConsultarMatricula(string placa)
    {
        var (datos, error) = await _consulta.ConsultarMatriculaAsync(placa);

        if (datos is not null) return Ok(datos);

        return Ok(new MatriculaVehiculoDto(false, error, ConsultaVehiculoService.Limpiar(placa),
            null, null, null, null,
            _consulta.Configurado ? "ecuadorapi" : "sin-configurar"));
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] GuardarVehiculoDto peticion)
    {
        var (ok, normalizada, error) = Reglas.ValidarPlaca(peticion.Placa, Reglas.MensajePlacaInvalidaVehiculos);
        if (!ok) return PeticionInvalida(error!);

        if (string.IsNullOrWhiteSpace(peticion.Propietario))
            return PeticionInvalida("El propietario es obligatorio");

        if (await _repo.PorPlacaAsync(normalizada) is { } existente)
            return Conflicto(existente.Activo
                ? "La placa ya está registrada"
                : $"La placa {normalizada} está de baja. Actívala en la lista, no hace falta registrarla otra vez.");

        // Un CLIENTE siempre queda como propietario de lo que registra (igual que el original);
        // OPERADOR/ADMIN pueden asignar otro usuario, o se usa el suyo si viene vacío.
        var propietarioUsername = EsClienteSolo || string.IsNullOrWhiteSpace(peticion.PropietarioUsername)
            ? Username
            : peticion.PropietarioUsername!.Trim();

        var tipo = string.IsNullOrWhiteSpace(peticion.Tipo) ? "AUTO" : peticion.Tipo!.Trim().ToUpperInvariant();

        var id = await _repo.CrearAsync(normalizada, tipo, Vacio(peticion.Marca), Vacio(peticion.Color),
            peticion.Propietario!.Trim(), Vacio(peticion.PropietarioEmail), propietarioUsername);

        return Ok(A((await _repo.PorIdAsync(id))!));
    }

    /// <summary>Solo OPERADOR/ADMIN. La placa nunca se modifica.</summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = "OPERADOR,ADMIN")]
    public async Task<IActionResult> Actualizar(long id, [FromBody] GuardarVehiculoDto peticion)
    {
        var vehiculo = await _repo.PorIdAsync(id);
        if (vehiculo is null) return NoEncontrado("Vehículo no encontrado");

        if (string.IsNullOrWhiteSpace(peticion.Propietario))
            return PeticionInvalida("El propietario es obligatorio");

        var tipo = string.IsNullOrWhiteSpace(peticion.Tipo) ? vehiculo.Tipo : peticion.Tipo!.Trim().ToUpperInvariant();

        await _repo.ActualizarAsync(id, tipo, Vacio(peticion.Marca), Vacio(peticion.Color),
            peticion.Propietario!.Trim(), Vacio(peticion.PropietarioEmail), vehiculo.PropietarioUsername);

        return Ok(A((await _repo.PorIdAsync(id))!));
    }

    /// <summary>Baja o alta lógica. El registro no se borra.</summary>
    [HttpPatch("{id:long}/activo")]
    public async Task<IActionResult> CambiarActivo(long id, [FromBody] ActivoVehiculoDto peticion)
    {
        var vehiculo = await _repo.PorIdAsync(id);
        if (vehiculo is null) return NoEncontrado("Vehículo no encontrado");
        if (EsClienteSolo && !Pertenece(vehiculo))
            return Prohibido("Solo puedes gestionar tus propios vehículos");

        await _repo.CambiarActivoAsync(id, peticion.Activo);
        vehiculo.Activo = peticion.Activo;
        return Ok(A(vehiculo));
    }

    /// <summary>Solo OPERADOR/ADMIN. Devuelve 200 con cuerpo vacio (no 204), igual que el original.</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = "OPERADOR,ADMIN")]
    public async Task<IActionResult> Eliminar(long id)
    {
        var vehiculo = await _repo.PorIdAsync(id);
        if (vehiculo is null) return NoEncontrado("Vehículo no encontrado");

        await _repo.EliminarAsync(id);
        return Ok();
    }

    private bool Pertenece(Vehiculo v) =>
        !string.IsNullOrWhiteSpace(v.PropietarioUsername) &&
        v.PropietarioUsername.Equals(Username, StringComparison.OrdinalIgnoreCase);

    private static string? Vacio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
