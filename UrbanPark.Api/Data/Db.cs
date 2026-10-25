using MySqlConnector;

namespace UrbanPark.Api.Data;

/// <summary>Fabrica de conexiones MySQL (una por peticion).</summary>
public interface IFabricaConexion
{
    MySqlConnection Crear();
}

public sealed class FabricaConexion : IFabricaConexion
{
    private readonly string _cadena;

    public FabricaConexion(IConfiguration configuracion)
    {
        _cadena = configuracion.GetConnectionString("UrbanPark")
                  ?? throw new InvalidOperationException("Falta ConnectionStrings:UrbanPark en appsettings.json");
    }

    public MySqlConnection Crear() => new(_cadena);
}

/// <summary>Helpers minimos de acceso a datos (sin ORM: el SQL es explicito y verificable).</summary>
public static class DbExtensiones
{
    public static async Task AbrirAsync(this MySqlConnection cn)
    {
        if (cn.State != System.Data.ConnectionState.Open)
            await cn.OpenAsync();
    }

    private static MySqlCommand Comando(MySqlConnection cn, string sql, (string nombre, object? valor)[] ps)
    {
        var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (nombre, valor) in ps)
            cmd.Parameters.AddWithValue(nombre, valor ?? DBNull.Value);
        return cmd;
    }

    public static async Task<List<T>> ConsultarAsync<T>(this MySqlConnection cn, string sql,
        Func<MySqlDataReader, T> map, params (string nombre, object? valor)[] ps)
    {
        await cn.AbrirAsync();
        await using var cmd = Comando(cn, sql, ps);
        var lista = new List<T>();
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lista.Add(map(rd));
        return lista;
    }

    public static async Task<T?> ConsultarUnoAsync<T>(this MySqlConnection cn, string sql,
        Func<MySqlDataReader, T> map, params (string nombre, object? valor)[] ps) where T : class
    {
        var lista = await cn.ConsultarAsync(sql, map, ps);
        return lista.Count > 0 ? lista[0] : null;
    }

    public static async Task<int> EjecutarAsync(this MySqlConnection cn, string sql,
        params (string nombre, object? valor)[] ps)
    {
        await cn.AbrirAsync();
        await using var cmd = Comando(cn, sql, ps);
        return await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<long> EscalarAsync(this MySqlConnection cn, string sql,
        params (string nombre, object? valor)[] ps)
    {
        await cn.AbrirAsync();
        await using var cmd = Comando(cn, sql, ps);
        var valor = await cmd.ExecuteScalarAsync();
        return valor is null || valor is DBNull ? 0L : Convert.ToInt64(valor);
    }

    // ---- lectores tolerantes a NULL ----
    public static string? Texto(this MySqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? null : rd.GetString(i);
    }

    public static string TextoObligatorio(this MySqlDataReader rd, string columna) => rd.Texto(columna) ?? "";

    public static int? EnteroNulo(this MySqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? null : rd.GetInt32(i);
    }

    public static long Long(this MySqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? 0L : rd.GetInt64(i);
    }

    public static long? LongNulo(this MySqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? null : rd.GetInt64(i);
    }

    public static double? DobleNulo(this MySqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? null : rd.GetDouble(i);
    }

    public static bool Booleano(this MySqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return !rd.IsDBNull(i) && rd.GetBoolean(i);
    }

    public static DateTime Fecha(this MySqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? default : rd.GetDateTime(i);
    }

    public static DateTime? FechaNula(this MySqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? null : rd.GetDateTime(i);
    }

    public static byte[]? Bytes(this MySqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        if (rd.IsDBNull(i)) return null;
        var largo = rd.GetBytes(i, 0, null, 0, 0);
        var buffer = new byte[largo];
        rd.GetBytes(i, 0, buffer, 0, (int)largo);
        return buffer;
    }
}
