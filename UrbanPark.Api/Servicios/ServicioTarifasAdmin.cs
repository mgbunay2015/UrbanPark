using System.Globalization;
using UrbanPark.Api.Repositorios;

namespace UrbanPark.Api.Servicios;

public sealed class ServicioTarifasAdmin
{
    private readonly SedeRepositorio _sedes;
    private readonly EspacioRepositorio _espacios;

    public ServicioTarifasAdmin(SedeRepositorio sedes, EspacioRepositorio espacios)
    {
        _sedes = sedes;
        _espacios = espacios;
    }

    public Task<List<TarifaSedeResumen>> ResumenAsync() => _espacios.ResumenTarifasPorSedeAsync();

    public async Task<(int Sedes, int Plazas, string Detalle)> AplicarAsync(TarifasMasivasPeticion peticion)
    {
        var modo = (peticion.Modo ?? "").Trim().ToUpperInvariant();
        if (peticion.Valor <= 0 && modo is "FIJO" or "TARIFA_BASE")
            throw new ArgumentException("El valor debe ser mayor que cero.");

        var ids = await ResolverSedesAsync(peticion.Sedes);
        if (ids.Count == 0)
            throw new ArgumentException("No hay sedes para actualizar.");

        return modo switch
        {
            "PORCENTAJE" => await AplicarPorcentajeAsync(ids, peticion),
            "FIJO" => await AplicarFijoAsync(ids, peticion),
            "TARIFA_BASE" => await AplicarTarifaBaseAsync(ids, peticion.Valor),
            _ => throw new ArgumentException("Modo inválido. Use PORCENTAJE, FIJO o TARIFA_BASE.")
        };
    }

    private async Task<List<long>> ResolverSedesAsync(long[]? pedidas)
    {
        if (pedidas is { Length: > 0 })
            return pedidas.Where(id => id > 0).Distinct().ToList();

        var todas = await _sedes.ListarAsync();
        return todas.Where(s => s.Activa).Select(s => s.Id).ToList();
    }

    private async Task<(int, int, string)> AplicarPorcentajeAsync(List<long> sedes, TarifasMasivasPeticion p)
    {
        var factor = 1 + p.Valor / 100.0;
        var plazas = await _espacios.AjustarTarifaMasivoAsync(sedes, p.Zona, p.TipoVehiculo, factor, null);

        var sedesActualizadas = 0;
        if (string.IsNullOrWhiteSpace(p.Zona) && string.IsNullOrWhiteSpace(p.TipoVehiculo))
            sedesActualizadas = await _sedes.AjustarTarifaBaseMasivoAsync(sedes, factor);

        var signo = p.Valor >= 0 ? "+" : "";
        return (sedesActualizadas > 0 ? sedes.Count : 0, plazas,
            $"Ajuste {signo}{p.Valor.ToString("0.##", CultureInfo.InvariantCulture)} % en {plazas} plazas.");
    }

    private async Task<(int, int, string)> AplicarFijoAsync(List<long> sedes, TarifasMasivasPeticion p)
    {
        var plazas = await _espacios.AjustarTarifaMasivoAsync(sedes, p.Zona, p.TipoVehiculo, null, p.Valor);
        return (0, plazas, $"Tarifa fija ${p.Valor:0.00}/h en {plazas} plazas.");
    }

    private async Task<(int, int, string)> AplicarTarifaBaseAsync(List<long> sedes, double tarifaBase)
    {
        var sedesActualizadas = await _sedes.ActualizarTarifaBaseMasivoAsync(sedes, tarifaBase);
        var plazas = 0;
        foreach (var id in sedes)
        {
            var sede = await _sedes.PorIdAsync(id);
            if (sede is null) continue;
            plazas += await _espacios.RecalcularTarifasSedeAsync(id, sede.Codigo, tarifaBase);
        }

        return (sedesActualizadas, plazas,
            $"Tarifa base ${tarifaBase:0.00}/h en {sedesActualizadas} sedes; {plazas} plazas recalculadas.");
    }
}

public sealed record TarifasMasivasPeticion(string? Modo, double Valor, long[]? Sedes, string? Zona, string? TipoVehiculo);

public sealed record TarifaSedeResumen(long SedeId, string Codigo, string Nombre, double TarifaBase, double TarifaMin,
    double TarifaMax, int Plazas);
