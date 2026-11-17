using MySqlConnector;
using UrbanPark.Api.Data;
using UrbanPark.Api.Modelos;

namespace UrbanPark.Api.Repositorios;

public sealed class VehiculoRepositorio
{
    private const string Columnas = "id, placa, tipo, marca, color, propietario, propietario_email, propietario_username, propietario_usuario_id, descuento, activo";
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
        PropietarioUsuarioId = rd.LongNulo("propietario_usuario_id"),
        Activo = rd.Booleano("activo")
    };

    public async Task<List<Vehiculo>> ListarAsync()
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarAsync($"SELECT {Columnas} FROM vehiculos ORDER BY placa", Mapear);
    }

    /// <summary>Sobrecarga para llamadas que solo tienen el username (pruebas y tareas internas).</summary>
    public Task<List<Vehiculo>> ListarPorPropietarioAsync(string username) =>
        ListarPorPropietarioAsync(0, username);

    /// <summary>
    /// Vehiculos del duenno: por id de usuario y, para filas anteriores a la columna
    /// propietario_usuario_id, por username del propietario. Sin id en el token (version
    /// anterior) se compara solo el username.
    /// </summary>
    public async Task<List<Vehiculo>> ListarPorPropietarioAsync(long usuarioId, string username)
    {
        await using var cn = _fabrica.Crear();

        if (usuarioId <= 0)
            return await cn.ConsultarAsync(
                $"SELECT {Columnas} FROM vehiculos WHERE propietario_username = @u ORDER BY placa",
                Mapear, ("@u", username));

        return await cn.ConsultarAsync(
            $@"SELECT {Columnas} FROM vehiculos
                WHERE propietario_usuario_id = @uid
                   OR (propietario_usuario_id IS NULL AND propietario_username = @u)
                ORDER BY placa",
            Mapear, ("@uid", usuarioId), ("@u", username));
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
        string propietario, string? propietarioEmail, string? propietarioUsername, long? propietarioUsuarioId = null)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync(
            @"INSERT INTO vehiculos (placa, tipo, marca, color, propietario, propietario_email, propietario_username,
                                     propietario_usuario_id, descuento)
              VALUES (@p, @t, @m, @c, @pr, @pe, @pu, @puid, @d)",
            ("@p", placa), ("@t", tipo), ("@m", marca), ("@c", color),
            ("@pr", propietario), ("@pe", propietarioEmail), ("@pu", propietarioUsername),
            ("@puid", propietarioUsuarioId), ("@d", Reglas.DescuentoDe(propietario)));

        return await cn.EscalarAsync("SELECT id FROM vehiculos WHERE placa = @p LIMIT 1", ("@p", placa));
    }

    /// <summary>
    /// Cambia el duenno del vehiculo. Se usa cuando el ADMIN reasigna un vehiculo a otra cuenta:
    /// sin esto el usuario nuevo no lo veria (el filtro mira propietario_usuario_id).
    /// </summary>
    public async Task CambiarPropietarioAsync(long id, string? propietarioUsername, long? propietarioUsuarioId)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync(
            "UPDATE vehiculos SET propietario_username = @pu, propietario_usuario_id = @puid WHERE id = @id",
            ("@pu", propietarioUsername), ("@puid", propietarioUsuarioId), ("@id", id));
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
