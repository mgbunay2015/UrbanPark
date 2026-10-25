using MySqlConnector;
using UrbanPark.Api.Data;
using UrbanPark.Api.Modelos;

namespace UrbanPark.Api.Repositorios;

public sealed class BiometriaRepositorio
{
    private const string Columnas = "id, usuario, vehiculo_id, placa, tipo, dispositivo, token_hash, creado_en, (foto_rostro IS NOT NULL) AS tiene_foto";
    private readonly IFabricaConexion _fabrica;

    public BiometriaRepositorio(IFabricaConexion fabrica) => _fabrica = fabrica;

    private static Biometria Mapear(MySqlDataReader rd) => new()
    {
        Id = rd.Long("id"),
        Usuario = rd.TextoObligatorio("usuario"),
        VehiculoId = rd.LongNulo("vehiculo_id"),
        Placa = rd.TextoObligatorio("placa"),
        Tipo = rd.TextoObligatorio("tipo"),
        Dispositivo = rd.Texto("dispositivo"),
        TokenHash = rd.TextoObligatorio("token_hash"),
        CreadoEn = rd.Fecha("creado_en"),
        FotoRostro = rd.Long("tiene_foto") > 0 ? [1] : null
    };

    public async Task<List<Biometria>> ListarPorUsuarioAsync(string usuario)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarAsync(
            $"SELECT {Columnas} FROM biometrias WHERE usuario = @u ORDER BY creado_en DESC",
            Mapear, ("@u", usuario));
    }

    public async Task<Biometria?> PorVehiculoAsync(long vehiculoId)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync(
            $"SELECT {Columnas} FROM biometrias WHERE vehiculo_id = @id LIMIT 1",
            Mapear, ("@id", vehiculoId));
    }

    public async Task<long> GuardarAsync(string usuario, long? vehiculoId, string placa, string tipo,
        string tokenHash, string? dispositivo, byte[]? foto)
    {
        await using var cn = _fabrica.Crear();
        if (vehiculoId is > 0)
            await cn.EjecutarAsync("DELETE FROM biometrias WHERE vehiculo_id = @id", ("@id", vehiculoId.Value));
        else
            await cn.EjecutarAsync(
                "DELETE FROM biometrias WHERE usuario = @u AND (vehiculo_id IS NULL OR placa = 'LOGIN') AND dispositivo <=> @d",
                ("@u", usuario), ("@d", dispositivo));

        await cn.EjecutarAsync(
            @"INSERT INTO biometrias (usuario, vehiculo_id, placa, tipo, dispositivo, token_hash, foto_rostro, creado_en)
              VALUES (@u, @v, @p, @t, @d, @h, @f, @c)",
            ("@u", usuario), ("@v", vehiculoId), ("@p", placa), ("@t", tipo),
            ("@d", dispositivo), ("@h", tokenHash), ("@f", foto), ("@c", DateTime.Now));

        return await cn.EscalarAsync("SELECT LAST_INSERT_ID()");
    }

    public async Task<bool> TokenValidoAsync(string usuario, string tokenHash)
    {
        await using var cn = _fabrica.Crear();
        var n = await cn.EscalarAsync(
            "SELECT COUNT(*) FROM biometrias WHERE usuario = @u AND token_hash = @h",
            ("@u", usuario), ("@h", tokenHash));
        return n > 0;
    }

    public async Task<Biometria?> PorTokenHashAsync(string tokenHash)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync(
            $"SELECT {Columnas} FROM biometrias WHERE token_hash = @h ORDER BY creado_en DESC LIMIT 1",
            Mapear, ("@h", tokenHash));
    }
}
