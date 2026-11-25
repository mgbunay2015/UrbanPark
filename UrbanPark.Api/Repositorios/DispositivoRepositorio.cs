using MySqlConnector;
using UrbanPark.Api.Data;

namespace UrbanPark.Api.Repositorios;

/// <summary>Un equipo vinculado a una cuenta con el autenticador de Google.</summary>
public sealed record Dispositivo(long Id, string Usuario, string TokenHash, DateTime CreadoEn, DateTime? UltimoUso);

/// <summary>
/// Equipos que ya hicieron login completo (usuario + contraseña + código) y quedaron
/// vinculados: desde ese equipo se puede entrar escribiendo solo el código del autenticador.
/// Del token solo se guarda el hash SHA-256.
/// </summary>
public sealed class DispositivoRepositorio
{
    private const string Columnas = "id, usuario, token_hash, creado_en, ultimo_uso";
    private readonly IFabricaConexion _fabrica;

    public DispositivoRepositorio(IFabricaConexion fabrica) => _fabrica = fabrica;

    private static Dispositivo Mapear(MySqlDataReader rd) => new(
        rd.Long("id"),
        rd.TextoObligatorio("usuario"),
        rd.TextoObligatorio("token_hash"),
        rd.Fecha("creado_en"),
        rd.FechaNula("ultimo_uso"));

    public async Task<Dispositivo?> PorTokenAsync(string tokenHash)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync($"SELECT {Columnas} FROM dispositivos WHERE token_hash = @t LIMIT 1",
            Mapear, ("@t", tokenHash));
    }

    public async Task<List<Dispositivo>> ListarPorUsuarioAsync(string usuario)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarAsync(
            $"SELECT {Columnas} FROM dispositivos WHERE usuario = @u ORDER BY creado_en DESC",
            Mapear, ("@u", usuario));
    }

    /// <summary>Vincula el equipo. Devuelve false si ese token ya estaba guardado.</summary>
    public async Task<bool> CrearAsync(string usuario, string tokenHash)
    {
        await using var cn = _fabrica.Crear();
        var filas = await cn.EjecutarAsync(
            @"INSERT IGNORE INTO dispositivos (usuario, token_hash, creado_en, ultimo_uso)
              VALUES (@u, @t, @c, @c)",
            ("@u", usuario), ("@t", tokenHash), ("@c", DateTime.Now));
        return filas > 0;
    }

    /// <summary>Marca el último uso del equipo.</summary>
    public async Task MarcarUsoAsync(long id)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync("UPDATE dispositivos SET ultimo_uso = @u WHERE id = @id",
            ("@u", DateTime.Now), ("@id", id));
    }

    /// <summary>Borra todos los equipos de un usuario (al desactivar el autenticador).</summary>
    public async Task<int> BorrarPorUsuarioAsync(string usuario)
    {
        await using var cn = _fabrica.Crear();
        return await cn.EjecutarAsync("DELETE FROM dispositivos WHERE usuario = @u", ("@u", usuario));
    }
}
