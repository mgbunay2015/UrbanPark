using MySqlConnector;
using UrbanPark.Api.Data;
using UrbanPark.Api.Modelos;

namespace UrbanPark.Api.Repositorios;

public sealed class UsuarioRepositorio
{
    private const string Columnas = "id, username, email, first_name, last_name, password_hash, roles, enabled, google_sub, creado_en, totp_secret, totp_activo";
    private readonly IFabricaConexion _fabrica;

    public UsuarioRepositorio(IFabricaConexion fabrica) => _fabrica = fabrica;

    private static Usuario Mapear(MySqlDataReader rd) => new()
    {
        Id = rd.Long("id"),
        Username = rd.TextoObligatorio("username"),
        Email = rd.TextoObligatorio("email"),
        FirstName = rd.TextoObligatorio("first_name"),
        LastName = rd.TextoObligatorio("last_name"),
        PasswordHash = rd.TextoObligatorio("password_hash"),
        Roles = rd.TextoObligatorio("roles"),
        Enabled = rd.Booleano("enabled"),
        GoogleSub = rd.Texto("google_sub"),
        CreadoEn = rd.Fecha("creado_en"),
        TotpSecret = rd.Texto("totp_secret"),
        TotpActivo = rd.Booleano("totp_activo")
    };

    public async Task<Usuario?> PorUsernameAsync(string username)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync($"SELECT {Columnas} FROM usuarios WHERE username = @u LIMIT 1", Mapear,
            ("@u", username));
    }

    public async Task<Usuario?> PorIdAsync(long id)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync($"SELECT {Columnas} FROM usuarios WHERE id = @id LIMIT 1", Mapear, ("@id", id));
    }

    public async Task<Usuario?> PorEmailAsync(string email)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync($"SELECT {Columnas} FROM usuarios WHERE email = @e LIMIT 1", Mapear, ("@e", email));
    }

    public async Task<Usuario?> PorGoogleSubAsync(string sub)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync($"SELECT {Columnas} FROM usuarios WHERE google_sub = @s LIMIT 1", Mapear, ("@s", sub));
    }

    public async Task<List<Usuario>> ListarAsync()
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarAsync($"SELECT {Columnas} FROM usuarios ORDER BY username", Mapear);
    }

    public async Task<long> CrearAsync(string username, string email, string firstName, string lastName,
        string passwordHash, string roles, bool enabled = true, string? googleSub = null)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync(
            @"INSERT INTO usuarios (username, email, first_name, last_name, password_hash, roles, enabled, google_sub, creado_en)
              VALUES (@u, @e, @fn, @ln, @ph, @ro, @en, @gs, @cr)",
            ("@u", username), ("@e", email), ("@fn", firstName), ("@ln", lastName),
            ("@ph", passwordHash), ("@ro", roles), ("@en", enabled), ("@gs", googleSub), ("@cr", DateTime.Now));

        return await cn.EscalarAsync("SELECT id FROM usuarios WHERE username = @u LIMIT 1", ("@u", username));
    }

    public async Task ActualizarAsync(long id, string email, string firstName, string lastName, bool enabled)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync(
            "UPDATE usuarios SET email = @e, first_name = @fn, last_name = @ln, enabled = @en WHERE id = @id",
            ("@e", email), ("@fn", firstName), ("@ln", lastName), ("@en", enabled), ("@id", id));
    }

    public async Task ActualizarRolesAsync(long id, string roles)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync("UPDATE usuarios SET roles = @ro WHERE id = @id", ("@ro", roles), ("@id", id));
    }

    public async Task ActualizarPasswordAsync(long id, string passwordHash)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync("UPDATE usuarios SET password_hash = @ph WHERE id = @id", ("@ph", passwordHash), ("@id", id));
    }

    public async Task VincularGoogleAsync(long id, string googleSub)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync("UPDATE usuarios SET google_sub = @s WHERE id = @id", ("@s", googleSub), ("@id", id));
    }


    /// <summary>Guarda el secreto del autenticador. activo = 0 hasta que el usuario confirme un código.</summary>
    public async Task GuardarTotpAsync(long id, string? secreto, bool activo)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync("UPDATE usuarios SET totp_secret = @s, totp_activo = @a WHERE id = @id",
            ("@s", secreto), ("@a", activo), ("@id", id));
    }
    public async Task<int> ContarAsync()
    {
        await using var cn = _fabrica.Crear();
        return (int)await cn.EscalarAsync("SELECT COUNT(*) FROM usuarios");
    }

    public async Task<bool> ExisteUsernameOEmailAsync(string? username, string? email)
    {
        await using var cn = _fabrica.Crear();
        var n = await cn.EscalarAsync(
            "SELECT COUNT(*) FROM usuarios WHERE username = @u OR email = @e",
            ("@u", username ?? ""), ("@e", email ?? ""));
        return n > 0;
    }
}
