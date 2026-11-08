using System.Globalization;
using MySqlConnector;
using UrbanPark.Api.Modelos;
using UrbanPark.Api.Servicios;

namespace UrbanPark.Api.Data;

/// <summary>
/// Crea la base de datos MySQL, el esquema y los datos semilla la primera vez que arranca la API.
/// Es idempotente: se puede ejecutar en cada arranque sin duplicar informacion.
/// Los mismos scripts estan en /database/*.sql para quien prefiera crearlos a mano.
/// </summary>
public static class InicializadorDb
{
    private static readonly string[] Ddl =
    [
        @"CREATE TABLE IF NOT EXISTS usuarios (
            id            BIGINT AUTO_INCREMENT PRIMARY KEY,
            username      VARCHAR(50)  NOT NULL,
            email         VARCHAR(120) NOT NULL,
            first_name    VARCHAR(80)  NOT NULL DEFAULT '',
            last_name     VARCHAR(80)  NOT NULL DEFAULT '',
            password_hash VARCHAR(255) NOT NULL,
            roles         VARCHAR(100) NOT NULL DEFAULT 'CLIENTE',
            enabled       TINYINT(1)   NOT NULL DEFAULT 1,
            google_sub    VARCHAR(64)  NULL,
            totp_secret   VARCHAR(64)  NULL,
            totp_activo   TINYINT(1)   NOT NULL DEFAULT 0,
            creado_en     DATETIME(6)  NOT NULL,
            UNIQUE KEY uq_usuarios_username (username),
            UNIQUE KEY uq_usuarios_google (google_sub)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",

        // Los parqueaderos del grupo. El cliente elige la sede ANTES de escoger el puesto.
        @"CREATE TABLE IF NOT EXISTS sedes (
            id             BIGINT AUTO_INCREMENT PRIMARY KEY,
            codigo         VARCHAR(20)  NOT NULL,
            nombre         VARCHAR(120) NOT NULL,
            direccion      VARCHAR(200) NOT NULL,
            sector         VARCHAR(80)  NOT NULL,
            latitud        DOUBLE       NOT NULL,
            longitud       DOUBLE       NOT NULL,
            telefono       VARCHAR(40)  NOT NULL DEFAULT '',
            horario        VARCHAR(80)  NOT NULL DEFAULT '24 horas',
            techado        TINYINT(1)   NOT NULL DEFAULT 0,
            vigilancia     TINYINT(1)   NOT NULL DEFAULT 1,
            acepta_reserva TINYINT(1)   NOT NULL DEFAULT 1,
            tarifa_base    DOUBLE       NOT NULL,
            servicios      VARCHAR(200) NOT NULL DEFAULT '',
            activa         TINYINT(1)   NOT NULL DEFAULT 1,
            UNIQUE KEY uq_sedes_codigo (codigo),
            KEY ix_sedes_sector (sector)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",

        @"CREATE TABLE IF NOT EXISTS espacios (
            id            BIGINT AUTO_INCREMENT PRIMARY KEY,
            codigo        VARCHAR(20)  NOT NULL,
            zona          VARCHAR(80)  NOT NULL,
            tipo_vehiculo VARCHAR(20)  NOT NULL,
            ocupado       TINYINT(1)   NOT NULL DEFAULT 0,
            tarifa_hora   DOUBLE       NOT NULL,
            nivel         INT          NULL,
            descripcion   VARCHAR(255) NULL,
            sede_id       BIGINT       NULL,
            UNIQUE KEY uq_espacios_codigo (codigo),
            KEY ix_espacios_ocupado (ocupado),
            KEY ix_espacios_zona (zona),
            KEY ix_espacios_sede (sede_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",

        @"CREATE TABLE IF NOT EXISTS vehiculos (
            id                   BIGINT AUTO_INCREMENT PRIMARY KEY,
            placa                VARCHAR(20)  NOT NULL,
            tipo                 VARCHAR(20)  NOT NULL DEFAULT 'AUTO',
            marca                VARCHAR(60)  NULL,
            color                VARCHAR(40)  NULL,
            propietario          VARCHAR(120) NOT NULL,
            propietario_email    VARCHAR(120) NULL,
            propietario_username VARCHAR(50)  NULL,
            UNIQUE KEY uq_vehiculos_placa (placa),
            KEY ix_vehiculos_propietario (propietario_username)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",

        @"CREATE TABLE IF NOT EXISTS tickets (
            id                   BIGINT AUTO_INCREMENT PRIMARY KEY,
            codigo               VARCHAR(20)  NOT NULL,
            placa                VARCHAR(20)  NOT NULL,
            espacio_id           BIGINT       NOT NULL,
            espacio_codigo       VARCHAR(20)  NULL,
            zona                 VARCHAR(80)  NULL,
            entrada              DATETIME(6)  NOT NULL,
            salida               DATETIME(6)  NULL,
            estado               VARCHAR(20)  NOT NULL DEFAULT 'ABIERTO',
            tarifa_hora          DOUBLE       NULL,
            monto_total          DOUBLE       NULL,
            usuario              VARCHAR(50)  NULL,
            liberacion_pendiente TINYINT(1)   NOT NULL DEFAULT 0,
            modo                 VARCHAR(10)  NOT NULL DEFAULT 'AHORA',
            salida_prevista      DATETIME(6)  NULL,
            UNIQUE KEY uq_tickets_codigo (codigo),
            KEY ix_tickets_estado (estado),
            KEY ix_tickets_placa (placa),
            KEY ix_tickets_espacio (espacio_id),
            KEY ix_tickets_usuario (usuario),
            KEY ix_tickets_pendiente (liberacion_pendiente)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",

        @"CREATE TABLE IF NOT EXISTS biometrias (
            id               BIGINT AUTO_INCREMENT PRIMARY KEY,
            usuario          VARCHAR(50)  NOT NULL,
            vehiculo_id      BIGINT       NULL,
            placa            VARCHAR(20)  NOT NULL,
            tipo             VARCHAR(20)  NOT NULL,
            dispositivo      VARCHAR(120) NULL,
            token_hash       VARCHAR(64)  NOT NULL,
            foto_rostro      MEDIUMBLOB   NULL,
            creado_en        DATETIME(6)  NOT NULL,
            UNIQUE KEY uq_biometrias_vehiculo (vehiculo_id),
            KEY ix_biometrias_usuario (usuario)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",
        @"CREATE TABLE IF NOT EXISTS dispositivos (
            id         BIGINT AUTO_INCREMENT PRIMARY KEY,
            usuario    VARCHAR(50)  NOT NULL,
            token_hash VARCHAR(64)  NOT NULL,
            creado_en    DATETIME(6)  NOT NULL,
            ultimo_uso   DATETIME(6)  NULL,
            UNIQUE KEY uq_dispositivos_token (token_hash),
            KEY ix_dispositivos_usuario (usuario)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",

        @"CREATE TABLE IF NOT EXISTS reporte_balance (
            id           INT          NOT NULL PRIMARY KEY,
            desde        DATE         NOT NULL,
            hasta        DATE         NOT NULL,
            generado_en  DATETIME(6)  NOT NULL,
            zonas_json   TEXT         NOT NULL,
            plazas_json  TEXT         NOT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci"
    ];

    // ================================================================== sedes
    /// <summary>
    /// Los 12 parqueaderos del grupo en Quito: cada uno con su dirección real y sus
    /// coordenadas, para que el cliente los vea en el mapa y elija el que le conviene.
    /// </summary>
    private static readonly (string Codigo, string Nombre, string Direccion, string Sector,
        double Latitud, double Longitud, double Tarifa, bool Techado, string Horario, string Servicios)[] Sedes =
    [
        ("CEN", "Urban Park Centro Histórico", "Av. 10 de Agosto N21-56 y Av. Roca", "Centro",
            -0.20776, -78.49184, 0.70, true, "24 horas", "Techado · Cámaras · Baños · Wi-Fi"),
        ("PLA", "Urban Park Plaza Grande", "García Moreno y Sucre, frente a la Catedral", "Centro Histórico",
            -0.22010, -78.51230, 0.90, false, "06:00 a 23:00", "Cámaras · Baños · Cuidadores"),
        ("ALA", "Urban Park La Alameda", "Av. Gran Colombia y Av. 10 de Agosto", "La Alameda",
            -0.21450, -78.50600, 0.61, false, "24 horas", "Cámaras · Baños · Lavado"),
        ("SAN", "Urban Park San Blas", "Calle Chimborazo y Av. Pichincha", "San Blas",
            -0.21750, -78.50650, 0.50, true, "06:00 a 22:00", "Techado · Cámaras · Aseo"),
        ("GON", "Urban Park González Suárez", "Av. González Suárez y Av. Patria", "González Suárez",
            -0.20650, -78.49750, 0.79, true, "24 horas", "Techado · Cámaras · Baños · Wi-Fi"),
        ("COL", "Urban Park Colón", "Av. Colón y Av. 6 de Diciembre", "La Colón",
            -0.19900, -78.48900, 0.67, true, "24 horas", "Techado · Cámaras · Baños"),
        ("FOC", "Urban Park La Foch", "Av. Reina Victoria N24-56 y La Pinta", "La Foch",
            -0.20180, -78.48850, 0.58, false, "24 horas", "Cámaras · Baños · Carga eléctrica"),
        ("MAR", "Urban Park Mariscal", "Av. Amazonas N30-12 y Av. Eloy Alfaro", "La Mariscal",
            -0.18700, -78.48800, 0.54, false, "24 horas", "Cámaras · Aseo · Lavado"),
        ("CAR", "Urban Park La Carolina", "Av. Naciones Unidas y Núñez de Vela", "La Carolina",
            -0.17950, -78.48130, 0.76, false, "24 horas", "Cámaras · Baños · Zona verde"),
        ("QUI", "Urban Park Quicentro", "Av. Naciones Unidas y Av. 6 de Diciembre", "Iñaquito",
            -0.17580, -78.48300, 0.90, true, "24 horas", "Techado · Cámaras · Baños · Wi-Fi · Valet"),
        ("NAC", "Urban Park Naciones Unidas", "Av. Naciones Unidas y Av. Japón", "Naciones Unidas",
            -0.17320, -78.48650, 0.73, true, "24 horas", "Techado · Cámaras · Baños · Carga eléctrica"),
        ("EBA", "Urban Park El Batán", "Av. Eloy Alfaro y Av. Naciones Unidas", "El Batán",
            -0.18000, -78.47300, 0.64, false, "06:00 a 23:00", "Cámaras · Baños · Aseo")
    ];

    /// <summary>
    /// Zonas de cada sede. La sede CEN conserva las cuatro zonas historicas
    /// (Centro, Norte, Sur y Este) con sus mismas tarifas; las demás sedes replican
    /// el mismo esquema A/B/C/D con tarifas derivadas de su tarifa base.
    /// </summary>
    private static readonly Dictionary<string, ZonaTarifa[]> ZonasPorSede = CrearZonas();

    private sealed record ZonaTarifa(string Prefijo, string Zona, int Nivel, double Auto, double Moto, double Camioneta, double Disc);

    private static Dictionary<string, ZonaTarifa[]> CrearZonas()
    {
        var mapa = new Dictionary<string, ZonaTarifa[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var sede in Sedes)
        {
            var nombres = sede.Codigo == "CEN"
                ? new (string Pref, string Nombre, int Nivel, char Letra)[]
                {
                    ("A", "Zona A - Centro", 1, 'A'),
                    ("B", "Zona B - Norte", 2, 'B'),
                    ("C", "Zona C - Sur", -1, 'C'),
                    ("D", "Zona D - Este", 1, 'D')
                }
                :
                [
                    ("A", $"Zona A - {sede.Sector} PB", 1, 'A'),
                    ("B", $"Zona B - {sede.Sector} P1", 2, 'B'),
                    ("C", $"Zona C - {sede.Sector} S1", -1, 'C'),
                    ("D", $"Zona D - {sede.Sector} Exterior", 1, 'D')
                ];

            mapa[sede.Codigo] = ConstruirZonas(sede.Codigo, sede.Tarifa, nombres);
        }

        return mapa;
    }

    private static ZonaTarifa[] ConstruirZonas(string codigo, double baseTarifa,
        (string Pref, string Nombre, int Nivel, char Letra)[] defs) =>
        defs.Select(d => new ZonaTarifa(
            d.Pref,
            d.Nombre,
            d.Nivel,
            ReglasTarifa.TarifaHora(codigo, d.Letra, "AUTO", baseTarifa),
            ReglasTarifa.TarifaHora(codigo, d.Letra, "MOTO", baseTarifa),
            ReglasTarifa.TarifaHora(codigo, d.Letra, "CAMIONETA", baseTarifa),
            ReglasTarifa.TarifaHora(codigo, d.Letra, "DISCAPACIDAD", baseTarifa)
        )).ToArray();

    private static readonly (string Placa, string Tipo, string Marca, string Color, string Propietario, string Email, string Usuario)[] Vehiculos =
    [
        ("ABC-123", "AUTO", "Toyota", "Gris", "Mujer", "cliente1@urbanpark.local", "cliente1"),
        ("XYZ-789", "MOTO", "Yamaha", "Rojo", "Estudiante", "operador1@urbanpark.local", "operador1"),
        ("DEF-456", "AUTO", "Chevrolet", "Azul", "Tercera Edad", "cliente2@urbanpark.local", "cliente2"),
        ("GHI-789", "CAMIONETA", "Ford", "Blanco", "Embarazada", "cliente3@urbanpark.local", "cliente3")
    ];

    /// <summary>
    /// Usuarios demo. Los hashes PBKDF2-SHA256 (100.000 iteraciones) fueron precalculados para que
    /// coincidan exactamente con PasswordService.Verificar.
    /// </summary>
    private static readonly (string Username, string Email, string Nombre, string Apellido, string Roles, string Hash)[] Usuarios =
    [
        ("cliente1", "cliente1@urbanpark.local", "Ana", "Cliente", "CLIENTE",
            "pbkdf2$100000$o6hSk1YMFbT2BuTMllvzuQ==$ALd83ChE1GMMVKUnpbvZzsThYYC4g6qfZTtUIy6oSYc="),
        ("cliente2", "cliente2@urbanpark.local", "Pedro", "Cliente", "CLIENTE",
            "pbkdf2$100000$v5xmFlu1zXeBH/spHUwiXg==$34+HJ5MGrFD+oljU8VtvFzDFWCb+zNacTGqtfge/RCo="),
        ("cliente3", "cliente3@urbanpark.local", "María", "Cliente", "CLIENTE",
            "pbkdf2$100000$Ig2G8NZrBfcP9O+goVkPDw==$dXYlFhnuqtM60MxFnUoxQdHIaD60EZs6zOgOMICIucQ="),
        ("operador1", "operador1@urbanpark.local", "Luis", "Operador", "OPERADOR",
            "pbkdf2$100000$kR5h5MoFilXtf5rHS89Zag==$TAk/+Yn/8UWeJNhglWDMX7/t9bvArQjz3KuEwVlHOpQ="),
        ("admin1", "admin1@urbanpark.local", "Admin", "General", "ADMIN,OPERADOR",
            "pbkdf2$100000$a3a0iBbFN8gpz7sklLvvZw==$nXLVpZpIa2yh1U7X0UQa184FWJDz729hD89dZZE2lgA=")
    ];

    public static async Task InicializarAsync(IConfiguration configuracion, ILogger logger)
    {
        var cadena = configuracion.GetConnectionString("UrbanPark")
                     ?? throw new InvalidOperationException("Falta ConnectionStrings:UrbanPark");

        // --- 1) crear la base de datos si no existe (conexion sin Database=) ---
        var builder = new MySqlConnectionStringBuilder(cadena);
        var nombreBase = builder.Database;
        builder.Database = "";
        await using (var cnServidor = new MySqlConnection(builder.ConnectionString))
        {
            await cnServidor.OpenAsync();
            await using var cmd = cnServidor.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE IF NOT EXISTS `{nombreBase}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
            await cmd.ExecuteNonQueryAsync();
            logger.LogInformation("Base de datos '{Base}' verificada.", nombreBase);
        }

        // --- 2) esquema ---
        await using (var cn = new MySqlConnection(cadena))
        {
            await cn.OpenAsync();
            foreach (var sentencia in Ddl)
            {
                await using var cmd = cn.CreateCommand();
                cmd.CommandText = sentencia;
                await cmd.ExecuteNonQueryAsync();
            }
            logger.LogInformation("Esquema verificado (sedes, usuarios, espacios, vehiculos, tickets).");
        }

        // --- 3) migracion de bases anteriores (sin sede) ---
        await using (var cn = new MySqlConnection(cadena))
        {
            await cn.OpenAsync();
            await MigrarSedeIdAsync(cn);
            await SembrarSedesAsync(cn, logger);
            var plazas = await SembrarEspaciosAsync(cn, logger);
            await AcotarTarifasAlRangoAsync(cn, logger);
            await SincronizarTarifasDesdeReglasAsync(cn, logger);
            await NormalizarTicketsHistoricosAsync(cn, logger);
            logger.LogInformation("Plazas semilla verificadas: {Plazas} en {Sedes} sedes.", plazas, Sedes.Length);
        }

        // --- 4) semillas restantes ---
        await using (var cn = new MySqlConnection(cadena))
        {
            await cn.OpenAsync();

            foreach (var v in Vehiculos)
            {
                await using var cmd = cn.CreateCommand();
                cmd.CommandText = @"INSERT IGNORE INTO vehiculos (placa, tipo, marca, color, propietario, propietario_email, propietario_username)
                                    VALUES (@p, @t, @m, @c, @pr, @pe, @pu)";
                cmd.Parameters.AddWithValue("@p", v.Placa);
                cmd.Parameters.AddWithValue("@t", v.Tipo);
                cmd.Parameters.AddWithValue("@m", v.Marca);
                cmd.Parameters.AddWithValue("@c", v.Color);
                cmd.Parameters.AddWithValue("@pr", v.Propietario);
                cmd.Parameters.AddWithValue("@pe", v.Email);
                cmd.Parameters.AddWithValue("@pu", v.Usuario);
                await cmd.ExecuteNonQueryAsync();
            }

            foreach (var u in Usuarios)
            {
                await using var cmd = cn.CreateCommand();
                cmd.CommandText = @"INSERT IGNORE INTO usuarios (username, email, first_name, last_name, password_hash, roles, enabled, creado_en)
                                    VALUES (@u, @e, @fn, @ln, @ph, @ro, 1, @cr)";
                cmd.Parameters.AddWithValue("@u", u.Username);
                cmd.Parameters.AddWithValue("@e", u.Email);
                cmd.Parameters.AddWithValue("@fn", u.Nombre);
                cmd.Parameters.AddWithValue("@ln", u.Apellido);
                cmd.Parameters.AddWithValue("@ph", u.Hash);
                cmd.Parameters.AddWithValue("@ro", u.Roles);
                cmd.Parameters.AddWithValue("@cr", DateTime.Now);
                await cmd.ExecuteNonQueryAsync();
            }

            await AjustarPerfilesAsync(cn);
        }

        logger.LogInformation("Datos semilla verificados: {Sedes} sedes, {Vehiculos} vehiculos, {Usuarios} usuarios demo.",
            Sedes.Length, Vehiculos.Length, Usuarios.Length);
    }

    // ------------------------------------------------------------------ sedes
    private static async Task SembrarSedesAsync(MySqlConnection cn, ILogger logger)
    {
        foreach (var s in Sedes)
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = @"INSERT INTO sedes (codigo, nombre, direccion, sector, latitud, longitud, telefono, horario,
                                                   techado, vigilancia, acepta_reserva, tarifa_base, servicios, activa)
                                VALUES (@c, @n, @d, @s, @la, @lo, @t, @h, @te, @v, 1, @tb, @se, 1)
                                ON DUPLICATE KEY UPDATE nombre = VALUES(nombre), direccion = VALUES(direccion),
                                   sector = VALUES(sector), latitud = VALUES(latitud), longitud = VALUES(longitud),
                                   horario = VALUES(horario), techado = VALUES(techado), vigilancia = VALUES(vigilancia),
                                   acepta_reserva = 1, tarifa_base = VALUES(tarifa_base), servicios = VALUES(servicios),
                                   activa = 1";
            cmd.Parameters.AddWithValue("@c", s.Codigo);
            cmd.Parameters.AddWithValue("@n", s.Nombre);
            cmd.Parameters.AddWithValue("@d", s.Direccion);
            cmd.Parameters.AddWithValue("@s", s.Sector);
            cmd.Parameters.AddWithValue("@la", s.Latitud);
            cmd.Parameters.AddWithValue("@lo", s.Longitud);
            cmd.Parameters.AddWithValue("@t", TelefonoDe(s.Codigo));
            cmd.Parameters.AddWithValue("@h", s.Horario);
            cmd.Parameters.AddWithValue("@te", s.Techado ? 1 : 0);
            cmd.Parameters.AddWithValue("@v", 1);
            cmd.Parameters.AddWithValue("@tb", s.Tarifa);
            cmd.Parameters.AddWithValue("@se", s.Servicios);
            await cmd.ExecuteNonQueryAsync();
        }

        logger.LogInformation("Sedes verificadas: {Cantidad} parqueaderos con dirección y coordenadas.", Sedes.Length);
    }

    private static async Task AcotarTarifasAlRangoAsync(MySqlConnection cn, ILogger logger)
    {
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE sedes SET tarifa_base = LEAST({ReglasTarifa.Maxima}, GREATEST({ReglasTarifa.Minima}, tarifa_base));
            UPDATE espacios SET tarifa_hora = LEAST({ReglasTarifa.Maxima}, GREATEST({ReglasTarifa.Minima}, tarifa_hora));
            """;
        await cmd.ExecuteNonQueryAsync();
        logger.LogInformation("Tarifas acotadas al rango ${Min:0.00}–${Max:0.00}/h.",
            ReglasTarifa.Minima, ReglasTarifa.Maxima);
    }

    /// <summary>
    /// Aplica <see cref="ReglasTarifa"/> a sedes y plazas (corrige importes viejos o semillas en cero).
    /// </summary>
    private static async Task SincronizarTarifasDesdeReglasAsync(MySqlConnection cn, ILogger logger)
    {
        var sedes = await LeerIdsSedesAsync(cn);
        var plazasActualizadas = 0;

        foreach (var sede in Sedes)
        {
            if (!sedes.TryGetValue(sede.Codigo, out var sedeId)) continue;
            var baseTarifa = ReglasTarifa.Acotar(sede.Tarifa);

            await using (var updSede = cn.CreateCommand())
            {
                updSede.CommandText = "UPDATE sedes SET tarifa_base = @t WHERE id = @id";
                updSede.Parameters.AddWithValue("@t", baseTarifa);
                updSede.Parameters.AddWithValue("@id", sedeId);
                await updSede.ExecuteNonQueryAsync();
            }

            await using var leer = cn.CreateCommand();
            leer.CommandText = "SELECT id, codigo, tipo_vehiculo FROM espacios WHERE sede_id = @s";
            leer.Parameters.AddWithValue("@s", sedeId);

            List<(long Id, string Codigo, string Tipo)> plazas;
            await using (var rd = await leer.ExecuteReaderAsync())
            {
                plazas = [];
                while (await rd.ReadAsync())
                    plazas.Add((rd.GetInt64(0), rd.GetString(1), rd.GetString(2)));
            }

            foreach (var p in plazas)
            {
                var letra = ReglasTarifa.LetraZona(p.Codigo);
                var nueva = ReglasTarifa.TarifaHora(sede.Codigo, letra, p.Tipo, baseTarifa);

                await using var upd = cn.CreateCommand();
                upd.CommandText = "UPDATE espacios SET tarifa_hora = @t WHERE id = @id";
                upd.Parameters.AddWithValue("@t", nueva);
                upd.Parameters.AddWithValue("@id", p.Id);
                plazasActualizadas += await upd.ExecuteNonQueryAsync();
            }
        }

        logger.LogInformation(
            "Tarifas de {Plazas} plazas y {Sedes} sedes alineadas con ReglasTarifa (${Min:0.00}–${Max:0.00}/h).",
            plazasActualizadas, Sedes.Length, ReglasTarifa.Minima, ReglasTarifa.Maxima);
    }

    /// <summary>
    /// Alinea tickets históricos al rango $0.50–$0.90: tarifa de la plaza y montos con la misma fórmula de cierre.
    /// </summary>
    private static async Task NormalizarTicketsHistoricosAsync(MySqlConnection cn, ILogger logger)
    {
        var min = ReglasTarifa.Minima;
        var max = ReglasTarifa.Maxima;
        var minS = min.ToString(CultureInfo.InvariantCulture);
        var maxS = max.ToString(CultureInfo.InvariantCulture);

        await using (var porId = cn.CreateCommand())
        {
            porId.CommandText = """
                UPDATE tickets t
                  INNER JOIN espacios e ON e.id = t.espacio_id
                   SET t.tarifa_hora = e.tarifa_hora
                 WHERE t.tarifa_hora IS NULL
                    OR ABS(t.tarifa_hora - e.tarifa_hora) > 0.001;
                """;
            await porId.ExecuteNonQueryAsync();
        }

        await using (var acotar = cn.CreateCommand())
        {
            acotar.CommandText = $"""
                UPDATE tickets
                   SET tarifa_hora = LEAST({maxS}, GREATEST({minS}, tarifa_hora))
                 WHERE tarifa_hora IS NOT NULL
                   AND (tarifa_hora < {minS} OR tarifa_hora > {maxS});
                """;
            await acotar.ExecuteNonQueryAsync();
        }

        var descuentos = await CargarDescuentosPorPlacaAsync(cn);
        var recalculados = 0;

        await using var leer = cn.CreateCommand();
        leer.CommandText = """
            SELECT id, placa, entrada, salida, tarifa_hora
              FROM tickets
             WHERE estado = 'CERRADO'
               AND salida IS NOT NULL
               AND tarifa_hora IS NOT NULL
               AND tarifa_hora > 0;
            """;

        List<(long Id, string Placa, DateTime Entrada, DateTime Salida, double Tarifa)> pendientes;
        await using (var rd = await leer.ExecuteReaderAsync())
        {
            pendientes = [];
            while (await rd.ReadAsync())
            {
                pendientes.Add((
                    rd.GetInt64(0),
                    rd.GetString(1),
                    rd.GetDateTime(2),
                    rd.GetDateTime(3),
                    rd.GetDouble(4)));
            }
        }

        foreach (var t in pendientes)
        {
            var placa = Reglas.NormalizarPlaca(t.Placa);
            var pct = descuentos.GetValueOrDefault(placa, 0);
            var bruto = Reglas.CalcularMonto(t.Entrada, t.Salida, t.Tarifa);
            var monto = Reglas.CalcularMonto(t.Entrada, t.Salida, t.Tarifa, pct);
            var descuento = Math.Round(Math.Max(0, bruto - monto), 2, MidpointRounding.AwayFromZero);

            await using var upd = cn.CreateCommand();
            upd.CommandText = "UPDATE tickets SET monto_total = @m, monto_descuento = @d WHERE id = @id";
            upd.Parameters.AddWithValue("@m", monto);
            upd.Parameters.AddWithValue("@d", descuento);
            upd.Parameters.AddWithValue("@id", t.Id);
            recalculados += await upd.ExecuteNonQueryAsync();
        }

        logger.LogInformation(
            "Tickets históricos alineados al rango ${Min:0.00}–${Max:0.00}/h; {Cerrados} cierres recalculados.",
            min, max, recalculados);
    }

    private static async Task<Dictionary<string, int>> CargarDescuentosPorPlacaAsync(MySqlConnection cn)
    {
        var mapa = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT placa, descuento, propietario FROM vehiculos";
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var placa = Reglas.NormalizarPlaca(rd.GetString(0));
            var d = rd.IsDBNull(1) ? 0 : Convert.ToInt32(rd.GetValue(1), CultureInfo.InvariantCulture);
            var prop = rd.IsDBNull(2) ? "" : rd.GetString(2);
            var pct = Math.Max(d, Reglas.DescuentoDe(prop));
            if (!mapa.TryGetValue(placa, out var prev) || pct > prev)
                mapa[placa] = pct;
        }

        return mapa;
    }

    private static string TelefonoDe(string codigo) =>
        (codigo.GetHashCode() & 0x7FFFFFFF) % 2 == 0 ? "02 258 7410" : "02 394 5520";

    /// <summary>
    /// Inserta/actualiza las 16 plazas de cada zona de cada sede en bloque
    /// (una sola sentencia por cada 100 plazas para no encarecer el arranque).
    /// </summary>
    private static async Task<int> SembrarEspaciosAsync(MySqlConnection cn, ILogger logger)
    {
        // 1) bases anteriores: las plazas A-01..D-16 pasan a la sede CEN con su codigo nuevo.
        await using (var renombrar = cn.CreateCommand())
        {
            renombrar.CommandText = @"UPDATE espacios SET codigo = CONCAT('CEN-', codigo)
                                      WHERE sede_id IS NULL AND codigo REGEXP '^[A-D]-[0-9]{2}$'";
            var movidas = await renombrar.ExecuteNonQueryAsync();
            if (movidas > 0)
                logger.LogInformation("{Movidas} plazas del esquema anterior renombradas a la sede CEN.", movidas);
        }

        var sedes = await LeerIdsSedesAsync(cn);
        var filas = new List<(string Codigo, string Zona, long SedeId, string Tipo, double Tarifa, int Nivel, string Descripcion)>();

        foreach (var sede in Sedes)
        {
            if (!sedes.TryGetValue(sede.Codigo, out var sedeId)) continue;
            if (!ZonasPorSede.TryGetValue(sede.Codigo, out var zonas)) continue;

            foreach (var z in zonas)
            {
                for (var i = 1; i <= 15; i++)
                {
                    var (tipo, tarifa, descripcion) = i switch
                    {
                        <= 2 => ("DISCAPACIDAD", z.Disc, "Plaza preferencial"),
                        <= 4 => ("MOTO", z.Moto, "Plaza de motos"),
                        <= 6 => ("CAMIONETA", z.Camioneta, "Plaza amplia"),
                        _ => ("AUTO", z.Auto, "Plaza estándar")
                    };
                    filas.Add(($"{sede.Codigo}-{z.Prefijo}-{i:00}", z.Zona, sedeId, tipo, tarifa, z.Nivel, descripcion));
                }

                var tarifaEmb = ReglasTarifa.TarifaHora(sede.Codigo, ReglasTarifa.LetraZona($"{sede.Codigo}-{z.Prefijo}-16"),
                    "EMBARAZADA", sede.Tarifa);
                filas.Add(($"{sede.Codigo}-{z.Prefijo}-16", z.Zona, sedeId, "EMBARAZADA", tarifaEmb, z.Nivel,
                    "Plaza para embarazadas"));
            }
        }

        const int bloque = 100;
        for (var inicio = 0; inicio < filas.Count; inicio += bloque)
        {
            var trozo = filas.Skip(inicio).Take(bloque).ToList();
            await using var cmd = cn.CreateCommand();

            var valores = new List<string>(trozo.Count);
            for (var i = 0; i < trozo.Count; i++)
            {
                valores.Add($"(@c{i}, @z{i}, @t{i}, 0, @th{i}, @n{i}, @d{i}, @s{i})");
                var f = trozo[i];
                cmd.Parameters.AddWithValue($"@c{i}", f.Codigo);
                cmd.Parameters.AddWithValue($"@z{i}", f.Zona);
                cmd.Parameters.AddWithValue($"@t{i}", f.Tipo);
                cmd.Parameters.AddWithValue($"@th{i}", f.Tarifa);
                cmd.Parameters.AddWithValue($"@n{i}", f.Nivel);
                cmd.Parameters.AddWithValue($"@d{i}", f.Descripcion);
                cmd.Parameters.AddWithValue($"@s{i}", f.SedeId);
            }

            cmd.CommandText =
                "INSERT INTO espacios (codigo, zona, tipo_vehiculo, ocupado, tarifa_hora, nivel, descripcion, sede_id) VALUES " +
                string.Join(", ", valores) +
                " ON DUPLICATE KEY UPDATE sede_id = VALUES(sede_id), zona = VALUES(zona), tipo_vehiculo = VALUES(tipo_vehiculo)," +
                " tarifa_hora = VALUES(tarifa_hora), nivel = VALUES(nivel), descripcion = VALUES(descripcion)";
            await cmd.ExecuteNonQueryAsync();
        }

        // 2) cualquier plaza huerfana se asigna a la sede principal, para que el mapa nunca la pierda.
        if (sedes.TryGetValue("CEN", out var sedePrincipal))
        {
            await using var asignar = cn.CreateCommand();
            asignar.CommandText = "UPDATE espacios SET sede_id = @s WHERE sede_id IS NULL OR sede_id = 0";
            asignar.Parameters.AddWithValue("@s", sedePrincipal);
            await asignar.ExecuteNonQueryAsync();
        }

        return filas.Count;
    }

    private static async Task<Dictionary<string, long>> LeerIdsSedesAsync(MySqlConnection cn)
    {
        var mapa = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT codigo, id FROM sedes";
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            mapa[rd.GetString(0)] = rd.GetInt64(1);
        return mapa;
    }

    /// <summary>Agrega la columna sede_id a una base creada con la version anterior.</summary>
    private static async Task MigrarSedeIdAsync(MySqlConnection cn)
    {
        var existe = await ExisteColumnaAsync(cn, "espacios", "sede_id");
        if (!existe)
        {
            await using var alterar = cn.CreateCommand();
            alterar.CommandText = "ALTER TABLE espacios ADD COLUMN sede_id BIGINT NULL";
            await alterar.ExecuteNonQueryAsync();
        }

        var indice = await ExisteIndiceAsync(cn, "espacios", "ix_espacios_sede");
        if (!indice)
        {
            await using var crear = cn.CreateCommand();
            crear.CommandText = "ALTER TABLE espacios ADD INDEX ix_espacios_sede (sede_id)";
            await crear.ExecuteNonQueryAsync();
        }
    }

    private static async Task<bool> ExisteColumnaAsync(MySqlConnection cn, string tabla, string columna)
    {
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM information_schema.COLUMNS
                            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND COLUMN_NAME = @c";
        cmd.Parameters.AddWithValue("@t", tabla);
        cmd.Parameters.AddWithValue("@c", columna);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static async Task<bool> ExisteIndiceAsync(MySqlConnection cn, string tabla, string indice)
    {
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM information_schema.STATISTICS
                            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND INDEX_NAME = @i";
        cmd.Parameters.AddWithValue("@t", tabla);
        cmd.Parameters.AddWithValue("@i", indice);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    /// <summary>
    /// Agrega el descuento y reescribe los perfiles viejos (Anciano, Discapacitado y los nombres demo)
    /// para que los vehículos ya cargados usen la distribución nueva.
    /// </summary>
    private static async Task AjustarPerfilesAsync(MySqlConnection cn)
    {
        if (!await ExisteColumnaAsync(cn, "vehiculos", "descuento"))
        {
            await using var alterar = cn.CreateCommand();
            alterar.CommandText = "ALTER TABLE vehiculos ADD COLUMN descuento INT NOT NULL DEFAULT 0";
            await alterar.ExecuteNonQueryAsync();
        }

        if (!await ExisteColumnaAsync(cn, "vehiculos", "activo"))
        {
            await using var alterarActivo = cn.CreateCommand();
            alterarActivo.CommandText = "ALTER TABLE vehiculos ADD COLUMN activo TINYINT(1) NOT NULL DEFAULT 1";
            await alterarActivo.ExecuteNonQueryAsync();
        }

        string[] ajustes =
        [
            "UPDATE vehiculos SET propietario = 'Tercera Edad' WHERE LOWER(propietario) IN ('anciano', 'adulto mayor') OR LOWER(propietario) LIKE '%anciano%'",
            "UPDATE vehiculos SET propietario = 'Capacidades Especiales' WHERE LOWER(propietario) IN ('discapacitado', 'discapacidad') OR LOWER(propietario) LIKE '%discapacit%'",
            "UPDATE vehiculos SET propietario = 'Embarazada' WHERE LOWER(propietario) LIKE '%embaraz%'",
            "UPDATE vehiculos SET propietario = 'Estudiante' WHERE LOWER(propietario) LIKE '%estudiante%' OR LOWER(propietario) LIKE '%estudiate%'",
            "UPDATE vehiculos SET propietario = 'Mujer', descuento = 0 WHERE placa = 'ABC-123' AND propietario = 'Ana Cliente'",
            "UPDATE vehiculos SET propietario = 'Estudiante', descuento = 25 WHERE placa = 'XYZ-789' AND propietario = 'Luis Operador'",
            "UPDATE vehiculos SET propietario = 'Tercera Edad', descuento = 50 WHERE placa = 'DEF-456' AND propietario = 'Pedro Cliente'",
            "UPDATE vehiculos SET propietario = 'Embarazada', descuento = 25 WHERE placa = 'GHI-789' AND (propietario = 'María Cliente' OR propietario = 'Maria Cliente')",
            @"UPDATE vehiculos SET descuento = CASE
                WHEN LOWER(propietario) LIKE '%capacidades%' OR LOWER(propietario) LIKE '%discapacit%' OR LOWER(propietario) LIKE '%especial%' THEN 50
                WHEN LOWER(propietario) LIKE '%tercera%' OR LOWER(propietario) LIKE '%anciano%' OR LOWER(propietario) LIKE '%mayor%' THEN 50
                WHEN LOWER(propietario) LIKE '%embaraz%' THEN 25
                WHEN LOWER(propietario) LIKE '%estudiante%' OR LOWER(propietario) LIKE '%estudiate%' THEN 25
                ELSE 0 END"
        ];

        foreach (var sql in ajustes)
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        if (!await ExisteColumnaAsync(cn, "tickets", "monto_descuento"))
        {
            await using var alterar = cn.CreateCommand();
            alterar.CommandText = "ALTER TABLE tickets ADD COLUMN monto_descuento DOUBLE NOT NULL DEFAULT 0";
            await alterar.ExecuteNonQueryAsync();
        }

        // Reservas: modo AHORA/RESERVA y la hora de salida prevista.
        // Autenticador de Google (TOTP).
        if (!await ExisteColumnaAsync(cn, "usuarios", "totp_secret"))
        {
            await using var alterarTotp = cn.CreateCommand();
            alterarTotp.CommandText = "ALTER TABLE usuarios ADD COLUMN totp_secret VARCHAR(64) NULL";
            await alterarTotp.ExecuteNonQueryAsync();
        }

        if (!await ExisteColumnaAsync(cn, "usuarios", "totp_activo"))
        {
            await using var alterarTotpActivo = cn.CreateCommand();
            alterarTotpActivo.CommandText = "ALTER TABLE usuarios ADD COLUMN totp_activo TINYINT(1) NOT NULL DEFAULT 0";
            await alterarTotpActivo.ExecuteNonQueryAsync();
        }
        // Sede de cada ticket (para reportes y exportaciones por sucursal).
        if (!await ExisteColumnaAsync(cn, "tickets", "sede_id"))
        {
            await using var alterarSede = cn.CreateCommand();
            alterarSede.CommandText = "ALTER TABLE tickets ADD COLUMN sede_id BIGINT NULL";
            await alterarSede.ExecuteNonQueryAsync();
        }

        // Se rellena con la sede de la plaza que quedó guardada en el ticket.
        await using (var rellenarSede = cn.CreateCommand())
        {
            rellenarSede.CommandText = @"
                UPDATE tickets t
                  JOIN espacios e ON e.codigo = t.espacio_codigo
                   SET t.sede_id = e.sede_id
                 WHERE t.sede_id IS NULL AND e.sede_id IS NOT NULL";
            await rellenarSede.ExecuteNonQueryAsync();
        }

        // Los tickets antiguos pueden tener el código de plaza ya cambiado: se busca por id.
        await using (var rellenarSedePorId = cn.CreateCommand())
        {
            rellenarSedePorId.CommandText = @"
                UPDATE tickets t
                  JOIN espacios e ON e.id = t.espacio_id
                   SET t.sede_id = e.sede_id
                 WHERE t.sede_id IS NULL AND e.sede_id IS NOT NULL";
            await rellenarSedePorId.ExecuteNonQueryAsync();
        }


        if (!await ExisteColumnaAsync(cn, "tickets", "modo"))
        {
            await using var alterarModo = cn.CreateCommand();
            alterarModo.CommandText = "ALTER TABLE tickets ADD COLUMN modo VARCHAR(10) NOT NULL DEFAULT 'AHORA'";
            await alterarModo.ExecuteNonQueryAsync();
        }

        if (!await ExisteColumnaAsync(cn, "tickets", "salida_prevista"))
        {
            await using var alterarSalida = cn.CreateCommand();
            alterarSalida.CommandText = "ALTER TABLE tickets ADD COLUMN salida_prevista DATETIME(6) NULL";
            await alterarSalida.ExecuteNonQueryAsync();
        }

        await using (var relleno = cn.CreateCommand())
        {
            relleno.CommandText = @"UPDATE tickets
                                    SET monto_descuento = GREATEST(0, ROUND(
                                        CEILING(GREATEST(1, TIMESTAMPDIFF(MINUTE, entrada, salida)) / 60.0) * tarifa_hora
                                        - monto_total, 2))
                                    WHERE estado = 'CERRADO'
                                      AND salida IS NOT NULL
                                      AND tarifa_hora IS NOT NULL
                                      AND monto_total IS NOT NULL
                                      AND monto_descuento = 0
                                      AND monto_total + 0.009 < CEILING(GREATEST(1, TIMESTAMPDIFF(MINUTE, entrada, salida)) / 60.0) * tarifa_hora";
            await relleno.ExecuteNonQueryAsync();
        }
    }
}
