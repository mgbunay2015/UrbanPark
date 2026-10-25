using UrbanPark.Api.Modelos;
using UrbanPark.Api.Repositorios;

namespace UrbanPark.Api.Servicios;

public enum ResultadoOcupar { Ok, NoExiste, YaOcupada }

/// <summary>
/// Servicio de espacios visto como dependencia externa: es lo que en el sistema original
/// se invocaba por Feign desde ms-tickets y ms-ia. Cuando la demo marca el servicio como
/// caído, todas las operaciones lanzan EspaciosNoDisponibleException para que el Circuit
/// Breaker y los fallbacks actuen igual que antes.
/// </summary>
public sealed class ServicioEspacios
{
    private readonly EspacioRepositorio _repo;
    private readonly EstadoDemoService _demo;

    public ServicioEspacios(EspacioRepositorio repo, EstadoDemoService demo)
    {
        _repo = repo;
        _demo = demo;
    }

    public bool Caido => _demo.EspaciosCaido;

    public void VerificarDisponibilidad()
    {
        if (_demo.EspaciosCaido)
            throw new EspaciosNoDisponibleException(
                "ms-espacios no disponible (simulado desde la pestaña Resilience). Detalle: conexión rechazada.");
    }

    public async Task<List<Espacio>> ListarAsync()
    {
        VerificarDisponibilidad();
        return await _repo.ListarAsync();
    }

    public async Task<List<Espacio>> DisponiblesAsync(string? tipo, string? zona)
    {
        VerificarDisponibilidad();
        return await _repo.DisponiblesAsync(tipo, zona);
    }

    /// <summary>
    /// Ocupación global o solo de las sedes indicadas (null/lista vacía = todas).
    /// </summary>
    public async Task<OcupacionDto> OcupacionAsync(IEnumerable<long>? sedes = null)
    {
        VerificarDisponibilidad();
        var plazas = await _repo.ListarAsync();

        var filtro = sedes?.Where(id => id > 0).ToHashSet();
        if (filtro is { Count: > 0 })
            plazas = plazas.Where(p => p.SedeId is long sede && filtro.Contains(sede)).ToList();

        return EspacioRepositorio.CalcularOcupacion(plazas);
    }

    /// <summary>Plazas de esas sedes (null/lista vacía = todas).</summary>
    public async Task<List<Espacio>> ListarDeSedesAsync(IEnumerable<long>? sedes = null)
    {
        VerificarDisponibilidad();
        var plazas = await _repo.ListarAsync();

        var filtro = sedes?.Where(id => id > 0).ToHashSet();
        if (filtro is { Count: > 0 })
            plazas = plazas.Where(p => p.SedeId is long sede && filtro.Contains(sede)).ToList();

        return plazas;
    }

    /// <summary>Ocupa la plaza. Devuelve el espacio ocupado para copiar codigo/zona/tarifa al ticket.</summary>
    public async Task<(ResultadoOcupar resultado, Espacio? espacio)> OcuparAsync(long id)
    {
        VerificarDisponibilidad();

        var espacio = await _repo.PorIdAsync(id);
        if (espacio is null) return (ResultadoOcupar.NoExiste, null);
        if (espacio.Ocupado) return (ResultadoOcupar.YaOcupada, espacio);

        // Ocupacion condicional: evita la race condition del backend original.
        var ok = await _repo.OcuparAsync(id);
        return ok ? (ResultadoOcupar.Ok, espacio) : (ResultadoOcupar.YaOcupada, espacio);
    }

    public async Task LiberarAsync(long id)
    {
        VerificarDisponibilidad();
        await _repo.LiberarAsync(id);
    }
}
