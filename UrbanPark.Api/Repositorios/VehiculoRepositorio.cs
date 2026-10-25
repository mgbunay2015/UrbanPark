using MySqlConnector;
using UrbanPark.Api.Data;
using UrbanPark.Api.Modelos;

namespace UrbanPark.Api.Repositorios;

public sealed class VehiculoRepositorio
{
    private const string Columnas = "id, placa, tipo, marca, color, propietario, propietario_email, propietario_username, descuento, activo";
    private readonly IFabricaConexion _fabrica;

    public VehiculoRepositorio(IFabricaConexion fabrica) => _fabrica = fabrica;

    private static Vehiculo Mapear(MySqlDataReader rd) => new()
    {
        Id = rd.Long("id"),
        Placa = rd.TextoObligatorio("placa"),
        Tipo = rd.TextoObligatorio("tipo"),
        Marca = rd.Texto("marca"),
        Color = rd.Texto("color"),
        Propietario = rd.TextoObligatorio("propietario"),
        Descuento = rd.EnteroNulo("descuento") ?? 0,
        PropietarioEmail = rd.Texto("propietario_email"),
        PropietarioUsername = rd.Texto("propietario_username"),
        Activo = rd.Booleano("activo")
    };

    public async Task<List<Vehiculo>> ListarAsync()
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarAsync($"SELECT {Columnas} FROM vehiculos ORDER BY placa", Mapear);
    }

    public async Task<List<Vehiculo>> ListarPorPropietarioAsync(string username)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarAsync(
            $"SELECT {Columnas} FROM vehiculos WHERE propietario_username = @u ORDER BY placa",
            Mapear, ("@u", username));
    }

    public async Task<Vehiculo?> PorIdAsync(long id)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync($"SELECT {Columnas} FROM vehiculos WHERE id = @id LIMIT 1", Mapear, ("@id", id));
    }

    public async Task<Vehiculo?> PorPlacaAsync(string placa)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync(
            $"SELECT {Columnas} FROM vehiculos WHERE UPPER(placa) = @p LIMIT 1",
            Mapear, ("@p", placa.ToUpperInvariant()));
    }

    public async Task<long> CrearAsync(string placa, string tipo, string? marca, string? color,
        string propietario, string? propietarioEmail, string? propietarioUsername)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync(
            @"INSERT INTO vehiculos (placa, tipo, marca, color, propietario, propietario_email, propietario_username, descuento)
              VALUES (@p, @t, @m, @c, @pr, @pe, @pu, @d)",
            ("@p", placa), ("@t", tipo), ("@m", marca), ("@c", color),
            ("@pr", propietario), ("@pe", propietarioEmail), ("@pu", propietarioUsername),
            ("@d", Reglas.DescuentoDe(propietario)));

        return await cn.EscalarAsync("SELECT id FROM vehiculos WHERE placa = @p LIMIT 1", ("@p", placa));
    }

    /// <summary>Actualiza el vehiculo. La placa NUNCA se modifica (igual que el original).</summary>
    public async Task ActualizarAsync(long id, string tipo, string? marca, string? color, string propietario,
        string? propietarioEmail, string? propietarioUsername)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync(
            @"UPDATE vehiculos SET tipo = @t, marca = @m, color = @c, propietario = @pr,
                     propietario_email = @pe, propietario_username = @pu, descuento = @d
              WHERE id = @id",
            ("@t", tipo), ("@m", marca), ("@c", color), ("@pr", propietario),
            ("@pe", propietarioEmail), ("@pu", propietarioUsername),
            ("@d", Reglas.DescuentoDe(propietario)), ("@id", id));
    }

    public async Task CambiarActivoAsync(long id, bool activo)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync("UPDATE vehiculos SET activo = @a WHERE id = @id", ("@a", activo), ("@id", id));
    }

    public async Task EliminarAsync(long id)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync("DELETE FROM vehiculos WHERE id = @id", ("@id", id));
    }

    public async Task<int> ContarAsync()
    {
        await using var cn = _fabrica.Crear();
        return (int)await cn.EscalarAsync("SELECT COUNT(*) FROM vehiculos");
    }
}
