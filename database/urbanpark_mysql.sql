-- ============================================================================
--  URBAN PARK - Base de datos MySQL
--  Versión móvil (.NET MAUI + ASP.NET Core + MySQL)
--
--  Este script crea el esquema completo y los datos semilla.
--  La API también lo hace automáticamente al arrancar (InicializadorDb),
--  así que solo necesitas ejecutarlo si prefieres preparar la base a mano.
--
--  Uso:  mysql -u root -p < urbanpark_mysql.sql
-- ============================================================================

CREATE DATABASE IF NOT EXISTS urbanpark
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

USE urbanpark;

-- ----------------------------------------------------------------------------
-- 1) USUARIOS  (reemplaza a Keycloak: la autenticación es propia)
--    Los roles se guardan separados por coma, p. ej. 'ADMIN,OPERADOR'.
--    password_hash usa PBKDF2-SHA256 (100.000 iteraciones):
--        pbkdf2$<iteraciones>$<saltBase64>$<hashBase64>
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS usuarios (
    id            BIGINT AUTO_INCREMENT PRIMARY KEY,
    username      VARCHAR(50)  NOT NULL,
    email         VARCHAR(120) NOT NULL,
    first_name    VARCHAR(80)  NOT NULL DEFAULT '',
    last_name     VARCHAR(80)  NOT NULL DEFAULT '',
    password_hash VARCHAR(255) NOT NULL,
    roles         VARCHAR(100) NOT NULL DEFAULT 'CLIENTE',
    enabled       TINYINT(1)   NOT NULL DEFAULT 1,
    google_sub    VARCHAR(64)  NULL COMMENT 'Identificador de Google Sign-In',
    creado_en     DATETIME(6)  NOT NULL,
    UNIQUE KEY uq_usuarios_username (username),
    UNIQUE KEY uq_usuarios_google (google_sub)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------------------------
-- 2) SEDES  (los parqueaderos del grupo: el cliente elige la sede antes del puesto)
--    Cada sede tiene su dirección y sus coordenadas para que el mapa y la IA
--    (distancia en km / minutos a pie) puedan recomendarla.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS sedes (
    id             BIGINT AUTO_INCREMENT PRIMARY KEY,
    codigo         VARCHAR(20)  NOT NULL COMMENT 'CEN, PLA, ALA... se usa como prefijo de las plazas',
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
    tarifa_base    DOUBLE       NOT NULL COMMENT 'Tarifa/hora de referencia de la sede',
    servicios      VARCHAR(200) NOT NULL DEFAULT '',
    activa         TINYINT(1)   NOT NULL DEFAULT 1,
    UNIQUE KEY uq_sedes_codigo (codigo),
    KEY ix_sedes_sector (sector)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------------------------
-- 3) ESPACIOS  (plazas de estacionamiento, siempre dentro de una sede)
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS espacios (
    id            BIGINT AUTO_INCREMENT PRIMARY KEY,
    codigo        VARCHAR(20)  NOT NULL COMMENT 'SEDE-ZONA-NUMERO, p. ej. CEN-A-07',
    zona          VARCHAR(80)  NOT NULL,
    tipo_vehiculo VARCHAR(20)  NOT NULL COMMENT 'AUTO | MOTO | CAMIONETA | DISCAPACIDAD | EMBARAZADA',
    ocupado       TINYINT(1)   NOT NULL DEFAULT 0,
    tarifa_hora   DOUBLE       NOT NULL,
    nivel         INT          NULL COMMENT 'Puede ser negativo (sótano)',
    descripcion   VARCHAR(255) NULL,
    sede_id       BIGINT       NULL,
    UNIQUE KEY uq_espacios_codigo (codigo),
    KEY ix_espacios_ocupado (ocupado),
    KEY ix_espacios_zona (zona),
    KEY ix_espacios_sede (sede_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------------------------
-- 4) VEHICULOS
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS vehiculos (
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ----------------------------------------------------------------------------
-- 5) TICKETS  (entrada / salida y cobro)
--    La unicidad "1 ticket ABIERTO por placa" y "1 por plaza" se controla en la
--    aplicación (no hay índice parcial en MySQL para eso).
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS tickets (
    id                   BIGINT AUTO_INCREMENT PRIMARY KEY,
    codigo               VARCHAR(20)  NOT NULL COMMENT 'Formato TK-XXXXXXXX',
    placa                VARCHAR(20)  NOT NULL,
    espacio_id           BIGINT       NOT NULL,
    espacio_codigo       VARCHAR(20)  NULL,
    zona                 VARCHAR(80)  NULL,
    entrada              DATETIME(6)  NOT NULL,
    salida               DATETIME(6)  NULL,
    estado               VARCHAR(20)  NOT NULL DEFAULT 'ABIERTO' COMMENT 'ABIERTO | CERRADO | ANULADO',
    tarifa_hora          DOUBLE       NULL COMMENT 'Se copia de la plaza al abrir',
    monto_total          DOUBLE       NULL COMMENT 'NULL mientras el ticket está abierto',
    usuario              VARCHAR(50)  NULL,
    liberacion_pendiente TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '1 = cobrado pero la plaza no se pudo liberar',
    modo                 VARCHAR(10)  NOT NULL DEFAULT 'AHORA' COMMENT 'AHORA = entra ya | RESERVA = guardado para una franja',
    salida_prevista      DATETIME(6)  NULL COMMENT 'Hora de salida reservada',
    UNIQUE KEY uq_tickets_codigo (codigo),
    KEY ix_tickets_estado (estado),
    KEY ix_tickets_placa (placa),
    KEY ix_tickets_espacio (espacio_id),
    KEY ix_tickets_usuario (usuario),
    KEY ix_tickets_pendiente (liberacion_pendiente)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--

-- ============================================================================
--  DATOS SEMILLA
-- ============================================================================

-- --- 12 parqueaderos de Quito, cada uno en una dirección distinta -----------
INSERT INTO sedes (codigo, nombre, direccion, sector, latitud, longitud, telefono, horario,
                   techado, vigilancia, acepta_reserva, tarifa_base, servicios, activa) VALUES
('CEN', 'Urban Park Centro Histórico', 'Av. 10 de Agosto N21-56 y Av. Roca',            'Centro',            -0.20776, -78.49184, '02 258 7410', '24 horas',      1, 1, 1, 3.5, 'Techado · Cámaras · Baños · Wi-Fi',     1),
('PLA', 'Urban Park Plaza Grande',     'García Moreno y Sucre, frente a la Catedral',   'Centro Histórico',  -0.22010, -78.51230, '02 394 5520', '06:00 a 23:00', 0, 1, 1, 4.2, 'Cámaras · Baños · Cuidadores',          1),
('ALA', 'Urban Park La Alameda',       'Av. Gran Colombia y Av. 10 de Agosto',          'La Alameda',        -0.21450, -78.50600, '02 258 7410', '24 horas',      0, 1, 1, 3.0, 'Cámaras · Baños · Lavado',              1),
('SAN', 'Urban Park San Blas',         'Calle Chimborazo y Av. Pichincha',              'San Blas',          -0.21750, -78.50650, '02 394 5520', '06:00 a 22:00', 1, 1, 1, 2.4, 'Techado · Cámaras · Aseo',              1),
('GON', 'Urban Park González Suárez',  'Av. González Suárez y Av. Patria',              'González Suárez',   -0.20650, -78.49750, '02 258 7410', '24 horas',      1, 1, 1, 3.8, 'Techado · Cámaras · Baños · Wi-Fi',     1),
('COL', 'Urban Park Colón',            'Av. Colón y Av. 6 de Diciembre',                'La Colón',          -0.19900, -78.48900, '02 394 5520', '24 horas',      1, 1, 1, 3.2, 'Techado · Cámaras · Baños',             1),
('FOC', 'Urban Park La Foch',          'Av. Reina Victoria N24-56 y La Pinta',          'La Foch',           -0.20180, -78.48850, '02 258 7410', '24 horas',      0, 1, 1, 2.8, 'Cámaras · Baños · Carga eléctrica',     1),
('MAR', 'Urban Park Mariscal',         'Av. Amazonas N30-12 y Av. Eloy Alfaro',         'La Mariscal',       -0.18700, -78.48800, '02 394 5520', '24 horas',      0, 1, 1, 2.6, 'Cámaras · Aseo · Lavado',               1),
('CAR', 'Urban Park La Carolina',      'Av. Naciones Unidas y Núñez de Vela',           'La Carolina',       -0.17950, -78.48130, '02 258 7410', '24 horas',      0, 1, 1, 3.6, 'Cámaras · Baños · Zona verde',          1),
('QUI', 'Urban Park Quicentro',        'Av. Naciones Unidas y Av. 6 de Diciembre',      'Iñaquito',          -0.17580, -78.48300, '02 394 5520', '24 horas',      1, 1, 1, 4.2, 'Techado · Cámaras · Baños · Wi-Fi · Valet', 1),
('NAC', 'Urban Park Naciones Unidas',  'Av. Naciones Unidas y Av. Japón',               'Naciones Unidas',   -0.17320, -78.48650, '02 258 7410', '24 horas',      1, 1, 1, 3.4, 'Techado · Cámaras · Baños · Carga eléctrica', 1),
('EBA', 'Urban Park El Batán',         'Av. Eloy Alfaro y Av. Naciones Unidas',         'El Batán',          -0.18000, -78.47300, '02 394 5520', '06:00 a 23:00', 0, 1, 1, 2.9, 'Cámaras · Baños · Aseo',                1)
ON DUPLICATE KEY UPDATE
    nombre = VALUES(nombre), direccion = VALUES(direccion), sector = VALUES(sector),
    latitud = VALUES(latitud), longitud = VALUES(longitud), telefono = VALUES(telefono),
    horario = VALUES(horario), techado = VALUES(techado), vigilancia = VALUES(vigilancia),
    acepta_reserva = VALUES(acepta_reserva), tarifa_base = VALUES(tarifa_base),
    servicios = VALUES(servicios), activa = VALUES(activa);

-- --- 768 plazas: 64 por sede (4 zonas de 16) --------------------------------
--     En cada zona: 2 discapacidad, 2 motos, 2 camionetas, 9 autos y 1
--     puesto para embarazadas. El código lleva la sede delante (CEN-A-07).

-- Primero, las plazas de una base anterior (A-01..D-16 sin sede) pasan a CEN.
UPDATE espacios SET codigo = CONCAT('CEN-', codigo)
WHERE sede_id IS NULL AND codigo REGEXP '^[A-D]-[0-9]{2}$';

INSERT IGNORE INTO espacios (codigo, zona, tipo_vehiculo, ocupado, tarifa_hora, nivel, descripcion, sede_id)
WITH RECURSIVE numeros AS (
    SELECT 1 AS i
    UNION ALL
    SELECT i + 1 FROM numeros WHERE i < 16
),
zonas AS (
    -- sede, prefijo, nombre de la zona, nivel, tarifa auto, moto, camioneta, discapacidad
              SELECT 'CEN' AS sede, 'A' AS pref, 'Zona A - Centro'  AS zona,  1 AS nivel, 3.5 AS auto, 1.5 AS moto, 4.0 AS camioneta, 2.0 AS disc
    UNION ALL SELECT 'CEN', 'B', 'Zona B - Norte',   2, 2.8, 1.5, 4.0, 2.0
    UNION ALL SELECT 'CEN', 'C', 'Zona C - Sur',    -1, 2.5, 1.5, 3.8, 2.0
    UNION ALL SELECT 'CEN', 'D', 'Zona D - Este',    1, 3.0, 1.5, 4.2, 2.0
    UNION ALL SELECT 'PLA', 'A', 'Zona A - Centro Histórico PB',       1, 4.2, 1.5, 4.8, 2.0
    UNION ALL SELECT 'PLA', 'B', 'Zona B - Centro Histórico P1',       2, 3.8, 1.5, 4.6, 2.0
    UNION ALL SELECT 'PLA', 'C', 'Zona C - Centro Histórico S1',      -1, 3.4, 1.5, 4.6, 2.0
    UNION ALL SELECT 'PLA', 'D', 'Zona D - Centro Histórico Exterior', 1, 4.4, 1.5, 5.0, 2.0
    UNION ALL SELECT 'ALA', 'A', 'Zona A - La Alameda PB',       1, 3.0, 1.5, 3.5, 2.0
    UNION ALL SELECT 'ALA', 'B', 'Zona B - La Alameda P1',       2, 2.7, 1.5, 3.3, 2.0
    UNION ALL SELECT 'ALA', 'C', 'Zona C - La Alameda S1',      -1, 2.4, 1.5, 3.3, 2.0
    UNION ALL SELECT 'ALA', 'D', 'Zona D - La Alameda Exterior', 1, 3.2, 1.5, 3.6, 2.0
    UNION ALL SELECT 'SAN', 'A', 'Zona A - San Blas PB',       1, 2.4, 1.5, 2.8, 2.0
    UNION ALL SELECT 'SAN', 'B', 'Zona B - San Blas P1',       2, 2.2, 1.5, 2.6, 2.0
    UNION ALL SELECT 'SAN', 'C', 'Zona C - San Blas S1',      -1, 1.9, 1.5, 2.6, 2.0
    UNION ALL SELECT 'SAN', 'D', 'Zona D - San Blas Exterior', 1, 2.5, 1.5, 2.9, 2.0
    UNION ALL SELECT 'GON', 'A', 'Zona A - González Suárez PB',       1, 3.8, 1.5, 4.4, 2.0
    UNION ALL SELECT 'GON', 'B', 'Zona B - González Suárez P1',       2, 3.4, 1.5, 4.2, 2.0
    UNION ALL SELECT 'GON', 'C', 'Zona C - González Suárez S1',      -1, 3.0, 1.5, 4.2, 2.0
    UNION ALL SELECT 'GON', 'D', 'Zona D - González Suárez Exterior', 1, 4.0, 1.5, 4.6, 2.0
    UNION ALL SELECT 'COL', 'A', 'Zona A - La Colón PB',       1, 3.2, 1.5, 3.7, 2.0
    UNION ALL SELECT 'COL', 'B', 'Zona B - La Colón P1',       2, 2.9, 1.5, 3.5, 2.0
    UNION ALL SELECT 'COL', 'C', 'Zona C - La Colón S1',      -1, 2.6, 1.5, 3.5, 2.0
    UNION ALL SELECT 'COL', 'D', 'Zona D - La Colón Exterior', 1, 3.4, 1.5, 3.8, 2.0
    UNION ALL SELECT 'FOC', 'A', 'Zona A - La Foch PB',       1, 2.8, 1.5, 3.2, 2.0
    UNION ALL SELECT 'FOC', 'B', 'Zona B - La Foch P1',       2, 2.5, 1.5, 3.1, 2.0
    UNION ALL SELECT 'FOC', 'C', 'Zona C - La Foch S1',      -1, 2.2, 1.5, 3.1, 2.0
    UNION ALL SELECT 'FOC', 'D', 'Zona D - La Foch Exterior', 1, 2.9, 1.5, 3.4, 2.0
    UNION ALL SELECT 'MAR', 'A', 'Zona A - La Mariscal PB',       1, 2.6, 1.5, 3.0, 2.0
    UNION ALL SELECT 'MAR', 'B', 'Zona B - La Mariscal P1',       2, 2.3, 1.5, 2.9, 2.0
    UNION ALL SELECT 'MAR', 'C', 'Zona C - La Mariscal S1',      -1, 2.1, 1.5, 2.9, 2.0
    UNION ALL SELECT 'MAR', 'D', 'Zona D - La Mariscal Exterior', 1, 2.7, 1.5, 3.1, 2.0
    UNION ALL SELECT 'CAR', 'A', 'Zona A - La Carolina PB',       1, 3.6, 1.5, 4.1, 2.0
    UNION ALL SELECT 'CAR', 'B', 'Zona B - La Carolina P1',       2, 3.2, 1.5, 4.0, 2.0
    UNION ALL SELECT 'CAR', 'C', 'Zona C - La Carolina S1',      -1, 2.9, 1.5, 4.0, 2.0
    UNION ALL SELECT 'CAR', 'D', 'Zona D - La Carolina Exterior', 1, 3.8, 1.5, 4.3, 2.0
    UNION ALL SELECT 'QUI', 'A', 'Zona A - Iñaquito PB',       1, 4.2, 1.5, 4.8, 2.0
    UNION ALL SELECT 'QUI', 'B', 'Zona B - Iñaquito P1',       2, 3.8, 1.5, 4.6, 2.0
    UNION ALL SELECT 'QUI', 'C', 'Zona C - Iñaquito S1',      -1, 3.4, 1.5, 4.6, 2.0
    UNION ALL SELECT 'QUI', 'D', 'Zona D - Iñaquito Exterior', 1, 4.4, 1.5, 5.0, 2.0
    UNION ALL SELECT 'NAC', 'A', 'Zona A - Naciones Unidas PB',       1, 3.4, 1.5, 3.9, 2.0
    UNION ALL SELECT 'NAC', 'B', 'Zona B - Naciones Unidas P1',       2, 3.1, 1.5, 3.7, 2.0
    UNION ALL SELECT 'NAC', 'C', 'Zona C - Naciones Unidas S1',      -1, 2.7, 1.5, 3.7, 2.0
    UNION ALL SELECT 'NAC', 'D', 'Zona D - Naciones Unidas Exterior', 1, 3.6, 1.5, 4.1, 2.0
    UNION ALL SELECT 'EBA', 'A', 'Zona A - El Batán PB',       1, 2.9, 1.5, 3.3, 2.0
    UNION ALL SELECT 'EBA', 'B', 'Zona B - El Batán P1',       2, 2.6, 1.5, 3.2, 2.0
    UNION ALL SELECT 'EBA', 'C', 'Zona C - El Batán S1',      -1, 2.3, 1.5, 3.2, 2.0
    UNION ALL SELECT 'EBA', 'D', 'Zona D - El Batán Exterior', 1, 3.0, 1.5, 3.5, 2.0
)
SELECT CONCAT(z.sede, '-', z.pref, '-', LPAD(n.i, 2, '0')) AS codigo,
       z.zona,
       CASE WHEN n.i <= 2 THEN 'DISCAPACIDAD'
            WHEN n.i <= 4 THEN 'MOTO'
            WHEN n.i <= 6 THEN 'CAMIONETA'
            WHEN n.i = 16 THEN 'EMBARAZADA'
            ELSE 'AUTO' END AS tipo_vehiculo,
       0 AS ocupado,
       CASE WHEN n.i <= 2 THEN z.disc
            WHEN n.i <= 4 THEN z.moto
            WHEN n.i <= 6 THEN z.camioneta
            ELSE z.auto END AS tarifa_hora,
       z.nivel,
       CASE WHEN n.i <= 2 THEN 'Plaza preferencial'
            WHEN n.i <= 4 THEN 'Plaza de motos'
            WHEN n.i <= 6 THEN 'Plaza amplia'
            WHEN n.i = 16 THEN 'Plaza para embarazadas'
            ELSE 'Plaza estándar' END AS descripcion,
       s.id AS sede_id
FROM zonas z
JOIN sedes s ON s.codigo = z.sede
CROSS JOIN numeros n;

-- --- cualquier plaza huérfana se asigna a la sede principal -----------------
UPDATE espacios SET sede_id = (SELECT id FROM sedes WHERE codigo = 'CEN')
WHERE sede_id IS NULL OR sede_id = 0;

-- --- 4 vehículos precargados ------------------------------------------------
INSERT IGNORE INTO vehiculos (placa, tipo, marca, color, propietario, propietario_email, propietario_username) VALUES
('ABC-123', 'AUTO',      'Toyota',    'Gris',   'Ana Cliente',   'cliente1@urbanpark.local',  'cliente1'),
('XYZ-789', 'MOTO',      'Yamaha',    'Rojo',   'Luis Operador', 'operador1@urbanpark.local', 'operador1'),
('DEF-456', 'AUTO',      'Chevrolet', 'Azul',   'Pedro Cliente', 'cliente2@urbanpark.local',  'cliente2'),
('GHI-789', 'CAMIONETA', 'Ford',      'Blanco', 'María Cliente', 'cliente3@urbanpark.local',  'cliente3');

-- --- 5 usuarios demo --------------------------------------------------------
--   cliente1  / cliente123    -> CLIENTE
--   cliente2  / cliente123    -> CLIENTE
--   cliente3  / cliente123    -> CLIENTE
--   operador1 / operador123   -> OPERADOR
--   admin1    / admin123      -> ADMIN,OPERADOR
INSERT IGNORE INTO usuarios (username, email, first_name, last_name, password_hash, roles, enabled, creado_en) VALUES
('cliente1',  'cliente1@urbanpark.local',  'Ana',   'Cliente',  'pbkdf2$100000$o6hSk1YMFbT2BuTMllvzuQ==$ALd83ChE1GMMVKUnpbvZzsThYYC4g6qfZTtUIy6oSYc=', 'CLIENTE',          1, NOW(6)),
('cliente2',  'cliente2@urbanpark.local',  'Pedro', 'Cliente',  'pbkdf2$100000$v5xmFlu1zXeBH/spHUwiXg==$34+HJ5MGrFD+oljU8VtvFzDFWCb+zNacTGqtfge/RCo=', 'CLIENTE',          1, NOW(6)),
('cliente3',  'cliente3@urbanpark.local',  'María', 'Cliente',  'pbkdf2$100000$Ig2G8NZrBfcP9O+goVkPDw==$dXYlFhnuqtM60MxFnUoxQdHIaD60EZs6zOgOMICIucQ=', 'CLIENTE',          1, NOW(6)),
('operador1', 'operador1@urbanpark.local', 'Luis',  'Operador', 'pbkdf2$100000$kR5h5MoFilXtf5rHS89Zag==$TAk/+Yn/8UWeJNhglWDMX7/t9bvArQjz3KuEwVlHOpQ=', 'OPERADOR',         1, NOW(6)),
('admin1',    'admin1@urbanpark.local',    'Admin', 'General',  'pbkdf2$100000$a3a0iBbFN8gpz7sklLvvZw==$nXLVpZpIa2yh1U7X0UQa184FWJDz729hD89dZZE2lgA=', 'ADMIN,OPERADOR',   1, NOW(6));

-- ============================================================================
--  VERIFICACIÓN
-- ============================================================================
SELECT 'sedes' AS tabla, COUNT(*) AS filas FROM sedes
UNION ALL SELECT 'espacios', COUNT(*) FROM espacios
UNION ALL SELECT 'zonas', COUNT(DISTINCT zona) FROM espacios
UNION ALL SELECT 'usuarios', COUNT(*) FROM usuarios
UNION ALL SELECT 'vehiculos', COUNT(*) FROM vehiculos
UNION ALL SELECT 'tickets', COUNT(*) FROM tickets;
