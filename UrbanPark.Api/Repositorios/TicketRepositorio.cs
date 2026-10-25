using MySqlConnector;
using UrbanPark.Api.Data;
using UrbanPark.Api.Modelos;

namespace UrbanPark.Api.Repositorios;

public sealed class TicketRepositorio
{
    private const string Columnas =
        "id, codigo, placa, espacio_id, espacio_codigo, zona, entrada, salida, estado, tarifa_hora, monto_total, monto_descuento, usuario, liberacion_pendiente, modo, salida_prevista";

    private readonly IFabricaConexion _fabrica;

    public TicketRepositorio(IFabricaConexion fabrica) => _fabrica = fabrica;

    private static Ticket Mapear(MySqlDataReader rd) => new()
    {
        Id = rd.Long("id"),
        Codigo = rd.TextoObligatorio("codigo"),
        Placa = rd.TextoObligatorio("placa"),
        EspacioId = rd.Long("espacio_id"),
        EspacioCodigo = rd.Texto("espacio_codigo"),
        Zona = rd.Texto("zona"),
        Entrada = rd.Fecha("entrada"),
        Salida = rd.FechaNula("salida"),
        Estado = rd.TextoObligatorio("estado"),
        TarifaHora = rd.DobleNulo("tarifa_hora"),
        MontoTotal = rd.DobleNulo("monto_total"),
        MontoDescuento = rd.DobleNulo("monto_descuento") ?? 0,
        Usuario = rd.Texto("usuario"),
        LiberacionPendiente = rd.Booleano("liberacion_pendiente"),
        Modo = rd.Texto("modo") ?? "AHORA",
        SalidaPrevista = rd.FechaNula("salida_prevista")
    };

    public async Task<List<Ticket>> ListarAsync()
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarAsync($"SELECT {Columnas} FROM tickets ORDER BY entrada DESC", Mapear);
    }

    public async Task<List<Ticket>> ListarPorUsuarioAsync(string username)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarAsync(
            $"SELECT {Columnas} FROM tickets WHERE usuario = @u ORDER BY entrada DESC",
            Mapear, ("@u", username));
    }

    public async Task<List<Ticket>> AbiertosAsync()
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarAsync(
            $"SELECT {Columnas} FROM tickets WHERE estado IN ('ABIERTO', 'RESERVADO') ORDER BY entrada ASC", Mapear);
    }

    public async Task<List<Ticket>> AbiertosPorUsuarioAsync(string username)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarAsync(
            $"SELECT {Columnas} FROM tickets WHERE estado IN ('ABIERTO', 'RESERVADO') AND usuario = @u ORDER BY entrada ASC",
            Mapear, ("@u", username));
    }

    public async Task<Ticket?> PorIdAsync(long id)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync($"SELECT {Columnas} FROM tickets WHERE id = @id LIMIT 1", Mapear, ("@id", id));
    }

    public async Task<Ticket?> AbiertoPorPlacaAsync(string placa)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync(
            $"SELECT {Columnas} FROM tickets WHERE estado IN ('ABIERTO', 'RESERVADO') AND UPPER(placa) = @p LIMIT 1",
            Mapear, ("@p", placa.ToUpperInvariant()));
    }

    public async Task<Ticket?> AbiertoPorEspacioAsync(long espacioId)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarUnoAsync(
            $"SELECT {Columnas} FROM tickets WHERE estado IN ('ABIERTO', 'RESERVADO') AND espacio_id = @e LIMIT 1",
            Mapear, ("@e", espacioId));
    }

    public async Task<List<Ticket>> PendientesLiberacionAsync()
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarAsync(
            $"SELECT {Columnas} FROM tickets WHERE liberacion_pendiente = 1", Mapear);
    }

    public async Task<long> CrearAsync(string placa, long espacioId, string? espacioCodigo, string? zona,
        DateTime entrada, double? tarifaHora, string? usuario, string modo = "AHORA", DateTime? salidaPrevista = null)
    {
        await using var cn = _fabrica.Crear();
        var codigo = Reglas.NuevoCodigoTicket();
        var estado = string.Equals(modo, "RESERVA", StringComparison.OrdinalIgnoreCase) ? "RESERVADO" : "ABIERTO";

        await cn.EjecutarAsync(
            @"INSERT INTO tickets (codigo, placa, espacio_id, espacio_codigo, zona, entrada, salida, estado,
                                   tarifa_hora, monto_total, usuario, liberacion_pendiente, modo, salida_prevista)
              VALUES (@c, @p, @e, @ec, @z, @en, NULL, @es, @th, NULL, @u, 0, @mo, @sp)",
            ("@c", codigo), ("@p", placa), ("@e", espacioId), ("@ec", espacioCodigo), ("@z", zona),
            ("@en", entrada), ("@th", tarifaHora), ("@u", usuario), ("@es", estado),
            ("@mo", modo), ("@sp", salidaPrevista));

        return await cn.EscalarAsync("SELECT id FROM tickets WHERE codigo = @c LIMIT 1", ("@c", codigo));
    }

    /// <summary>Convierte una reserva en una estancia activa (el cliente ya llegó al puesto).</summary>
    public async Task<bool> ActivarAsync(long id, DateTime entrada)
    {
        await using var cn = _fabrica.Crear();
        var filas = await cn.EjecutarAsync(
            @"UPDATE tickets SET estado = 'ABIERTO', entrada = @en
              WHERE id = @id AND estado = 'RESERVADO'",
            ("@en", entrada), ("@id", id));
        return filas > 0;
    }

    public async Task CerrarAsync(long id, DateTime salida, double monto, double descuento, bool liberacionPendiente)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync(
            @"UPDATE tickets SET salida = @s, estado = 'CERRADO', monto_total = @m, monto_descuento = @d, liberacion_pendiente = @lp
              WHERE id = @id",
            ("@s", salida), ("@m", monto), ("@d", descuento), ("@lp", liberacionPendiente), ("@id", id));
    }

    public async Task MarcarPendienteAsync(long id, bool pendiente)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync("UPDATE tickets SET liberacion_pendiente = @p WHERE id = @id",
            ("@p", pendiente), ("@id", id));
    }

    /// <summary>
    /// Anula una reserva recien creada (el cliente eligio el puesto por error).
    /// No se cobra nada: el ticket queda ANULADO con monto 0 para que no entre en la caja.
    /// </summary>
    public async Task AnularAsync(long id, DateTime salida)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync(
            @"UPDATE tickets SET salida = @s, estado = 'ANULADO', monto_total = 0, monto_descuento = 0
              WHERE id = @id AND estado IN ('ABIERTO', 'RESERVADO')",
            ("@s", salida), ("@id", id));
    }

    public async Task LimpiarPendientesDePlazaAsync(long espacioId)
    {
        await using var cn = _fabrica.Crear();
        await cn.EjecutarAsync("UPDATE tickets SET liberacion_pendiente = 0 WHERE espacio_id = @e AND liberacion_pendiente = 1",
            ("@e", espacioId));
    }

    /// <summary>Tickets cuyo rango de ENTRADA cae dentro del periodo (mismo criterio que el reporte original).</summary>
    public async Task<List<Ticket>> EnPeriodoAsync(DateTime desde, DateTime hasta)
    {
        await using var cn = _fabrica.Crear();
        return await cn.ConsultarAsync(
            $"SELECT {Columnas} FROM tickets WHERE entrada >= @d AND entrada <= @h ORDER BY entrada DESC",
            Mapear, ("@d", desde), ("@h", hasta));
    }

    public async Task<int> ContarPorEstadoAsync(string estado)
    {
        await using var cn = _fabrica.Crear();
        return (int)await cn.EscalarAsync("SELECT COUNT(*) FROM tickets WHERE estado = @e", ("@e", estado));
    }
}
