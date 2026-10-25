using System.Text.Json;
using UrbanPark.Api.Data;

namespace UrbanPark.Api.Repositorios;

/// <summary>Último reporte guardado: usos por zona y por plaza para equilibrar la asignación.</summary>
public sealed class ReporteBalance
{
    public DateTime Desde { get; init; }
    public DateTime Hasta { get; init; }
    public DateTime GeneradoEn { get; init; }
    public Dictionary<string, int> Zonas { get; init; } = [];
    public Dictionary<string, int> Plazas { get; init; } = [];
}

public sealed class ReporteBalanceRepositorio
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly IFabricaConexion _fabrica;

    public ReporteBalanceRepositorio(IFabricaConexion fabrica) => _fabrica = fabrica;

    public async Task GuardarAsync(DateTime desde, DateTime hasta, IReadOnlyDictionary<string, int> zonas,
        IReadOnlyDictionary<string, int> plazas)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync(
            @"INSERT INTO reporte_balance (id, desde, hasta, generado_en, zonas_json, plazas_json)
              VALUES (1, @d, @h, @g, @z, @p)
              ON DUPLICATE KEY UPDATE desde = VALUES(desde), hasta = VALUES(hasta), generado_en = VALUES(generado_en),
                  zonas_json = VALUES(zonas_json), plazas_json = VALUES(plazas_json)",
            ("@d", desde.Date), ("@h", hasta.Date), ("@g", DateTime.Now),
            ("@z", JsonSerializer.Serialize(zonas)), ("@p", JsonSerializer.Serialize(plazas)));
    }

    public async Task<ReporteBalance?> ObtenerAsync()
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync(
            "SELECT desde, hasta, generado_en, zonas_json, plazas_json FROM reporte_balance WHERE id = 1",
            rd => new ReporteBalance
            {
                Desde = rd.Fecha("desde"),
                Hasta = rd.Fecha("hasta"),
                GeneradoEn = rd.Fecha("generado_en"),
                Zonas = LeerMapa(rd.Texto("zonas_json")),
                Plazas = LeerMapa(rd.Texto("plazas_json"))
            });
    }

    private static Dictionary<string, int> LeerMapa(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
