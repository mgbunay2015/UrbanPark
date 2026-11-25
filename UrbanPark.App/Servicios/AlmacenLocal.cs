using System.Text.Json;
using UrbanPark.App.Modelos;

namespace UrbanPark.App.Servicios;

/// <summary>
/// Guarda en la base local una copia ordenada de las respuestas importantes y las devuelve
/// cuando no hay internet. La cola de cambios pendientes vive en la misma base.
///
/// La copia genérica (una entrada por ruta) resuelve cualquier consulta repetida; además se
/// desglosan los datos clave en tablas propias para poder reconstruir el QR, la lista de
/// vehículos y las plazas aunque la ruta exacta no se haya pedido antes.
/// </summary>
public static class AlmacenLocal
{
    private static readonly JsonSerializerOptions Opciones = JsonUrbanPark.Opciones;

    public sealed class Operacion
    {
        public string Id { get; set; } = "";
        public string Metodo { get; set; } = "POST";
        public string Ruta { get; set; } = "";
        public string? Cuerpo { get; set; }
        public string Usuario { get; set; } = "";
        public DateTime CreadoEn { get; set; } = DateTime.Now;
    }

    public static int Pendientes => BaseLocal.ContarPendientes();

    // ================================================================ pase del puesto (QR)
    /// <summary>Guarda el pase tal como llegó del servidor: es lo que pinta el QR sin red.</summary>
    public static void GuardarPase(PasePuestoDto pase, string usuario) =>
        BaseLocal.GuardarPase(pase, usuario, Sesion.UsuarioId);

    /// <summary>Pase del puesto guardado en el equipo (null si nunca se vio ese ticket).</summary>
    public static PasePuestoDto? LeerPase(long ticketId) => BaseLocal.LeerPase(ticketId);

    // ================================================================ copia por ruta
    public static void GuardarCache(string clave, string json)
    {
        BaseLocal.GuardarAjuste("cache:" + clave, json);
    }

    public static bool TryGet(string clave, out string json)
    {
        var guardado = BaseLocal.LeerAjuste("cache:" + clave);
        if (!string.IsNullOrWhiteSpace(guardado))
        {
            json = guardado;
            return true;
        }

        json = "";
        return false;
    }

    // ================================================================ cola de cambios
    public static void Encolar(string metodo, string ruta, string? cuerpo, string usuario) =>
        BaseLocal.Encolar(metodo, ruta, cuerpo, usuario, Sesion.UsuarioId);

    public static List<Operacion> PendientesDe(string usuario) =>
        BaseLocal.PendientesDe(usuario, Sesion.UsuarioId)
            .Select(p => new Operacion
            {
                Id = p.Id,
                Metodo = p.Metodo,
                Ruta = p.Ruta,
                Cuerpo = p.Cuerpo,
                Usuario = p.Usuario,
                CreadoEn = p.CreadoEn
            })
            .ToList();

    public static void Quitar(string id) => BaseLocal.QuitarPendiente(id);

    // ================================================================ espejo de datos clave
    /// <summary>
    /// Copia una respuesta correcta del API a la base local. Se llama en cada lectura con
    /// éxito, así la app siempre tiene la última información vista.
    /// </summary>
    public static void Reflejar(string ruta, string json)
    {
        try
        {
            var usuario = Sesion.Username;
            var usuarioId = Sesion.UsuarioId;

            // Pase del puesto (QR).
            if (ruta.StartsWith("/api/estacionar/", StringComparison.OrdinalIgnoreCase) &&
                !ruta.Contains("/activar", StringComparison.OrdinalIgnoreCase) &&
                !ruta.Contains("/cancelar", StringComparison.OrdinalIgnoreCase))
            {
                var pase = JsonSerializer.Deserialize<PasePuestoDto>(json, Opciones);
                if (pase is { TicketId: > 0 })
                    BaseLocal.GuardarPase(pase, usuario, usuarioId);
                return;
            }

            // Vehículos del cliente.
            if (ruta.StartsWith("/api/vehiculos", StringComparison.OrdinalIgnoreCase))
            {
                var lista = JsonSerializer.Deserialize<List<VehiculoDto>>(json, Opciones);
                if (lista is not null) BaseLocal.GuardarVehiculos(lista, usuario, usuarioId);
                return;
            }

            // Parqueaderos.
            if (ruta.StartsWith("/api/sedes", StringComparison.OrdinalIgnoreCase) && !ruta.Contains("/recomendar"))
            {
                var lista = JsonSerializer.Deserialize<List<SedeDto>>(json, Opciones);
                if (lista is not null) BaseLocal.GuardarSedes(lista);
                return;
            }

            // Plazas de una sede (la ruta trae ?sedeId=N).
            if (ruta.StartsWith("/api/espacios", StringComparison.OrdinalIgnoreCase))
            {
                var sedeId = LeerSedeId(ruta);
                var lista = JsonSerializer.Deserialize<List<EspacioDto>>(json, Opciones);
                if (lista is not null && sedeId > 0) BaseLocal.GuardarEspacios(sedeId, lista);
                return;
            }

            // Tickets propios.
            if (ruta.StartsWith("/api/tickets", StringComparison.OrdinalIgnoreCase) &&
                !ruta.Contains("abiertos", StringComparison.OrdinalIgnoreCase))
            {
                var lista = JsonSerializer.Deserialize<List<TicketDto>>(json, Opciones);
                if (lista is not null) BaseLocal.GuardarTickets(lista.Where(t => t.Abierto), usuario, usuarioId);
                return;
            }

            if (ruta.StartsWith("/api/tickets/abiertos", StringComparison.OrdinalIgnoreCase))
            {
                var lista = JsonSerializer.Deserialize<List<TicketDto>>(json, Opciones);
                if (lista is not null) BaseLocal.GuardarTickets(lista, usuario, usuarioId);
            }
        }
        catch (Exception)
        {
            // Una respuesta con formato inesperado no debe romper la consulta en curso.
        }
    }

    /// <summary>
    /// Refleja en SQLite la respuesta de una <b>escritura</b> (POST/PUT/DELETE) que salió bien.
    ///
    /// Es distinto de <see cref="Reflejar"/>: aquí la respuesta suele ser un único objeto, y
    /// pasarla por el reflejo de listas borraría el resto de datos del usuario. Por eso solo se
    /// atiende a lo que se puede guardar sin ambigüedad: el pase del puesto.
    /// </summary>
    public static void ReflejarEscritura(string ruta, string json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            // Abrir/activar un puesto devuelve el pase con su QR: es lo que hace falta para
            // seguir operando sin conexión justo después.
            if (ruta.StartsWith("/api/estacionar/", StringComparison.OrdinalIgnoreCase))
            {
                var pase = JsonSerializer.Deserialize<PasePuestoDto>(json, Opciones);
                if (pase is { TicketId: > 0 })
                {
                    var estado = (pase.Estado ?? "").ToUpperInvariant();
                    // Un ticket anulado ya no sirve como pase vigente.
                    if (estado is "ANULADO" or "CANCELADO") BaseLocal.QuitarPase(pase.TicketId);
                    else BaseLocal.GuardarPase(pase, Sesion.Username);
                }
            }
        }
        catch (Exception ex)
        {
            Registro.Aviso("Local", $"No se pudo guardar la respuesta de {ruta}: {ex.Message}");
        }
    }

    /// <summary>
    /// Último recurso sin internet: si la ruta no estaba en la copia genérica se intenta
    /// reconstruir la respuesta con las tablas de datos clave.
    /// </summary>
    public static bool TryGetEstructurado<T>(string ruta, out T valor)
    {
        valor = default!;
        try
        {
            var usuario = Sesion.Username;
            var usuarioId = Sesion.UsuarioId;

            if (ruta.StartsWith("/api/estacionar/", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(ruta.TrimEnd('/').Split('/').Last(), out var ticketId) &&
                ticketId > 0)
            {
                var pase = BaseLocal.LeerPase(ticketId);
                if (pase is not null && pase is T convertido)
                {
                    valor = convertido;
                    return true;
                }
            }

            if (ruta.StartsWith("/api/vehiculos", StringComparison.OrdinalIgnoreCase) &&
                typeof(T) == typeof(List<VehiculoDto>))
            {
                var lista = BaseLocal.LeerVehiculos(usuario, usuarioId);
                if (lista.Count > 0)
                {
                    valor = (T)(object)lista;
                    return true;
                }
            }

            if (ruta.StartsWith("/api/sedes", StringComparison.OrdinalIgnoreCase) &&
                typeof(T) == typeof(List<SedeDto>))
            {
                var lista = BaseLocal.LeerSedes();
                if (lista.Count > 0)
                {
                    valor = (T)(object)lista;
                    return true;
                }
            }

            if (ruta.StartsWith("/api/espacios", StringComparison.OrdinalIgnoreCase) &&
                typeof(T) == typeof(List<EspacioDto>))
            {
                var sedeId = LeerSedeId(ruta);
                var lista = sedeId > 0 ? BaseLocal.LeerEspacios(sedeId) : [];
                if (lista.Count > 0)
                {
                    valor = (T)(object)lista;
                    return true;
                }
            }

            if (ruta.StartsWith("/api/tickets", StringComparison.OrdinalIgnoreCase) &&
                typeof(T) == typeof(List<TicketDto>))
            {
                var lista = BaseLocal.LeerTickets(usuario, usuarioId);
                if (lista.Count > 0)
                {
                    valor = (T)(object)lista;
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // se deja que el llamador muestre el mensaje de "sin copia"
        }

        return false;
    }

    /// <summary>
    /// Pase del puesto guardado en el equipo. Se usa al abrir el QR sin conexión y tras
    /// reiniciar la app, cuando todavía no se ha podido consultar el servidor.
    /// </summary>
    public static PasePuestoDto? PaseSinRed(long? ticketId = null)
    {
        var usuario = Sesion.Username;
        if (ticketId is > 0)
        {
            var porId = BaseLocal.LeerPase(ticketId.Value);
            if (porId is not null) return porId;
        }

        return BaseLocal.LeerPaseVigente(usuario, Sesion.UsuarioId);
    }

    public static void MarcarFallo(string id, string? error) => BaseLocal.MarcarFallo(id, error);

    public static int ContarPendientes() => BaseLocal.ContarPendientes();

    /// <summary>Borra todo lo del usuario: se usa al cerrar sesión.</summary>
    public static void LimpiarUsuario(string usuario, long usuarioId = 0) =>
        BaseLocal.LimpiarUsuario(usuario, usuarioId);

    /// <summary>Resumen de la base local para la pantalla de ajustes/diagnóstico.</summary>
    public static (int Vehiculos, int Sedes, int Espacios, int Pases, int Tickets, int Pendientes) Estado() =>
        BaseLocal.Resumen();

    private static long LeerSedeId(string ruta)
    {
        var marca = "sedeId=";
        var indice = ruta.IndexOf(marca, StringComparison.OrdinalIgnoreCase);
        if (indice < 0) return 0;

        var inicio = indice + marca.Length;
        var fin = ruta.IndexOf('&', inicio);
        var texto = fin < 0 ? ruta[inicio..] : ruta[inicio..fin];
        return long.TryParse(texto, out var id) ? id : 0;
    }
}
