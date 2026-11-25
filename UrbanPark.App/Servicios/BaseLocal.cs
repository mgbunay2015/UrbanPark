using System.Text.Json;
using System.Threading;
using SQLite;
using UrbanPark.App.Modelos;

namespace UrbanPark.App.Servicios;

/// <summary>
/// Base de datos del equipo en SQLite (<c>urbanpark.db3</c>). Guarda una copia de lo
/// indispensable para que la app siga sirviendo sin internet: el pase del puesto con su QR,
/// los vehículos, las sedes y plazas, los tickets abiertos y la cola de cambios pendientes.
///
/// <para>
/// Si SQLite no está disponible en un equipo, todas las operaciones se vuelven no-ops en lugar
/// de lanzar excepciones: la app sigue funcionando contra el servidor como antes.
/// </para>
///
/// Todo el acceso se hace dentro de un candado: SQLite no admite escrituras concurrentes y la
/// pantalla del QR refresca cada minuto mientras otras pantallas también consultan.
/// </summary>
public static class BaseLocal
{
    private static readonly Lock Candado = new();
    private static SQLiteConnection? _db;
    private static bool _iniciada;

    /// <summary>Nombre del archivo en la carpeta privada de la app.</summary>
    public const string NombreArchivo = "urbanpark.db3";

    /// <summary>Ruta completa de la base local (útil para diagnóstico).</summary>
    public static string Ruta => Path.Combine(FileSystem.AppDataDirectory, NombreArchivo);

    /// <summary>Motivo por el que la base local no está disponible (null si todo va bien).</summary>
    public static string? ErrorInicio { get; private set; }

    /// <summary>
    /// Crea el archivo y las tablas la primera vez. Seguro de llamar muchas veces y nunca lanza:
    /// si SQLite falla, la app continúa solo con el servidor.
    /// </summary>
    public static void Iniciar()
    {
        lock (Candado)
        {
            if (_iniciada && _db is not null) return;

            // El proveedor nativo (e_sqlite3) debe registrarse antes de abrir la conexión.
            // Sin esto, SQLite responde "not an error" en lugar de crear las tablas.
            try
            {
                SQLitePCL.Batteries_V2.Init();
            }
            catch (Exception ex)
            {
                ErrorInicio ??= $"No se pudo inicializar SQLite: {ex.Message}";
                Registro.Error("BaseLocal", "No se pudo registrar el proveedor de SQLite.", ex);
            }

            try
            {
                Directory.CreateDirectory(FileSystem.AppDataDirectory);
                var conexion = new SQLiteConnection(Ruta);
                conexion.CreateTable<AjusteLocal>();
                conexion.CreateTable<PaseLocal>();
                conexion.CreateTable<VehiculoLocal>();
                conexion.CreateTable<SedeLocal>();
                conexion.CreateTable<EspacioLocal>();
                conexion.CreateTable<TicketLocal>();
                conexion.CreateTable<PendienteLocal>();

                // Migracion del filtro por usuario: sqlite-net agrega la columna usuario_id a las
                // tablas que ya existian, pero deja las filas viejas en NULL. Se normalizan a 0
                // para que la lectura (long) no falle y para que el respaldo por username aplique.
                foreach (var tabla in new[] { "pases", "vehiculos", "tickets", "pendientes" })
                {
                    try { conexion.Execute($"UPDATE {tabla} SET usuario_id = 0 WHERE usuario_id IS NULL"); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"No se pudo migrar {tabla}: {ex.Message}"); }
                }

                // No se ejecuta ningún PRAGMA: con sqlite-net 1.11 sobre SQLitePCLRaw 3.x,
                // "PRAGMA journal_mode=WAL" lanza SQLiteException con el mensaje "not an error"
                // (el PRAGMA devuelve una fila y sqlite-net lo trata como error). El diario por
                // defecto de SQLite es suficiente para esta app.

                _db = conexion;
                _iniciada = true;
                ErrorInicio = null;
            }
            catch (Exception ex)
            {
                _db = null;
                _iniciada = false;
                ErrorInicio = $"No se pudo abrir la base local: {ex.Message}";
                Registro.Error("BaseLocal", "No se pudo abrir la base local SQLite.", ex);
            }
        }
    }

    /// <summary>True si la base local se abrió y las tablas existen.</summary>
    public static bool Disponible
    {
        get
        {
            Iniciar();
            return _db is not null;
        }
    }

    /// <summary>Conexión lista, o null si la base no está disponible.</summary>
    private static SQLiteConnection? Db
    {
        get
        {
            Iniciar();
            return _db;
        }
    }

    // ================================================================ ajustes
    public static void GuardarAjuste(string clave, string? valor)
    {
        lock (Candado)
        {
            Db?.InsertOrReplace(new AjusteLocal { Clave = clave, Valor = valor, Actualizado = DateTime.Now });
        }
    }

    public static string? LeerAjuste(string clave)
    {
        lock (Candado)
        {
            return Db?.Find<AjusteLocal>(clave)?.Valor;
        }
    }

    // ================================================================ pases (QR)
    /// <summary>Guarda el pase tal como llegó del servidor: es lo que pinta el QR sin red.</summary>
    public static void GuardarPase(PasePuestoDto pase, string usuario, long usuarioId = 0)
    {
        if (pase is null || pase.TicketId <= 0) return;
        lock (Candado)
        {
            Db?.InsertOrReplace(new PaseLocal
            {
                TicketId = pase.TicketId,
                Usuario = usuario ?? "",
                UsuarioId = usuarioId,
                Estado = pase.Estado ?? "",
                Json = JsonSerializer.Serialize(pase, JsonUrbanPark.Opciones),
                GuardadoEn = DateTime.Now
            });
        }
    }

    public static PasePuestoDto? LeerPase(long ticketId)
    {
        lock (Candado)
        {
            var fila = Db?.Find<PaseLocal>(ticketId);
            return fila is null ? null : Deserializar<PasePuestoDto>(fila.Json);
        }
    }

    /// <summary>
    /// Pase abierto (o reserva) más reciente del usuario, para abrir el QR sin red.
    /// Se busca por id del duenno y, en filas guardadas por versiones anteriores, por username.
    /// </summary>
    public static PasePuestoDto? LeerPaseVigente(string usuario, long usuarioId = 0)
    {
        lock (Candado)
        {
            var db = Db;
            if (db is null) return null;

            var propias = usuarioId > 0
                ? db.Table<PaseLocal>().Where(p => p.UsuarioId == usuarioId).ToList()
                    .Concat(db.Table<PaseLocal>().Where(p => p.UsuarioId == 0 && p.Usuario == usuario).ToList())
                : db.Table<PaseLocal>().Where(p => p.Usuario == usuario).ToList();

            var fila = propias
                .OrderByDescending(p => p.GuardadoEn)
                .Take(10)
                .FirstOrDefault(p => p.Estado is "ABIERTO" or "RESERVADO");

            fila ??= propias.OrderByDescending(p => p.GuardadoEn).FirstOrDefault();

            return fila is null ? null : Deserializar<PasePuestoDto>(fila.Json);
        }
    }

    public static void QuitarPase(long ticketId)
    {
        lock (Candado)
        {
            Db?.Delete<PaseLocal>(ticketId);
        }
    }

    // ================================================================ vehículos
    public static void GuardarVehiculos(IEnumerable<VehiculoDto> vehiculos, string usuario, long usuarioId = 0)
    {
        var lista = vehiculos?.ToList() ?? [];
        lock (Candado)
        {
            var db = Db;
            if (db is null) return;

            db.Execute("DELETE FROM vehiculos WHERE usuario = ?", usuario ?? "");
            foreach (var v in lista)
            {
                db.InsertOrReplace(new VehiculoLocal
                {
                    Placa = v.Placa,
                    Usuario = usuario ?? "",
                    UsuarioId = usuarioId,
                    Json = JsonSerializer.Serialize(v, JsonUrbanPark.Opciones),
                    Activo = v.Activo,
                    GuardadoEn = DateTime.Now
                });
            }
        }
    }

    public static List<VehiculoDto> LeerVehiculos(string usuario, long usuarioId = 0)
    {
        lock (Candado)
        {
            var db = Db;
            if (db is null) return [];

            // sqlite-net traduce comparaciones simples; el ternario se resuelve fuera de la consulta.
            var filas = usuarioId > 0
                ? db.Table<VehiculoLocal>().Where(v => v.UsuarioId == usuarioId).ToList()
                    .Concat(db.Table<VehiculoLocal>().Where(v => v.UsuarioId == 0 && v.Usuario == usuario).ToList())
                : db.Table<VehiculoLocal>().Where(v => v.Usuario == usuario).ToList();

            return filas
                .DistinctBy(v => v.Placa)
                .Select(v => Deserializar<VehiculoDto>(v.Json))
                .Where(v => v is not null)
                .Select(v => v!)
                .ToList();
        }
    }

    // ================================================================ sedes
    public static void GuardarSedes(IEnumerable<SedeDto> sedes)
    {
        var lista = sedes?.ToList() ?? [];
        lock (Candado)
        {
            var db = Db;
            if (db is null) return;

            db.Execute("DELETE FROM sedes");
            foreach (var s in lista)
            {
                db.InsertOrReplace(new SedeLocal
                {
                    SedeId = s.Id,
                    Json = JsonSerializer.Serialize(s, JsonUrbanPark.Opciones),
                    GuardadoEn = DateTime.Now
                });
            }
        }
    }

    public static List<SedeDto> LeerSedes()
    {
        lock (Candado)
        {
            var db = Db;
            if (db is null) return [];

            return db.Table<SedeLocal>()
                .OrderBy(s => s.SedeId)
                .ToList()
                .Select(s => Deserializar<SedeDto>(s.Json))
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
        }
    }

    public static SedeDto? LeerSede(long sedeId)
    {
        lock (Candado)
        {
            if (Db?.Find<SedeLocal>(sedeId) is not { } fila)
                return null;
            return Deserializar<SedeDto>(fila.Json);
        }
    }

    // ================================================================ plazas
    public static void GuardarEspacios(long sedeId, IEnumerable<EspacioDto> espacios)
    {
        var lista = espacios?.ToList() ?? [];
        lock (Candado)
        {
            var db = Db;
            if (db is null) return;

            db.Execute("DELETE FROM espacios WHERE sede_id = ?", sedeId);
            foreach (var e in lista)
            {
                db.InsertOrReplace(new EspacioLocal
                {
                    EspacioId = e.Id,
                    SedeId = sedeId,
                    Json = JsonSerializer.Serialize(e, JsonUrbanPark.Opciones),
                    GuardadoEn = DateTime.Now
                });
            }
        }
    }

    public static List<EspacioDto> LeerEspacios(long sedeId)
    {
        lock (Candado)
        {
            var db = Db;
            if (db is null) return [];

            return db.Table<EspacioLocal>()
                .Where(e => e.SedeId == sedeId)
                .ToList()
                .Select(e => Deserializar<EspacioDto>(e.Json))
                .Where(e => e is not null)
                .Select(e => e!)
                .ToList();
        }
    }

    // ================================================================ tickets abiertos
    public static void GuardarTickets(IEnumerable<TicketDto> tickets, string usuario, long usuarioId = 0)
    {
        var lista = tickets?.ToList() ?? [];
        lock (Candado)
        {
            var db = Db;
            if (db is null) return;

            db.Execute("DELETE FROM tickets WHERE usuario = ?", usuario ?? "");
            foreach (var t in lista)
            {
                db.InsertOrReplace(new TicketLocal
                {
                    TicketId = t.Id,
                    Usuario = usuario ?? "",
                    UsuarioId = usuarioId,
                    Abierto = t.Abierto,
                    Json = JsonSerializer.Serialize(t, JsonUrbanPark.Opciones),
                    GuardadoEn = DateTime.Now
                });
            }
        }
    }

    public static List<TicketDto> LeerTickets(string usuario, long usuarioId = 0)
    {
        lock (Candado)
        {
            var db = Db;
            if (db is null) return [];

            var filas = usuarioId > 0
                ? db.Table<TicketLocal>().Where(t => t.UsuarioId == usuarioId).ToList()
                    .Concat(db.Table<TicketLocal>().Where(t => t.UsuarioId == 0 && t.Usuario == usuario).ToList())
                : db.Table<TicketLocal>().Where(t => t.Usuario == usuario).ToList();

            return filas
                .DistinctBy(t => t.TicketId)
                .OrderByDescending(t => t.TicketId)
                .Select(t => Deserializar<TicketDto>(t.Json))
                .Where(t => t is not null)
                .Select(t => t!)
                .ToList();
        }
    }

    // ================================================================ cola de pendientes
    public static int ContarPendientes()
    {
        lock (Candado)
        {
            return Db?.Table<PendienteLocal>().Count() ?? 0;
        }
    }

    public static void Encolar(string metodo, string ruta, string? cuerpo, string usuario, long usuarioId = 0)
    {
        lock (Candado)
        {
            Db?.Insert(new PendienteLocal
            {
                Metodo = metodo,
                Ruta = ruta,
                Cuerpo = cuerpo,
                Usuario = usuario ?? "",
                UsuarioId = usuarioId,
                CreadoEn = DateTime.Now
            });
        }
    }

    /// <summary>
    /// Cambios pendientes del usuario. Se identifica por id y, en colas creadas por versiones
    /// anteriores, por username: si un operador y un cliente comparten equipo, no se mezclan.
    /// </summary>
    public static List<PendienteLocal> PendientesDe(string usuario, long usuarioId = 0)
    {
        lock (Candado)
        {
            var db = Db;
            if (db is null) return [];

            var filas = usuarioId > 0
                ? db.Table<PendienteLocal>().Where(p => p.UsuarioId == usuarioId).ToList()
                    .Concat(db.Table<PendienteLocal>().Where(p => p.UsuarioId == 0 && p.Usuario == usuario).ToList())
                : db.Table<PendienteLocal>().Where(p => p.Usuario == usuario).ToList();

            return filas
                .DistinctBy(p => p.Id)
                .OrderBy(p => p.CreadoEn)
                .ToList();
        }
    }

    public static void QuitarPendiente(string id)
    {
        lock (Candado)
        {
            Db?.Delete<PendienteLocal>(id);
        }
    }

    public static void MarcarFallo(string id, string? error)
    {
        lock (Candado)
        {
            var db = Db;
            if (db is null) return;

            var fila = db.Find<PendienteLocal>(id);
            if (fila is null) return;
            fila.Intentos++;
            fila.UltimoError = error;
            db.Update(fila);
        }
    }

    /// <summary>Borra todo lo del usuario: se usa al cerrar sesión.</summary>
    public static void LimpiarUsuario(string usuario, long usuarioId = 0)
    {
        lock (Candado)
        {
            var db = Db;
            if (db is null) return;

            foreach (var tabla in new[] { "vehiculos", "tickets", "pases", "pendientes" })
            {
                db.Execute($"DELETE FROM {tabla} WHERE usuario = ?", usuario ?? "");
                if (usuarioId > 0)
                    db.Execute($"DELETE FROM {tabla} WHERE usuario_id = ?", usuarioId);
            }
        }
    }

    /// <summary>Diagnóstico de la pantalla de ajustes: cuántas filas hay de cada cosa.</summary>
    public static (int Vehiculos, int Sedes, int Espacios, int Pases, int Tickets, int Pendientes) Resumen()
    {
        lock (Candado)
        {
            var db = Db;
            if (db is null) return (0, 0, 0, 0, 0, 0);

            return (
                db.Table<VehiculoLocal>().Count(),
                db.Table<SedeLocal>().Count(),
                db.Table<EspacioLocal>().Count(),
                db.Table<PaseLocal>().Count(),
                db.Table<TicketLocal>().Count(),
                db.Table<PendienteLocal>().Count());
        }
    }

    private static T? Deserializar<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonUrbanPark.Opciones);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
