using MySqlConnector;
using UrbanPark.Api.Data;
using UrbanPark.Api.Modelos;

namespace UrbanPark.Api.Repositorios;

/// <summary>Conteo de plazas de una sede (una sola consulta agrupada para todo el mapa).</summary>
public sealed record ConteoSede(long SedeId, int Total, int Ocupados, int Libres, int LibresAuto, int LibresMoto,
    int LibresCamioneta, int LibresDiscapacidad, int LibresEmbarazada);

/// <summary>Acceso a las sedes (parqueaderos) y a los conteos de sus plazas.</summary>
public sealed class SedeRepositorio
{
    private const string Columnas =
        "id, codigo, nombre, direccion, sector, latitud, longitud, telefono, horario, techado, vigilancia, " +
        "acepta_reserva, tarifa_base, servicios, activa";

    private readonly IFabricaConexion _fabrica;

    public SedeRepositorio(IFabricaConexion fabrica) => _fabrica = fabrica;

    internal static Sede Mapear(MySqlDataReader rd) => new()
    {
        Id = rd.Long("id"),
        Codigo = rd.TextoObligatorio("codigo"),
        Nombre = rd.TextoObligatorio("nombre"),
        Direccion = rd.TextoObligatorio("direccion"),
        Sector = rd.TextoObligatorio("sector"),
        Latitud = rd.DobleNulo("latitud") ?? 0,
        Longitud = rd.DobleNulo("longitud") ?? 0,
        Telefono = rd.TextoObligatorio("telefono"),
        Horario = rd.TextoObligatorio("horario"),
        Techado = rd.Booleano("techado"),
        Vigilancia = rd.Booleano("vigilancia"),
        AceptaReserva = rd.Booleano("acepta_reserva"),
        TarifaBase = rd.DobleNulo("tarifa_base") ?? 0,
        Servicios = rd.TextoObligatorio("servicios"),
        Activa = rd.Booleano("activa")
    };

    public async Task<List<Sede>> ListarAsync()
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarAsync($"SELECT {Columnas} FROM sedes ORDER BY nombre", Mapear);
    }

    public async Task<Sede?> PorIdAsync(long id)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync($"SELECT {Columnas} FROM sedes WHERE id = @id LIMIT 1", Mapear, ("@id", id));
    }

    public async Task<Sede?> PorCodigoAsync(string codigo)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync($"SELECT {Columnas} FROM sedes WHERE UPPER(codigo) = @c LIMIT 1", Mapear,
            ("@c", (codigo ?? "").Trim().ToUpperInvariant()));
    }

    /// <summary>Una fila por sede con el total y los libres (globales y por tipo de plaza).</summary>
    public async Task<Dictionary<long, ConteoSede>> ConteosAsync()
    {
        await using var cn = _fabrica.Crear();
        var filas = await cn.ConsultarAsync(
            @"SELECT COALESCE(e.sede_id, 0) AS sede_id,
                     COUNT(e.id) AS total,
                     COALESCE(SUM(e.ocupado = 1), 0) AS ocupados,
                     COALESCE(SUM(CASE WHEN e.ocupado = 0 THEN 1 ELSE 0 END), 0) AS libres,
                     COALESCE(SUM(CASE WHEN e.ocupado = 0 AND e.tipo_vehiculo = 'AUTO'         THEN 1 ELSE 0 END), 0) AS libres_auto,
                     COALESCE(SUM(CASE WHEN e.ocupado = 0 AND e.tipo_vehiculo = 'MOTO'         THEN 1 ELSE 0 END), 0) AS libres_moto,
                     COALESCE(SUM(CASE WHEN e.ocupado = 0 AND e.tipo_vehiculo = 'CAMIONETA'    THEN 1 ELSE 0 END), 0) AS libres_camioneta,
                     COALESCE(SUM(CASE WHEN e.ocupado = 0 AND e.tipo_vehiculo = 'DISCAPACIDAD' THEN 1 ELSE 0 END), 0) AS libres_discapacidad,
                     COALESCE(SUM(CASE WHEN e.ocupado = 0 AND e.tipo_vehiculo = 'EMBARAZADA'   THEN 1 ELSE 0 END), 0) AS libres_embarazada
              FROM espacios e
              GROUP BY COALESCE(e.sede_id, 0)",
            rd => new ConteoSede(
                rd.Long("sede_id"),
                Cuenta(rd, "total"),
                Cuenta(rd, "ocupados"),
                Cuenta(rd, "libres"),
                Cuenta(rd, "libres_auto"),
                Cuenta(rd, "libres_moto"),
                Cuenta(rd, "libres_camioneta"),
                Cuenta(rd, "libres_discapacidad"),
                Cuenta(rd, "libres_embarazada")));

        var mapa = new Dictionary<long, ConteoSede>(filas.Count);
        foreach (var fila in filas)
            mapa[fila.SedeId] = fila;
        return mapa;
    }

    /// <summary>COUNT y SUM llegan como BIGINT o DECIMAL según el servidor: se convierten sin castear.</summary>
    private static int Cuenta(MySqlDataReader rd, string columna) =>
        Convert.ToInt32(Convert.ToInt64(rd.GetValue(rd.GetOrdinal(columna)), System.Globalization.CultureInfo.InvariantCulture));

    public async Task<long> ContarAsync()
    {
        await using var cn = _fabrica.Crear();
        return await cn.EscalarAsync("SELECT COUNT(*) FROM sedes");
    }
}
