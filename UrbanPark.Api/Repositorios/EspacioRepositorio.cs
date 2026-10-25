using MySqlConnector;
using UrbanPark.Api.Data;
using UrbanPark.Api.Modelos;

namespace UrbanPark.Api.Repositorios;

public sealed class EspacioRepositorio
{
    private const string Columnas =
        "e.id, e.codigo, e.zona, e.tipo_vehiculo, e.ocupado, e.tarifa_hora, e.nivel, e.descripcion, e.sede_id, " +
        "s.nombre AS sede_nombre, s.direccion AS sede_direccion";

    private const string Desde = "espacios e LEFT JOIN sedes s ON s.id = e.sede_id";

    private readonly IFabricaConexion _fabrica;

    public EspacioRepositorio(IFabricaConexion fabrica) => _fabrica = fabrica;

    internal static Espacio Mapear(MySqlDataReader rd) => new()
    {
        Id = rd.Long("id"),
        Codigo = rd.TextoObligatorio("codigo"),
        Zona = rd.TextoObligatorio("zona"),
        TipoVehiculo = rd.TextoObligatorio("tipo_vehiculo"),
        Ocupado = rd.Booleano("ocupado"),
        TarifaHora = rd.DobleNulo("tarifa_hora") ?? 0,
        Nivel = rd.EnteroNulo("nivel"),
        Descripcion = rd.Texto("descripcion"),
        SedeId = rd.LongNulo("sede_id"),
        SedeNombre = rd.Texto("sede_nombre"),
        SedeDireccion = rd.Texto("sede_direccion")
    };

    public async Task<List<Espacio>> ListarAsync()
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarAsync($"SELECT {Columnas} FROM {Desde} ORDER BY e.codigo", Mapear);
    }

    /// <summary>Plazas de una sede (el mapa y la pantalla de puestos siempre trabajan por sede).</summary>
    public async Task<List<Espacio>> ListarPorSedeAsync(long sedeId)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarAsync($"SELECT {Columnas} FROM {Desde} WHERE e.sede_id = @s ORDER BY e.codigo",
            Mapear, ("@s", sedeId));
    }

    public async Task<Espacio?> PorIdAsync(long id)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync($"SELECT {Columnas} FROM {Desde} WHERE e.id = @id LIMIT 1", Mapear, ("@id", id));
    }

    public async Task<Espacio?> PorCodigoAsync(string codigo)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync($"SELECT {Columnas} FROM {Desde} WHERE UPPER(e.codigo) = @c LIMIT 1", Mapear,
            ("@c", codigo.ToUpperInvariant()));
    }

    /// <summary>
    /// Plazas libres filtradas. El parametro "tipo" tiene PRECEDENCIA e ignora la zona,
    /// replicando el comportamiento exacto de GET /api/espacios/disponibles.
    /// </summary>
    public async Task<List<Espacio>> DisponiblesAsync(string? tipo, string? zona, long? sedeId = null)
    {
        await using var cn = _fabrica.Crear();
        var filtroSede = sedeId is null ? "" : " AND e.sede_id = @s";

        if (!string.IsNullOrWhiteSpace(tipo))
            return await cn.ConsultarAsync(
                $"SELECT {Columnas} FROM {Desde} WHERE e.ocupado = 0 AND UPPER(e.tipo_vehiculo) = @t{filtroSede} ORDER BY e.codigo",
                Mapear, sedeId is null ? [("@t", tipo.Trim().ToUpperInvariant())]
                    : [("@t", tipo.Trim().ToUpperInvariant()), ("@s", sedeId.Value)]);

        if (!string.IsNullOrWhiteSpace(zona))
            return await cn.ConsultarAsync(
                $"SELECT {Columnas} FROM {Desde} WHERE e.ocupado = 0 AND e.zona = @z{filtroSede} ORDER BY e.codigo",
                Mapear, sedeId is null ? [("@z", zona)] : [("@z", zona), ("@s", sedeId.Value)]);

        return await cn.ConsultarAsync(
            $"SELECT {Columnas} FROM {Desde} WHERE e.ocupado = 0{(sedeId is null ? "" : filtroSede)} ORDER BY e.codigo",
            Mapear, sedeId is null ? [] : [("@s", sedeId.Value)]);
    }

    public async Task<long> CrearAsync(string codigo, string zona, string tipoVehiculo, double tarifaHora,
        int? nivel, string? descripcion, long? sedeId = null)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync(
            @"INSERT INTO espacios (codigo, zona, tipo_vehiculo, ocupado, tarifa_hora, nivel, descripcion, sede_id)
              VALUES (@c, @z, @t, 0, @th, @n, @d, @s)",
            ("@c", codigo), ("@z", zona), ("@t", tipoVehiculo), ("@th", tarifaHora), ("@n", nivel),
            ("@d", descripcion), ("@s", sedeId));

        return await cn.EscalarAsync("SELECT id FROM espacios WHERE codigo = @c LIMIT 1", ("@c", codigo));
    }

    /// <summary>Actualiza la plaza. 'ocupado' NO se modifica nunca por esta via (igual que el original).</summary>
    public async Task ActualizarAsync(long id, string codigo, string zona, string tipoVehiculo, double tarifaHora,
        int? nivel, string? descripcion)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync(
            @"UPDATE espacios SET codigo = @c, zona = @z, tipo_vehiculo = @t, tarifa_hora = @th, nivel = @n, descripcion = @d
              WHERE id = @id",
            ("@c", codigo), ("@z", zona), ("@t", tipoVehiculo), ("@th", tarifaHora),
            ("@n", nivel), ("@d", descripcion), ("@id", id));
    }

    /// <summary>Ocupa la plaza de forma condicional. Devuelve false si ya estaba ocupada (choque real).</summary>
    public async Task<bool> OcuparAsync(long id)
    {
        await using var cn = _fabrica.Crear();
        var filas = await cn.EjecutarAsync("UPDATE espacios SET ocupado = 1 WHERE id = @id AND ocupado = 0", ("@id", id));
        return filas > 0;
    }

    /// <summary>Libera la plaza sin comprobar el estado previo (operacion idempotente).</summary>
    public async Task LiberarAsync(long id)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync("UPDATE espacios SET ocupado = 0 WHERE id = @id", ("@id", id));
    }

    public async Task<int> ContarAsync()
    {
        await using var cn = _fabrica.Crear();
        return (int)await cn.EscalarAsync("SELECT COUNT(*) FROM espacios");
    }

    /// <summary>Zonas de una sede con sus totales (para la ficha del parqueadero).</summary>
    public async Task<List<SedeZonaDto>> ZonasDeSedeAsync(long sedeId)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarAsync(
            @"SELECT zona,
                     MIN(nivel) AS nivel,
                     COUNT(*) AS total,
                     SUM(CASE WHEN ocupado = 0 THEN 1 ELSE 0 END) AS libres,
                     SUM(CASE WHEN ocupado = 1 THEN 1 ELSE 0 END) AS ocupados,
                     MIN(tarifa_hora) AS tarifa_desde,
                     MAX(tarifa_hora) AS tarifa_hasta
              FROM espacios
              WHERE sede_id = @s
              GROUP BY zona
              ORDER BY zona",
            rd => new SedeZonaDto(
                rd.TextoObligatorio("zona"),
                rd.EnteroNulo("nivel"),
                Convert.ToInt32(Convert.ToInt64(rd.GetValue(rd.GetOrdinal("total")))),
                Convert.ToInt32(Convert.ToInt64(rd.GetValue(rd.GetOrdinal("libres")))),
                Convert.ToInt32(Convert.ToInt64(rd.GetValue(rd.GetOrdinal("ocupados")))),
                rd.DobleNulo("tarifa_desde") ?? 0,
                rd.DobleNulo("tarifa_hasta") ?? 0),
            ("@s", sedeId));
    }

    public static OcupacionDto CalcularOcupacion(List<Espacio> plazas)
    {
        var total = plazas.Count;
        var ocupados = plazas.Count(p => p.Ocupado);
        var libres = total - ocupados;
        var porcentaje = total == 0 ? 0 : (int)Math.Round(ocupados * 100.0 / total);

        var zonas = new Dictionary<string, OcupacionZonaDto>();
        foreach (var grupo in plazas.GroupBy(p => p.Zona))
        {
            var t = grupo.Count();
            var o = grupo.Count(p => p.Ocupado);
            zonas[grupo.Key] = new OcupacionZonaDto(t, o, t - o, t == 0 ? 0 : (int)Math.Round(o * 100.0 / t));
        }

        return new OcupacionDto(total, ocupados, libres, porcentaje, zonas);
    }
}
