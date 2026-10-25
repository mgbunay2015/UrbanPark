# URBAN PARK — Especificación COMPLETA del Backend

> **Fuente:** lectura literal de todo el código de `urban-park-proyecto-main` (5 microservicios Java/Spring Boot 3.2.5 + Java 17, Keycloak 24.0, Nginx, Docker Compose).
> **Objetivo:** documento único de referencia para reimplementar el backend en **ASP.NET Core (C#) + MySQL**.
> **Aviso importante sobre la base de datos:** el código original usa **PostgreSQL 16** (`org.postgresql:postgresql` en los 3 `pom.xml` con persistencia y `jdbc:postgresql://` en los `application.yml`). El usuario pide la reimplementación en MySQL; en la §2 se da el tipo PostgreSQL real **y** su equivalente MySQL.

---

## 0. MAPA GENERAL DE ARQUITECTURA

| Servicio | Puerto interno | Puerto host | Persistencia | Responsabilidad |
|---|---|---|---|---|
| `ms-espacios` | 8081 | no publicado | PostgreSQL `db_espacios` | Catálogo de zonas/plazas, disponibilidad, ocupación |
| `ms-vehiculos` | 8082 | no publicado | PostgreSQL `db_vehiculos` | Registro de vehículos y propietarios |
| `ms-tickets` | 8083 | no publicado | PostgreSQL `db_tickets` | Tickets entrada/salida, cobro, resilience, demo Docker |
| `ms-ia` | 8084 | no publicado | **sin base de datos** (stateless) | Recomendación/asignación con Ollama + reportes |
| `ms-auth` | 8085 | no publicado | **sin base de datos** | Usuarios/perfiles vía Keycloak Admin API |
| `keycloak` | 8080 | **8091** | (H2 interna de Keycloak) | Emisor JWT, roles, usuarios |
| `nginx` | 80 | **8090** | — | Reverse proxy + frontend estático |
| `ollama` (perfil `ollama-docker`) | 11434 | no publicado | volumen `vol_ollama` | Modelo LLM |

**Dependencias entre servicios (llamadas HTTP):**
- `ms-tickets` → `ms-espacios` (OpenFeign, `EspaciosClient`), con Resilience4j (Circuit Breaker + Retry) **solo** en `ocupacionRemota()`.
- `ms-ia` → `ms-espacios` (`EspaciosIaClient`) y → `ms-tickets` (`TicketsIaClient`), OpenFeign sin Resilience4j.
- `ms-auth` → Keycloak Admin REST API (WebClient/WebFlux), usando service account `urbanpark-backend`.
- Todos los servicios validan JWT contra el **JWKS** de Keycloak (`jwk-set-uri`). **Ninguno valida `issuer` ni `audience`.**

Stack común: Java 17, Spring Boot **3.2.5**, Spring Security (OAuth2 Resource Server), Spring Data JPA + Hibernate `ddl-auto: update`, Lombok, `@EnableMethodSecurity`, CSRF deshabilitado, sesiones `STATELESS`.

---

## 1. CONTRATO DE API COMPLETO

### 1.0 Convenciones transversales

**Autorización (dos capas):**
1. **Filtro HTTP** (`SecurityFilterChain`): toda petición exige JWT válido salvo las excepciones indicadas.
2. **`@PreAuthorize`** en método (roles concretos).

**Roles reconocidos por todos los servicios** (desde el claim `realm_access.roles` → autoridad `ROLE_<rol>`):
`CLIENTE`, `OPERADOR`, `ADMIN`.

**Claim de identidad de usuario:** `preferred_username` (se usa para propiedad de vehículos y tickets).

**Formato de error estándar** (Spring Boot `server.error.include-message: always`):
```json
{
  "timestamp": "2026-01-01T10:00:00.000+00:00",
  "status": 409,
  "error": "Conflict",
  "message": "La plaza ya está ocupada",
  "path": "/api/espacios/1/ocupar"
}
```
Solo `ms-tickets` y `ms-ia` declaran explícitamente `include-message: always`; `ms-espacios`, `ms-vehiculos` y `ms-auth` usan el **valor por defecto de Spring Boot 3** (`include-message: never`) → en esos tres, el campo `message` normalmente **no** aparece en el JSON de error.

**Semántica de rol "cliente solo":** helper `AuthSupport.esClienteSolo(jwt)` devuelve `true` únicamente si el rol principal (ADMIN > OPERADOR > CLIENTE) es `CLIENTE`. Es decir: un usuario con `ADMIN` **o** `OPERADOR` ve todos los recursos; un usuario con únicamente `CLIENTE` ve solo los suyos. Un usuario **sin ninguno de los 3 roles** (p. ej. solo un rol ajeno) tiene `rolPrincipal = null` → `esClienteSolo = false` → **vería todos los recursos** (pero el `@PreAuthorize` normalmente lo bloquea antes).

---

### 1.1 ms-espacios — puerto 8081 — base `/api/espacios`

`@CrossOrigin` abierto. `@EnableMethodSecurity`.
`SecurityFilterChain`: `GET /api/espacios/**` → `authenticated()`; cualquier otra ruta → `authenticated()`. No hay rutas públicas (ni siquiera `/actuator/**`... sí: `requestMatchers("/actuator/**").permitAll()`).

#### 1.1.1 `GET /api/espacios` — Listar todas las plazas
- **Auth:** JWT válido (cualquier rol autenticado). Sin `@PreAuthorize`.
- **Parámetros:** ninguno.
- **Cuerpo:** no.
- **Respuesta 200** — `List<Espacio>` (array JSON):

| Campo | Tipo | Nulo |
|---|---|---|
| `id` | number (int64) | no |
| `codigo` | string | no |
| `zona` | string | no |
| `tipoVehiculo` | string (`AUTO`\|`MOTO`\|`CAMIONETA`\|`DISCAPACIDAD`) | no |
| `ocupado` | boolean | no |
| `tarifaHora` | number (double) | no |
| `nivel` | number (int32) | sí |
| `descripcion` | string | sí |

- **Errores:** `401` si falta/está mal el JWT (cuerpo de error sin `message`).

#### 1.1.2 `GET /api/espacios/disponibles` — Plazas libres filtradas
- **Auth:** JWT válido.
- **Query params:**
  - `tipo` (string, opcional) — se normaliza a **MAYÚSCULAS** (`tipo.toUpperCase()`).
  - `zona` (string, opcional) — coincidencia **exacta** (no `LIKE`).
- **Lógica de precedencia (importante):** si `tipo` viene no vacío, **ignora `zona`**; si `tipo` es vacío/nulo y `zona` no, filtra por zona; si ambos vacíos, devuelve todas las libres.
- **Respuesta 200:** `List<Espacio>` igual que 1.1.1.
- **Errores:** `401`.

#### 1.1.3 `GET /api/espacios/ocupacion` — Métricas de ocupación
- **Auth:** JWT válido.
- **Respuesta 200** — objeto (`Map<String,Object>`):

| Campo | Tipo | Cálculo |
|---|---|---|
| `total` | number | `count()` de todas las plazas |
| `ocupados` | number | `countByOcupadoTrue()` |
| `libres` | number | `total - ocupados` |
| `porcentajeGlobal` | number | `round(ocupados * 100.0 / total)`, `0` si `total == 0` (redondeo entero) |
| `zonas` | object (map) | clave = nombre de zona; valor = objeto `{ "total": n, "ocupados": n, "libres": n, "porcentaje": n }` con `porcentaje = round(o*100.0/t)` |

- **Nota:** `Map.of(...)` no admite nulos; los valores siempre están presentes. El `zonas` se agrupa por el campo `zona` (String) de las plazas.
- **Errores:** `401`.

#### 1.1.4 `GET /api/espacios/{id}` — Obtener plaza
- **Auth:** JWT válido.
- **Path:** `id` (number, int64, obligatorio).
- **Respuesta 200:** `Espacio`.
- **Errores:**
  - `404` → `ResponseStatusException(NOT_FOUND, "Espacio no encontrado")`.
  - `401`.

#### 1.1.5 `POST /api/espacios` — Crear plaza
- **Auth:** `hasAnyRole('OPERADOR','ADMIN')`.
- **Cuerpo:** `Espacio` (JSON). En `crear()`: `id` se fuerza a `null`; si `ocupado == null` se pone `false`.
  - `codigo` string — `@Column(nullable=false, unique=true)` → **obligatorio y único** (violación → `DataIntegrityViolationException` → HTTP 500).
  - `zona` string — `nullable=false` → obligatorio.
  - `tipoVehiculo` string — `nullable=false` → obligatorio.
  - `ocupado` boolean — opcional (default `false`).
  - `tarifaHora` number double — `nullable=false` → obligatorio.
  - `nivel` number int32 — opcional.
  - `descripcion` string — opcional.
  - **No hay anotaciones de Bean Validation** (`@NotNull`, etc.) en la entidad; la única barrera es el esquema de BD.
- **Respuesta 201/200:** `Espacio` creado con `id` asignado (Spring devuelve `200 OK` por defecto, no `201`, al no usar `ResponseEntity`).
- **Errores:** `401`; `403` (rol CLIENTE); `500` si `codigo` duplicado o falta campo no nulo.

#### 1.1.6 `PUT /api/espacios/{id}` — Actualizar plaza
- **Auth:** `hasAnyRole('OPERADOR','ADMIN')`.
- **Path:** `id`.
- **Cuerpo:** `Espacio`. **Solo se copian**: `codigo`, `zona`, `tipoVehiculo`, `tarifaHora`, `nivel`, `descripcion`.
  - **`ocupado` NO se actualiza nunca** desde este endpoint (se preserva el valor en BD). Tampoco se actualiza `id`.
- **Respuesta 200:** `Espacio` actualizado.
- **Errores:** `404` "Espacio no encontrado"; `401`; `403`; `500` por `codigo` duplicado.

#### 1.1.7 `POST /api/espacios/{id}/ocupar` — Ocupar plaza
- **Auth:** `hasAnyRole('CLIENTE','OPERADOR','ADMIN')` (necesario porque lo invoca `ms-tickets` con el token del usuario).
- **Path:** `id`.
- **Cuerpo:** no.
- **Lógica:** `obtener(id)` → si `ocupado == true` lanza conflicto → si no, `ocupado = true`, guarda.
- **Respuesta 200:** `Espacio` con `ocupado: true`.
- **Errores:**
  - `404` → "Espacio no encontrado".
  - `409` → `"La plaza ya está ocupada"`.
  - `401`, `403`.
- **Idempotencia:** **no** es idempotente (segunda llamada da 409).

#### 1.1.8 `POST /api/espacios/{id}/liberar` — Liberar plaza
- **Auth:** `hasAnyRole('CLIENTE','OPERADOR','ADMIN')`.
- **Path:** `id`.
- **Cuerpo:** no.
- **Lógica:** `obtener(id)` → `ocupado = false` → guarda. **Sin comprobar si ya estaba libre.**
- **Respuesta 200:** `Espacio` con `ocupado: false`.
- **Errores:** `404` "Espacio no encontrado"; `401`; `403`.
- **Idempotencia:** **sí** (liberar dos veces devuelve 200 ambas).

---

### 1.2 ms-vehiculos — puerto 8082 — base `/api/vehiculos`

`@CrossOrigin` abierto. `SecurityFilterChain`: `/actuator/**` permitAll; **cualquier otra** `authenticated()`.

#### 1.2.1 `GET /api/vehiculos` — Listar vehículos
- **Auth:** `hasAnyRole('CLIENTE','OPERADOR','ADMIN')`.
- **Filtrado por rol:** si `esClienteSolo` → `findByPropietarioUsernameIgnoreCase(username)`; si no → `findAll()`.
- **Respuesta 200** — `List<Vehiculo>`:

| Campo | Tipo | Nulo |
|---|---|---|
| `id` | number int64 | no |
| `placa` | string | no |
| `tipo` | string (`AUTO`\|`MOTO`\|`CAMIONETA`) | no |
| `marca` | string | sí |
| `color` | string | sí |
| `propietario` | string | no |
| `propietarioEmail` | string | sí |
| `propietarioUsername` | string | sí |

- **Errores:** `401`, `403`.

#### 1.2.2 `GET /api/vehiculos/{id}` — Obtener vehículo
- **Auth:** `hasAnyRole('CLIENTE','OPERADOR','ADMIN')`.
- **Path:** `id`.
- **Control de acceso:** `asegurarAcceso` → si CLIENTE y `propietarioUsername` ≠ `preferred_username` (o cualquiera es nulo) → `403`.
- **Respuesta 200:** `Vehiculo`.
- **Errores:** `404` `"Vehículo no encontrado"`; `403` `"Solo puedes gestionar tus propios vehículos"`; `401`.

#### 1.2.3 `GET /api/vehiculos/placa/{placa}` — Buscar por placa
- **Auth:** `hasAnyRole('CLIENTE','OPERADOR','ADMIN')`.
- **Path:** `placa` (string).
- **Búsqueda:** `findByPlacaIgnoreCase(placa)` (case-insensitive, **sin normalizar** guiones/espacios).
- **Respuesta 200:** `Vehiculo`.
- **Errores:** `404` `"Placa no registrada"`; `403` si es de otro cliente; `401`.

#### 1.2.4 `POST /api/vehiculos` — Registrar vehículo
- **Auth:** `hasAnyRole('CLIENTE','OPERADOR','ADMIN')`.
- **Cuerpo:** `Vehiculo`.

| Campo | Tipo | Obligatorio | Validación |
|---|---|---|---|
| `placa` | string | **sí** | No nula/no blanca → si no: `400 "La placa es obligatoria"`; luego `placa.trim().toUpperCase().replace(" ","")`; se exige `length` entre **4 y 12** y `matches("^[A-Z0-9]{2,8}(-?[A-Z0-9]{1,4})?$")` → si no: `400 "Placa inválida. Usa formato tipo ABC-123"` |
| `tipo` | string | no | si nulo → `"AUTO"`; si no, se pasa a **MAYÚSCULAS** |
| `marca` | string | no | — |
| `color` | string | no | — |
| `propietario` | string | **sí** | `@Column(nullable=false)` + validación explícita: si nulo/blanco → `400 "El propietario es obligatorio"` |
| `propietarioEmail` | string | no | — |
| `propietarioUsername` | string | no | Ver reglas de asignación abajo |

- **Reglas de asignación de `propietarioUsername`:**
  - Si el llamante es **CLIENTE** → **siempre** se fuerza `propietarioUsername = preferred_username` (ignora el valor enviado).
  - Si el llamante **no** es CLIENTE → si `propietarioUsername` viene nulo/blanco se pone `preferred_username`; si viene, se respeta.
  - (La comprobación interna `if (v.getPropietario() == null || ...)` es **código muerto**: `propietario` ya se validó arriba.)
- **Unicidad:** `findByPlacaIgnoreCase(placa)`; si existe → `409` `"La placa ya está registrada"`.
- **`id`** se fuerza a `null` (ignora el enviado).
- **Respuesta 200:** `Vehiculo` creado.
- **Errores:** `400` (placa/propietario), `409` (placa duplicada), `401`, `403`.

#### 1.2.5 `PUT /api/vehiculos/{id}` — Actualizar vehículo
- **Auth:** `hasAnyRole('OPERADOR','ADMIN')`.
- **Path:** `id`.
- **Control de acceso:** `obtener(id, jwt)` → para OPERADOR/ADMIN no aplica restricción de propiedad.
- **Cuerpo:** `Vehiculo`. **Campos copiados:** `marca`, `color`, `tipo`, `propietario`, `propietarioEmail`; y **solo si el llamante no es CLIENTE** y `propietarioUsername != null` → `propietarioUsername`.
- **`placa` NO se puede modificar** por este endpoint (no se copia).
- **Respuesta 200:** `Vehiculo`.
- **Errores:** `404`, `401`, `403`.

#### 1.2.6 `DELETE /api/vehiculos/{id}` — Eliminar vehículo
- **Auth:** `hasAnyRole('OPERADOR','ADMIN')`.
- **Path:** `id`.
- **Respuesta:** `void` → **HTTP 200 con cuerpo vacío** (no 204).
- **Errores:** `404`; `401`; `403`. (La comprobación `esClienteSolo → 403 "El cliente no puede eliminar vehículos; contacta al operador"` es **inalcanzable**, porque el `@PreAuthorize` ya rechaza a CLIENTE).

---

### 1.3 ms-tickets — puerto 8083 — base `/api/tickets`

`@CrossOrigin` abierto. `SecurityFilterChain`: `/actuator/**` permitAll; resto `authenticated()`.
Actuator expuesto: `health,info,circuitbreakers,circuitbreakerevents`.

**Todas** las operaciones salvo las indicadas requieren cabecera `Authorization: Bearer <jwt>`.

#### 1.3.1 `GET /api/tickets` — Listar tickets
- **Auth:** `hasAnyRole('CLIENTE','OPERADOR','ADMIN')`.
- **Filtrado:** CLIENTE → `findByUsuarioIgnoreCase(username)`; OPERADOR/ADMIN → `findAll()`.
- **Respuesta 200** — `List<Ticket>`:

| Campo | Tipo | Nulo |
|---|---|---|
| `id` | number int64 | no |
| `codigo` | string | no |
| `placa` | string | no |
| `espacioId` | number int64 | no |
| `espacioCodigo` | string | sí |
| `zona` | string | sí |
| `entrada` | string ISO-8601 `yyyy-MM-dd'T'HH:mm:ss` (LocalDateTime) | no |
| `salida` | string ISO-8601 | sí |
| `estado` | string (`ABIERTO`\|`CERRADO`) | no |
| `tarifaHora` | number double | sí |
| `montoTotal` | number double | sí |
| `usuario` | string | sí |
| `liberacionPendiente` | boolean | no (default `false`) |

- **Errores:** `401`, `403`.

#### 1.3.2 `GET /api/tickets/abiertos` — Tickets abiertos
- **Auth:** `hasAnyRole('CLIENTE','OPERADOR','ADMIN')`.
- **Filtrado:** CLIENTE → `findByUsuarioIgnoreCaseAndEstado(username, "ABIERTO")`; resto → `findByEstado("ABIERTO")`.
- **Respuesta 200:** `List<Ticket>`.
- **Errores:** `401`, `403`.

#### 1.3.3 `GET /api/tickets/{id}` — Obtener ticket
- **Auth:** `hasAnyRole('CLIENTE','OPERADOR','ADMIN')`.
- **Path:** `id`.
- **Control de acceso (`asegurarAcceso`):** si CLIENTE y `ticket.usuario` ≠ `preferred_username` (o nulos) → `403`.
- **Respuesta 200:** `Ticket`.
- **Errores:** `404` `"Ticket no encontrado"`; `403` `"Solo puedes ver o cerrar tus propios tickets"`; `401`.

#### 1.3.4 `GET /api/tickets/ocupacion` — Ocupación vía Feign + Resilience4j ⭐
- **Auth:** `hasRole('ADMIN')`.
- **Header requerido explícito:** `Authorization` (se propaga tal cual a ms-espacios).
- **Ruta afectada por Circuit Breaker `espaciosClient` + Retry `espaciosClient`.**
- **Respuesta 200 (camino feliz):** el `Map` devuelto por `GET /api/espacios/ocupacion` **más**:
  - `"origen": "ms-espacios (live)"`
  - `"resilience": "Circuit Breaker + Retry OK"`
  - (y los campos `total`, `ocupados`, `libres`, `porcentajeGlobal`, `zonas`).
- **Respuesta 200 (fallback):**

```json
{
  "fallback": true,
  "origen": "ms-tickets fallback",
  "mensaje": "Resilience4j activó fallback: <ex.getMessage()>",
  "total": 0,
  "ocupados": 0,
  "libres": 0
}
```

- **Nota:** el fallback **nunca** devuelve error HTTP; siempre es `200` con `fallback: true`.
- **Errores:** `401` (sin JWT), `403` (no ADMIN).

#### 1.3.5 `GET /api/tickets/demo/espacios/status` — Estado del contenedor ms-espacios
- **Auth:** `hasRole('ADMIN')`.
- **Implementación:** `docker ps -q -f name=ms-espacios`; si vacío, `docker ps -aq -f name=ms-espacios` (silencioso).
- **Respuesta 200:**

| Campo | Tipo | Valor |
|---|---|---|
| `servicio` | string | `"ms-espacios"` |
| `running` | boolean | `true` si hay contenedor corriendo |
| `containerId` | string | id del contenedor o `""` |
| `mensaje` | string | `"ms-espacios está en ejecución"` \| `"ms-espacios está detenido"` |

- **Errores:** `401`, `403`; `503` si el CLI docker no está disponible (`"…— ¿ms-tickets tiene docker.sock y docker-cli?"`).

#### 1.3.6 `POST /api/tickets/demo/espacios/stop` — Detener ms-espacios (demo)
- **Auth:** `hasRole('ADMIN')`.
- **Cuerpo:** no.
- **Implementación:** resuelve el id (preferentemente contenedor **en ejecución** con `docker ps -q`; si no, `docker ps -aq`) y ejecuta `docker stop <id>`. Timeout de ejecución: **45 s** (si expira → `exitCode 124`).
- **Respuesta 200:**

| Campo | Tipo | Valor |
|---|---|---|
| `action` | string | `"stop"` |
| `servicio` | string | `"ms-espacios"` |
| `containerId` | string | id |
| `mensaje` | string | `"ms-espacios detenido. Prueba ahora Resilience (debería activar fallback)."` |
| `ok` | boolean | `true` |

- **Errores:** `404` `"No se encontró el contenedor ms-espacios. ¿Está el stack con docker compose?"` (si no existe el contenedor); `502` `"No se pudo detener ms-espacios: <salida>"` (exit code ≠ 0); `503` si falla el arranque del proceso; `401`, `403`.

#### 1.3.7 `POST /api/tickets/demo/espacios/start` — Iniciar ms-espacios (demo)
- **Auth:** `hasRole('ADMIN')`.
- **Implementación:** `docker ps -aq -f name=ms-espacios` + `docker start <id>`.
- **Respuesta 200:** mismos campos con `action: "start"`, `ok: true` y `mensaje: "ms-espacios iniciado. Espera unos segundos y prueba de nuevo."`
- **Errores:** idénticos a 1.3.6 (`404`, `502` `"No se pudo iniciar ms-espacios: <salida>"`, `503`, `401`, `403`).

#### 1.3.8 `POST /api/tickets/reconciliar-plazas` — Reconciliar plazas ↔ tickets ABIERTO
- **Auth:** `hasRole('ADMIN')`.
- **Header requerido:** `Authorization`.
- **Cuerpo:** no.
- **Algoritmo (ver §3.6).**
- **Respuesta 200** (`Map`, orden de inserción):

| Campo | Tipo | Descripción |
|---|---|---|
| `ticketsAbiertos` | number | nº de tickets en estado ABIERTO (recalculado al final) |
| `plazasConTicketAbierto` | number | nº de `espacioId` distintos con ticket ABIERTO |
| `plazasOcupadasTrasSync` | number | `ocupados` remoto (o `-1` si no parseable) |
| `plazasLibresTrasSync` | number | `libres` remoto (o `-1`) |
| `plazasTotal` | number | `total` remoto, o `plazas.size()` si `total < 0` |
| `porcentajeGlobal` | number | del remoto, o `null` |
| `ocupacion` | object | respuesta completa de `/api/espacios/ocupacion`, o `{}` si falló |
| `ajustesOcupar` | number | plazas ocupadas durante la reconciliación |
| `ajustesLiberar` | number | plazas liberadas durante la reconciliación |
| `duplicadosCerrados` | number | tickets duplicados cerrados |
| `sinCambio` | number | plazas que ya estaban correctas |
| `cuadran` | boolean | `ocupados == plazasConTicketAbierto && abiertos.size() == plazasConTicketAbierto` |
| `detalle` | array[string] | log textual de acciones |
| `mensaje` | string | `"Plazas ocupadas (N) = tickets abiertos (M)"` o `"Aún no cuadran: ocupadas=N vs abiertos=M"` |

- **Errores:** `404`/`409` según respuesta de espacios mapeados por llamada individual (no abortan el bucle; se registran en `detalle`); `503` `"No se pudieron listar plazas para reconciliar"` si falla `GET /api/espacios`; `401`, `403`.

#### 1.3.9 `POST /api/tickets/sincronizar-liberaciones` — Liberar plazas pendientes
- **Auth:** `hasAnyRole('CLIENTE','OPERADOR','ADMIN')`.
- **Header requerido:** `Authorization`.
- **Cuerpo:** no.
- **Selección de pendientes:** CLIENTE → `findByUsuarioIgnoreCaseAndLiberacionPendienteTrue(username)`; OPERADOR/ADMIN → `findByLiberacionPendienteTrue()`.
- **Respuesta 200 — caso sin pendientes:**

```json
{ "pendientes": 0, "liberadas": 0, "fallidas": 0,
  "mensaje": "No hay liberaciones pendientes", "detalle": [] }
```

- **Respuesta 200 — caso con pendientes:**

| Campo | Tipo | Descripción |
|---|---|---|
| `pendientesIniciales` | number | nº de tickets con `liberacionPendiente=true` al inicio |
| `liberadas` | number | liberadas OK (+ las que daban 404 en espacios) |
| `fallidas` | number | ms-espacios aún no disponible u otro error |
| `sinCambio` | number | había ticket ABIERTO en esa plaza → se canceló el pendiente |
| `pendientesRestantes` | number | `countByLiberacionPendienteTrue()` global al final |
| `mensaje` | string | `"Sincronizado: N plaza(s) marcada(s) como disponible(s)"` / `"Aún no se pudo sincronizar (¿ms-espacios sigue caído?)"` / `"Sin cambios"` |
| `detalle` | array[string] | una línea por ticket procesado |

- **Errores:** `401`, `403`.

#### 1.3.10 `POST /api/tickets/abrir` — Abrir ticket ⭐
- **Auth:** `hasAnyRole('CLIENTE','OPERADOR','ADMIN')`.
- **Header requerido:** `Authorization`.
- **Cuerpo (`AbrirRequest`):**

| Campo | Tipo | Obligatorio | Validación |
|---|---|---|---|
| `placa` | string | **sí** | `validarPlaca()` (ver §3.1) |
| `espacioId` | number int64 | **sí** | Si `null` o `<= 0` → `400 "Debes indicar una plaza válida"` |

- **Flujo:** valida placa → valida espacioId → comprueba ticket ABIERTO por placa → comprueba ticket ABIERTO por plaza → `POST /api/espacios/{id}/ocupar` (Feign, con el `Authorization` del usuario) → crea el ticket.
- **Generación del código:** `"TK-" + UUID.randomUUID().toString().substring(0,8).toUpperCase()` (8 primeros hex en mayúsculas). Ej.: `TK-3F9A2C1B`.
- **Respuesta 200:** `Ticket` con `estado: "ABIERTO"`, `entrada = now()`, `salida = null`, `montoTotal = null`, `liberacionPendiente = false`, y `tarifaHora`/`espacioCodigo`/`zona` copiados de la respuesta de espacios (la `plaza` devuelta por `ocupar` es la fuente).
- **Errores:**

| HTTP | Mensaje literal | Causa |
|---|---|---|
| 400 | `"La placa es obligatoria"` | placa nula/ vacía |
| 400 | `"Placa inválida. Usa formato tipo ABC-123 o ABC123 (4 a 12 caracteres)."` | regex/longitud |
| 400 | `"Debes indicar una plaza válida"` | `espacioId` nulo o ≤ 0 |
| 409 | `"Ya existe un ticket abierto para la placa <PLACA> (ticket <CODIGO>). Ciérralo antes de abrir otro."` | ticket ABIERTO con esa placa |
| 409 | `"La plaza ya tiene un ticket abierto (<CODIGO> · <PLACA>). Elige otra plaza libre o cierra ese ticket."` | ticket ABIERTO en esa plaza |
| 409 | `"La plaza ID <id> ya está ocupada. Elige una plaza LIBRE en la pestaña Plazas."` | ms-espacios devolvió 409 |
| 404 | `"No existe la plaza ID <id>. Revisa el ID en Plazas."` | ms-espacios devolvió 404 |
| 503 | `"ms-espacios no disponible (Resilience/Feign). ¿Está caído el servicio? Detalle: <body>"` | ms-espacios 5xx o error de conexión (status < 0) |
| otros | `<body>` de ms-espacios tal cual | otros 4xx (se usa `max(status,400)`) |
| 401/403 | — | — |

- **Nota de diseño importante:** este método **NO** tiene `@CircuitBreaker` ni `@Retry` (solo `ocupacionRemota` los tiene). Un fallo de espacios aquí no genera fallback: genera 409/404/503.

#### 1.3.11 `POST /api/tickets/{id}/cerrar` — Cerrar y cobrar ticket ⭐
- **Auth:** `hasAnyRole('CLIENTE','OPERADOR','ADMIN')` + `asegurarAcceso` (CLIENTE solo el suyo).
- **Path:** `id`.
- **Header requerido:** `Authorization`.
- **Cuerpo:** no.
- **Flujo:** obtiene y autoriza → si ya `CERRADO` → `409` → intenta `POST /api/espacios/{espacioId}/liberar` → calcula el cobro → guarda.
- **Tolerancia a fallos al liberar:**
  - `FeignException` con status `<0` (error de conexión/timeout), `502`, `503`, `504` o `>=500` → **no falla el cierre**; se marca `liberacionPendiente = true`.
  - `404` → se ignora (la plaza no existe, nada que liberar); `liberacionPendiente` queda `false`.
  - Cualquier otro status (p. ej. `401`, `403`, `500`… salvo los anteriores) → se re-lanza mapeado (`mapEspaciosError`).
- **Cálculo del cobro:** ver §3.2.
- **Respuesta 200:** `Ticket` con `salida = now()`, `estado: "CERRADO"`, `montoTotal` calculado, `liberacionPendiente` según lo anterior.
- **Errores:**
  - `404` `"Ticket no encontrado"`.
  - `403` `"Solo puedes ver o cerrar tus propios tickets"`.
  - `409` `"El ticket ya está cerrado"`.
  - Mapeos de espacios (409/404/503) iguales que en `abrir`.
  - `401`.

---

### 1.4 ms-ia — puerto 8084 — base `/api/ia`

`@CrossOrigin` abierto. `SecurityFilterChain`: **`/api/ia/salud` permitAll**; todo el resto `authenticated()`.
`server.error.include-message: always`.
**Sin base de datos.** Sin actuator declarado (no hay `management` en su `application.yml`).

#### 1.4.1 `POST /api/ia/recomendar` — Recomendar plaza (no ocupa)
- **Auth:** `hasAnyRole('CLIENTE','ADMIN')` → **OPERADOR recibe 403.**
- **Header requerido:** `Authorization`.
- **Cuerpo (`RecomendarRequest`):**

| Campo | Tipo | Obligatorio |
|---|---|---|
| `tipoVehiculo` | string | no (si nulo → `"AUTO"`; se pasa a MAYÚSCULAS) |
| `preferencia` | string | no (si nulo/blanco → `"sin preferencia"` en el prompt) |

- **Respuesta 200 (`RecomendacionResponse`):**

| Campo | Tipo | Descripción |
|---|---|---|
| `zonaRecomendada` | string | zona de la plaza elegida |
| `espacioId` | number int64 | id de la plaza |
| `espacioCodigo` | string | código de la plaza |
| `tarifaHora` | number double | tarifa de la plaza |
| `nivel` | number int32 | nivel de la plaza |
| `motivo` | string | `"Asignación por Ollama (<modelo>): plaza <CODIGO>"` o `"Se usó asignación automática local (Ollama no respondió bien): <msg>"` |
| `explicacionIa` | string | texto `motivo` devuelto por el LLM, o frase heurística |
| `proveedorIa` | string | `"ollama/<modelo>"` o `"heuristica-local"` |
| `alternativas` | array[string] | hasta 3 elementos `"<CODIGO> (<ZONA>)"` excluyendo la elegida |

- **Errores:**
  - `503` `"No se pudo consultar plazas libres. Verifica que el servicio de espacios esté activo."`
  - `404` `"No hay plazas LIBRES para vehículos tipo <TIPO>. Cierra algún ticket abierto o elige otro tipo."`
  - `401`, `403` (OPERADOR).

#### 1.4.2 `POST /api/ia/asignar` — Asignar plaza + abrir ticket
- **Auth:** `hasAnyRole('CLIENTE','ADMIN')`.
- **Header requerido:** `Authorization`.
- **Cuerpo (`AsignarRequest`):**

| Campo | Tipo | Obligatorio | Validación |
|---|---|---|---|
| `placa` | string | **sí** | Si nula/blanca → `400 "Debes indicar la placa del vehículo (ej. ABC-123)."`; se normaliza con `trim().toUpperCase()` (**no** se quitan espacios internos ni se valida el regex de placa aquí; la validación real ocurre en ms-tickets al abrir) |
| `tipoVehiculo` | string | no | default `AUTO`, a mayúsculas |
| `preferencia` | string | no | — |

- **Flujo:** selección de plaza (§6) → `POST /api/tickets/abrir` con `{placa, espacioId}` y el token del usuario.
- **Respuesta 200 (`AsignacionResponse`):**

| Campo | Tipo | Descripción |
|---|---|---|
| `espacioId` | number int64 | plaza elegida |
| `espacioCodigo` | string | código |
| `zona` | string | zona |
| `tarifaHora` | number double | tarifa |
| `nivel` | number int32 | nivel |
| `placa` | string | `placa` del ticket devuelto por ms-tickets |
| `ticketId` | number int64 | id del ticket creado |
| `ticketCodigo` | string | código del ticket |
| `motivo` | string | igual semántica que en `/recomendar` |
| `explicacionIa` | string | — |
| `proveedorIa` | string | `ollama/<modelo>` o `heuristica-local` |
| `asignado` | boolean | `true` |

- **Errores (mapeo de `FeignException` de ms-tickets):**

| HTTP | Mensaje |
|---|---|
| 409 (body contiene `"placa"` o `"ticket abierto"`) | `"La placa <PLACA> ya tiene un ticket ABIERTO. Ve a Tickets y pulsa «Cerrar / cobrar» antes de asignar otra plaza."` |
| 409 (otro) | `"No se pudo asignar la plaza <CODIGO> porque está ocupada o en conflicto. Elige otra preferencia o cierra tickets abiertos."` |
| 404 | `"No se encontró la plaza <CODIGO> para crear el ticket."` |
| ≥500 o <0 | `"El servicio de tickets no responde. Espera unos segundos e intenta de nuevo."` |
| otros 4xx | el `message` extraído del JSON del cuerpo, o `"No se pudo crear el ticket para <PLACA>."` |

- **Además:** `400` por placa faltante; `404`/`503` de la fase de selección de plazas (igual que 1.4.1); `502` (catch genérico):
  `"Ollama eligió la plaza <CODIGO>, pero no se pudo crear el ticket. Intenta de nuevo o abre el ticket manualmente en Tickets. Detalle: <ex.getMessage()>"`.

#### 1.4.3 `POST /api/ia/reporte-tickets` — Reporte operativo con resumen IA
- **Auth:** `hasRole('ADMIN')`.
- **Header requerido:** `Authorization`.
- **Cuerpo (`ReporteRequest`, `required = false` → puede ir vacío o sin body):**

| Campo | Tipo | Formato | Default |
|---|---|---|---|
| `fechaDesde` | string (LocalDate) | `yyyy-MM-dd` | `LocalDate.now()` |
| `fechaHasta` | string (LocalDate) | `yyyy-MM-dd` | `LocalDate.now()` |

- **Validación:** si `hasta < desde` → `400` `"La fecha hasta no puede ser anterior a la fecha desde"`.
- **Respuesta 200 (`ReporteTicketsResponse`):**

| Campo | Tipo | Descripción |
|---|---|---|
| `totalTickets` | number | tickets con `entrada` en `[desde 00:00:00, hasta 23:59:59.999999999]` |
| `abiertos` | number | de esos, estado ABIERTO |
| `cerrados` | number | de esos, estado CERRADO |
| `recaudado` | number | suma de `montoTotal` de los CERRADOS del periodo, redondeada a 2 decimales |
| `porCobrarEstimado` | number | suma del estimado de **todos** los ABIERTO actuales (no solo del periodo) |
| `fechaDesde` | string | `yyyy-MM-dd` |
| `fechaHasta` | string | `yyyy-MM-dd` |
| `plazasTotal` | number | de `ocupacion.total` |
| `plazasOcupadas` | number | de `ocupacion.ocupados` |
| `plazasLibres` | number | de `ocupacion.libres` |
| `plazasPorcentaje` | number | de `ocupacion.porcentajeGlobal` |
| `ticketsAbiertosAhora` | number | nº de tickets ABIERTO en el sistema |
| `cuadranConPlazas` | boolean | `plazasOcupadas == distinct(espacioId de ABIERTOs)` **y** `plazasOcupadas == sync.plazasOcupadasTrasSync` |
| `resumenIa` | string | narrativa de Ollama (máx. 4 frases) o texto heurístico |
| `proveedorIa` | string | `ollama/<modelo>` o `heuristica-local` |
| `hallazgos` | array[string] | 6–7 frases generadas localmente (ver §6.4) |
| `metricas` | object | ver abajo |

- **`metricas` (siempre presente):** `fechaDesde`, `fechaHasta`, `periodo`, `plazasTotal`, `plazasOcupadas`, `plazasLibres`, `plazasPorcentaje`, `ticketsAbiertosAhora`, `cuadranConPlazas`, `totalTickets`, `abiertosPeriodo`, `cerrados`, `recaudado`, `porCobrarEstimado`, `porZona` (map zona→nº de tickets del periodo), `plazasOcupadasDetalle` (array de `{plaza, placa, zona, cliente, ticket, porCobrar}`), `ocupacion` (crudo de espacios), `sync` (crudo de `reconciliar-plazas`), `generadoEn` (`yyyy-MM-dd HH:mm:ss`).
- **Efecto secundario importante:** antes de calcular, invoca `POST /api/tickets/reconciliar-plazas` (con el token del ADMIN). Si falla, se ignora silenciosamente.
- **Errores:** `400` (fechas), `503` `"No se pudieron leer los tickets para el reporte."`, `401`, `403`.

#### 1.4.4 `GET /api/ia/ollama` — Estado del LLM
- **Auth:** JWT válido (cualquier rol; **no** hay `@PreAuthorize`, pero el filtro exige autenticación).
- **Comprobación:** `GET <baseUrl>/api/tags` con timeout **5 s**; disponible si `200 ≤ status < 300`.
- **Respuesta 200:**

| Campo | Tipo | Valor |
|---|---|---|
| `disponible` | boolean | — |
| `baseUrl` | string | p.ej. `http://host.docker.internal:11434` |
| `modelo` | string | p.ej. `llama3.2:3b` |
| `mensaje` | string | `"Ollama listo para asignar plazas"` \| `"Ollama no responde. Ejecuta: docker exec ollama ollama pull <modelo>"` |

- **Errores:** `401`.

#### 1.4.5 `GET /api/ia/salud` — Healthcheck público
- **Auth:** **pública** (`permitAll`).
- **Respuesta 200:** `{ "status": "ms-ia OK", "servicio": "Urban Park AI + Ollama" }` (record `MapSalud`).

---

### 1.5 ms-auth — puerto 8085 — bases `/api/auth`, `/api/admin/users`, `/api/perfil`

`SecurityFilterChain`:
- `POST /api/auth/register` → **permitAll**
- `/api/admin/**` → `hasRole("ADMIN")`
- `/api/perfil/**` → `authenticated()`
- `/actuator/**` → permitAll
- resto → `authenticated()`

**Sin base de datos propia**: toda la información vive en Keycloak. `server.error.include-message` **no** configurado (default).

#### 1.5.1 `POST /api/auth/register` — Registro público
- **Auth:** **ninguna**.
- **Cuerpo (`RegisterRequestDto`, record con Bean Validation):**

| Campo | Tipo | Validaciones |
|---|---|---|
| `username` | string | `@NotBlank` + `@Size(min=4, max=50)` |
| `email` | string | `@NotBlank` + `@Email` |
| `firstName` | string | `@NotBlank` |
| `lastName` | string | `@NotBlank` |
| `password` | string | `@NotBlank` + `@Size(min=6, max=100)` |
| `role` | string | `@NotBlank` + `@Pattern(regexp="CLIENTE\|OPERADOR\|ADMIN")` |

- **Flujo:** crea el usuario en Keycloak (`enabled=true`, `emailVerified=true`, credencial `password` con `temporary=false`) → `replaceRealmRoles(userId, role)`.
- **Respuesta 201 (`201 Created`):**

```json
{ "id": "<keycloak-user-id>", "username": "<username>", "role": "<rol>",
  "message": "Usuario registrado como <rol>" }
```

- **Errores:**
  - `400` — Bean Validation (`username`/`password`/`role`/`email` inválidos). Cuerpo estándar de Spring (`errors[]` de field errors, porque `include-binding-errors` no está activado → el detalle puede no aparecer).
  - `400` `"Rol no permitido: <role>"` — si el rol no está en `{CLIENTE, OPERADOR, ADMIN}` (defensa redundante al `@Pattern`).
  - `400` `"Rol no existe: <role>"` — Keycloak devuelve 404 al buscar el rol.
  - `409` `"El usuario <username> ya existe"` — Keycloak devuelve 409.
  - `502` `"No se pudo obtener token de servicio Keycloak"` / `"Keycloak Admin token falló: <status>"` / `"Error creando usuario: <body>"`.
  - `404` `"Usuario no encontrado: <username>"`.
- **Nota:** el registro público permite crear un **ADMIN**; no hay protección adicional.

#### 1.5.2 `GET /api/admin/users` — Listar usuarios
- **Auth:** `hasRole('ADMIN')` (a nivel de clase `@PreAuthorize` **y** en el filtro).
- **Respuesta 200:** `List<UserResponseDto>` — se piden a Keycloak `?max=200` y **se excluyen** los usuarios cuyo `username` empieza por `"service-account-"`.

`UserResponseDto`:

| Campo | Tipo |
|---|---|
| `id` | string (UUID Keycloak) |
| `username` | string |
| `email` | string (`""` si nulo) |
| `firstName` | string |
| `lastName` | string |
| `enabled` | boolean |
| `roles` | array[string] — **solo** los de `{CLIENTE, OPERADOR, ADMIN}`, ordenados alfabéticamente |

- **Errores:** `401`, `403`, `502`.

#### 1.5.3 `GET /api/admin/users/{id}` — Obtener usuario
- **Auth:** ADMIN.
- **Path:** `id` (string UUID).
- **Respuesta 200:** `UserResponseDto`.
- **Errores:** `404` `"Usuario no encontrado"`; `401`, `403`, `502`.

#### 1.5.4 `POST /api/admin/users` — Crear usuario (ADMIN)
- **Auth:** ADMIN.
- **Cuerpo:** `CreateUserDto` (idéntico a `RegisterRequestDto`).
- **Respuesta 201:** `UserResponseDto` del usuario recién creado.
- **Errores:** iguales a 1.5.1.

#### 1.5.5 `PUT /api/admin/users/{id}` — Actualizar usuario
- **Auth:** ADMIN.
- **Cuerpo (`UpdateUserDto`):**

| Campo | Tipo | Validaciones |
|---|---|---|
| `email` | string | `@NotBlank @Email` |
| `firstName` | string | `@NotBlank` |
| `lastName` | string | `@NotBlank` |
| `enabled` | boolean | opcional; si `null` se conserva el valor actual |

- **Respuesta 200:** `UserResponseDto`.
- **Errores:** `400` (validación); `404` `"Usuario no encontrado"`; `409` `"Email ya en uso"`; `502` `"Error actualizando usuario: <body>"`; `401`, `403`.
- **Nota:** el payload enviado a Keycloak **conserva el `username` actual** (no se puede cambiar; `editUsernameAllowed: false` en el realm).

#### 1.5.6 `PUT /api/admin/users/{id}/roles` — Reemplazar rol
- **Auth:** ADMIN.
- **Cuerpo (`UpdateRolesDto`):** `{ "role": "CLIENTE|OPERADOR|ADMIN" }` (`@NotBlank @Pattern`).
- **Lógica `replaceRealmRoles`:** elimina **todos** los roles de la app que tenga el usuario y asigna el nuevo. **Si el rol es `ADMIN`, asigna además `OPERADOR`.**
- **Respuesta 200:** `UserResponseDto` actualizado (roles ya ordenados).
- **Errores:** `400` `"Rol no permitido: <role>"`, `400` `"Rol no existe: <role>"`; `404`; `502`; `401`, `403`.

#### 1.5.7 `PUT /api/admin/users/{id}/password` — Reset de contraseña
- **Auth:** ADMIN.
- **Cuerpo (`ResetPasswordDto`):** `{ "password": "<min 6, max 100>" }`.
- **Respuesta 200:** `{ "message": "Contraseña actualizada" }`.
- **Errores:** `400` (validación); `404` (usuario no encontrado, vía `getUserIdByUsername` no aplica aquí — se llama directamente a reset-password); `502`; `401`, `403`.

#### 1.5.8 `GET /api/perfil/me` — Mi perfil
- **Auth:** autenticado (cualquier rol).
- **Identificación:** `jwt.preferred_username` → búsqueda exacta en Keycloak.
- **Respuesta 200:** `UserResponseDto`.
- **Errores:** `404` `"Usuario no encontrado: <username>"`; `401`; `502`.

#### 1.5.9 `PUT /api/perfil/me` — Actualizar mi perfil
- **Auth:** autenticado.
- **Cuerpo (`UpdatePerfilDto`):**

| Campo | Tipo | Validaciones |
|---|---|---|
| `email` | string | `@NotBlank @Email` |
| `firstName` | string | `@NotBlank` |
| `lastName` | string | `@NotBlank` |

- **Lógica:** obtiene el usuario actual, actualiza con `enabled` actual (no modificable por el propio usuario).
- **Respuesta 200:** `UserResponseDto`.
- **Errores:** `400`, `404`, `409` `"Email ya en uso"`, `502`, `401`.

#### 1.5.10 `PUT /api/perfil/me/password` — Cambiar mi contraseña
- **Auth:** autenticado.
- **Cuerpo (`ChangePasswordDto`):**

| Campo | Tipo | Validaciones |
|---|---|---|
| `currentPassword` | string | `@NotBlank` |
| `newPassword` | string | `@NotBlank` + `@Size(min=6, max=100)` |

- **Lógica:** verifica la contraseña actual mediante **ROPC** (`grant_type=password`, `client_id=urbanpark-web`) contra Keycloak; si falla → `400 "Contraseña actual incorrecta"`. Si funciona, obtiene token de servicio y hace `reset-password`.
- **Respuesta 200:** `{ "message": "Contraseña cambiada" }`.
- **Errores:** `400` (validación o contraseña incorrecta), `404` `"Usuario no encontrado: <username>"`, `502`, `401`.

#### 1.5.11 Endpoints de Keycloak consumidos directamente por el frontend (contrato implícito)

| Método | Ruta | Uso |
|---|---|---|
| `POST` | `/realms/urbanpark/protocol/openid-connect/token` | `grant_type=password` con `client_id=urbanpark-web`, `username`, `password`; también `grant_type=refresh_token` con `refresh_token` |
| `GET` | `/realms/urbanpark/protocol/openid-connect/certs` | JWKS (consumido por los 5 microservicios) |

La respuesta esperada de `token` incluye `access_token`, `refresh_token`, `expires_in` (el frontend refresca 15 s antes de expirar).

---

### 1.6 Resumen tabular de las 41 operaciones

| # | Servicio | Método | Ruta | Autorización |
|---|---|---|---|---|
| 1 | espacios | GET | `/api/espacios` | autenticado |
| 2 | espacios | GET | `/api/espacios/disponibles?tipo=&zona=` | autenticado |
| 3 | espacios | GET | `/api/espacios/ocupacion` | autenticado |
| 4 | espacios | GET | `/api/espacios/{id}` | autenticado |
| 5 | espacios | POST | `/api/espacios` | OPERADOR, ADMIN |
| 6 | espacios | PUT | `/api/espacios/{id}` | OPERADOR, ADMIN |
| 7 | espacios | POST | `/api/espacios/{id}/ocupar` | CLIENTE, OPERADOR, ADMIN |
| 8 | espacios | POST | `/api/espacios/{id}/liberar` | CLIENTE, OPERADOR, ADMIN |
| 9 | vehiculos | GET | `/api/vehiculos` | CLIENTE, OPERADOR, ADMIN (filtrado) |
| 10 | vehiculos | GET | `/api/vehiculos/{id}` | CLIENTE, OPERADOR, ADMIN (propiedad) |
| 11 | vehiculos | GET | `/api/vehiculos/placa/{placa}` | CLIENTE, OPERADOR, ADMIN (propiedad) |
| 12 | vehiculos | POST | `/api/vehiculos` | CLIENTE, OPERADOR, ADMIN |
| 13 | vehiculos | PUT | `/api/vehiculos/{id}` | OPERADOR, ADMIN |
| 14 | vehiculos | DELETE | `/api/vehiculos/{id}` | OPERADOR, ADMIN |
| 15 | tickets | GET | `/api/tickets` | CLIENTE, OPERADOR, ADMIN (filtrado) |
| 16 | tickets | GET | `/api/tickets/abiertos` | CLIENTE, OPERADOR, ADMIN (filtrado) |
| 17 | tickets | GET | `/api/tickets/{id}` | CLIENTE, OPERADOR, ADMIN (propiedad) |
| 18 | tickets | GET | `/api/tickets/ocupacion` | **ADMIN** |
| 19 | tickets | GET | `/api/tickets/demo/espacios/status` | **ADMIN** |
| 20 | tickets | POST | `/api/tickets/demo/espacios/stop` | **ADMIN** |
| 21 | tickets | POST | `/api/tickets/demo/espacios/start` | **ADMIN** |
| 22 | tickets | POST | `/api/tickets/reconciliar-plazas` | **ADMIN** |
| 23 | tickets | POST | `/api/tickets/sincronizar-liberaciones` | CLIENTE, OPERADOR, ADMIN |
| 24 | tickets | POST | `/api/tickets/abrir` | CLIENTE, OPERADOR, ADMIN |
| 25 | tickets | POST | `/api/tickets/{id}/cerrar` | CLIENTE, OPERADOR, ADMIN (propiedad) |
| 26 | ia | POST | `/api/ia/recomendar` | **CLIENTE, ADMIN** |
| 27 | ia | POST | `/api/ia/asignar` | **CLIENTE, ADMIN** |
| 28 | ia | POST | `/api/ia/reporte-tickets` | **ADMIN** |
| 29 | ia | GET | `/api/ia/ollama` | autenticado |
| 30 | ia | GET | `/api/ia/salud` | **público** |
| 31 | auth | POST | `/api/auth/register` | **público** |
| 32 | auth | GET | `/api/admin/users` | ADMIN |
| 33 | auth | GET | `/api/admin/users/{id}` | ADMIN |
| 34 | auth | POST | `/api/admin/users` | ADMIN |
| 35 | auth | PUT | `/api/admin/users/{id}` | ADMIN |
| 36 | auth | PUT | `/api/admin/users/{id}/roles` | ADMIN |
| 37 | auth | PUT | `/api/admin/users/{id}/password` | ADMIN |
| 38 | auth | GET | `/api/perfil/me` | autenticado |
| 39 | auth | PUT | `/api/perfil/me` | autenticado |
| 40 | auth | PUT | `/api/perfil/me/password` | autenticado |
| 41 | keycloak | POST | `/realms/urbanpark/protocol/openid-connect/token` | público |

---

## 2. MODELO DE DATOS

**Convención de nombres:** el código Java usa camelCase; Hibernate aplica por defecto `CamelCaseToUnderscoresNamingStrategy` (`SpringPhysicalNamingStrategy`) → los nombres físicos de columnas son **snake_case**. Las tablas se nombran explícitamente con `@Table`.

**Tipos:** se indica el tipo PostgreSQL real (dialecto activo) y el equivalente MySQL 8 para la reimplementación. `ddl-auto: update` significa que **Hibernate crea/actualiza las tablas automáticamente**; no hay scripts SQL de migración en el repositorio (no existe carpeta `db/migration` ni `schema.sql`/`data.sql`). Los datos semilla se insertan por código en `@PostConstruct`.

### 2.1 `db_espacios` (ms-espacios) — PostgreSQL, host local por defecto `localhost:5433`

#### Tabla `espacios`

| Columna Java | Columna física | PostgreSQL | MySQL 8 | Nulo | Restricciones |
|---|---|---|---|---|---|
| `id` | `id` | `BIGSERIAL` / `BIGINT` | `BIGINT AUTO_INCREMENT` | NO | **PK**, `GenerationType.IDENTITY` |
| `codigo` | `codigo` | `VARCHAR(255)` | `VARCHAR(255)` | NO | **UNIQUE** |
| `zona` | `zona` | `VARCHAR(255)` | `VARCHAR(255)` | NO | — |
| `tipoVehiculo` | `tipo_vehiculo` | `VARCHAR(255)` | `VARCHAR(255)` | NO | Valores esperados: `AUTO`, `MOTO`, `CAMIONETA`, `DISCAPACIDAD` (no hay CHECK en BD) |
| `ocupado` | `ocupado` | `BOOLEAN` | `TINYINT(1)` / `BOOLEAN` | NO | default Java `false`; en BD sin `DEFAULT` explícito |
| `tarifaHora` | `tarifa_hora` | `DOUBLE PRECISION` | `DOUBLE` | NO | — |
| `nivel` | `nivel` | `INTEGER` | `INT` | SÍ | Puede ser negativo (sótano = -1) |
| `descripcion` | `descripcion` | `VARCHAR(255)` | `VARCHAR(255)` | SÍ | — |

**Claves foráneas:** ninguna. **Relaciones:** ninguna (tabla plana; la "zona" es un string, no una entidad).
**Índices:** PK en `id`; índice único en `codigo` (por `unique=true`).

#### Datos semilla (seed) — `EspacioService.seed()`, `@PostConstruct`

Se ejecuta al arrancar. Inserta **solo las plazas cuyo `codigo` no exista** (`findByCodigo(...).isEmpty()`), por lo que es idempotente entre reinicios. **20 plazas**, siempre con `ocupado = false`:

| codigo | zona | tipoVehiculo | tarifaHora | nivel | descripcion |
|---|---|---|---|---|---|
| A-01 | Zona A - Centro | AUTO | 3.5 | 1 | Plaza techada cerca de ascensor |
| A-02 | Zona A - Centro | AUTO | 3.5 | 1 | Plaza estándar |
| A-03 | Zona A - Centro | DISCAPACIDAD | 2.0 | 1 | Plaza preferencial |
| A-04 | Zona A - Centro | AUTO | 3.5 | 1 | Plaza cerca de salida |
| A-05 | Zona A - Centro | CAMIONETA | 4.0 | 1 | Plaza amplia planta baja |
| B-01 | Zona B - Norte | AUTO | 2.8 | 2 | Vista exterior |
| B-02 | Zona B - Norte | CAMIONETA | 4.0 | 2 | Plaza amplia |
| B-03 | Zona B - Norte | AUTO | 2.8 | 2 | Plaza estándar N2 |
| B-04 | Zona B - Norte | DISCAPACIDAD | 2.0 | 2 | Plaza preferencial N2 |
| B-05 | Zona B - Norte | MOTO | 1.5 | 2 | Motos N2 |
| C-01 | Zona C - Sur | MOTO | 1.5 | -1 | Motos sótano |
| C-02 | Zona C - Sur | MOTO | 1.5 | -1 | Motos sótano |
| C-03 | Zona C - Sur | AUTO | 2.5 | -1 | Plaza sótano |
| C-04 | Zona C - Sur | CAMIONETA | 3.8 | -1 | Camionetas sótano |
| C-05 | Zona C - Sur | AUTO | 2.5 | -1 | Plaza sótano |
| D-01 | Zona D - Este | AUTO | 3.0 | 1 | Plaza zona este |
| D-02 | Zona D - Este | AUTO | 3.0 | 1 | Plaza zona este |
| D-03 | Zona D - Este | MOTO | 1.5 | 1 | Motos este |
| D-04 | Zona D - Este | CAMIONETA | 4.2 | 1 | Camionetas este |
| D-05 | Zona D - Este | DISCAPACIDAD | 2.0 | 1 | Plaza preferencial este |

**Tarifas por tipo (derivadas del seed):** AUTO 2.5–3.5; MOTO 1.5; CAMIONETA 3.8–4.2; DISCAPACIDAD 2.0.
**Zonas (4):** "Zona A - Centro", "Zona B - Norte", "Zona C - Sur", "Zona D - Este".
**Distribución por tipo:** AUTO 9, MOTO 4, CAMIONETA 4, DISCAPACIDAD 3.
**Total de plazas inicial: 20** (todas libres).

> ⚠️ No hay tabla de tarifas separada: la tarifa es un atributo de cada plaza (`tarifaHora`) y se copia al ticket al abrirlo.

### 2.2 `db_vehiculos` (ms-vehiculos) — PostgreSQL, host local por defecto `localhost:5434`

#### Tabla `vehiculos`

| Columna Java | Columna física | PostgreSQL | MySQL 8 | Nulo | Restricciones |
|---|---|---|---|---|---|
| `id` | `id` | `BIGSERIAL` / `BIGINT` | `BIGINT AUTO_INCREMENT` | NO | **PK**, IDENTITY |
| `placa` | `placa` | `VARCHAR(255)` | `VARCHAR(255)` | NO | **UNIQUE** |
| `tipo` | `tipo` | `VARCHAR(255)` | `VARCHAR(255)` | NO | `AUTO`, `MOTO`, `CAMIONETA` (sin CHECK) |
| `marca` | `marca` | `VARCHAR(255)` | `VARCHAR(255)` | SÍ | — |
| `color` | `color` | `VARCHAR(255)` | `VARCHAR(255)` | SÍ | — |
| `propietario` | `propietario` | `VARCHAR(255)` | `VARCHAR(255)` | NO | — |
| `propietarioEmail` | `propietario_email` | `VARCHAR(255)` | `VARCHAR(255)` | SÍ | — |
| `propietarioUsername` | `propietario_username` | `VARCHAR(255)` | `VARCHAR(255)` | SÍ | Se compara con el claim `preferred_username` |

**Claves foráneas:** ninguna. **Índices:** PK `id`; único `placa`.

#### Datos semilla — `VehiculoService.seed()`, `@PostConstruct`

1. **Si la tabla está vacía** (`count() == 0`), inserta:

| placa | tipo | marca | color | propietario | propietarioEmail | propietarioUsername |
|---|---|---|---|---|---|---|
| `ABC-123` | AUTO | Toyota | Gris | Ana Cliente | cliente1@urbanpark.local | cliente1 |
| `XYZ-789` | MOTO | Yamaha | Rojo | Luis Operador | operador1@urbanpark.local | operador1 |

2. **Backfill de propietarios** para registros previos sin `propietarioUsername`:
   - email empieza por `cliente1` → `cliente1`
   - email empieza por `operador1` → `operador1`
   - placa `ABC-123` (case-insensitive) → `cliente1`
   - placa `XYZ-789` (case-insensitive) → `operador1`
3. **`asegurarVehiculo(...)`** — inserta si la placa no existe (comparación `equalsIgnoreCase`):

| placa | tipo | marca | color | propietario | propietarioEmail | propietarioUsername |
|---|---|---|---|---|---|---|
| `DEF-456` | AUTO | Chevrolet | Azul | Pedro Cliente | cliente2@urbanpark.local | cliente2 |
| `GHI-789` | CAMIONETA | Ford | Blanco | María Cliente | cliente3@urbanpark.local | cliente3 |

**Total semilla: 4 vehículos.**

### 2.3 `db_tickets` (ms-tickets) — PostgreSQL, host local por defecto `localhost:5435`

#### Tabla `tickets`

| Columna Java | Columna física | PostgreSQL | MySQL 8 | Nulo | Restricciones |
|---|---|---|---|---|---|
| `id` | `id` | `BIGSERIAL` / `BIGINT` | `BIGINT AUTO_INCREMENT` | NO | **PK**, IDENTITY |
| `codigo` | `codigo` | `VARCHAR(255)` | `VARCHAR(255)` | NO | **UNIQUE** |
| `placa` | `placa` | `VARCHAR(255)` | `VARCHAR(255)` | NO | — (no único: unicidad solo lógica, ver §3.4) |
| `espacioId` | `espacio_id` | `BIGINT` | `BIGINT` | NO | **FK lógica cross-servicio** hacia `db_espacios.espacios.id` (no hay FK física: bases separadas) |
| `espacioCodigo` | `espacio_codigo` | `VARCHAR(255)` | `VARCHAR(255)` | SÍ | Copia desnormalizada de `espacios.codigo` |
| `zona` | `zona` | `VARCHAR(255)` | `VARCHAR(255)` | SÍ | Copia desnormalizada de `espacios.zona` |
| `entrada` | `entrada` | `TIMESTAMP` | `DATETIME(6)` | NO | — |
| `salida` | `salida` | `TIMESTAMP` | `DATETIME(6)` | SÍ | — |
| `estado` | `estado` | `VARCHAR(255)` | `VARCHAR(255)` | NO | Valores: `ABIERTO`, `CERRADO` (sin CHECK, comentado en código) |
| `tarifaHora` | `tarifa_hora` | `DOUBLE PRECISION` | `DOUBLE` | SÍ | Copia de la tarifa de la plaza al abrir |
| `montoTotal` | `monto_total` | `DOUBLE PRECISION` | `DOUBLE` | SÍ | Null mientras ABIERTO; calculado al cerrar |
| `usuario` | `usuario` | `VARCHAR(255)` | `VARCHAR(255)` | SÍ | `preferred_username` del creador |
| `liberacionPendiente` | `liberacion_pendiente` | `BOOLEAN` | `TINYINT(1)` | NO | `@Builder.Default = false` |

**Claves foráneas:** ninguna física (arquitectura de base de datos por servicio). `espacioId` es una referencia lógica.
**Índices:** PK `id`; único `codigo`. **No hay índices** sobre `placa`, `estado`, `espacio_id` ni `usuario` (posible punto de mejora en la reimplementación).
**Sin datos semilla:** `ms-tickets` no inserta nada al arrancar.

#### Consultas derivadas usadas (relevantes para índices/rendimiento en MySQL)

`findByEstado(estado)` · `findByPlacaAndEstado(placa, estado)` · `findByEspacioIdAndEstado(espacioId, estado)` · `findByEspacioIdAndEstadoOrderByEntradaAsc(espacioId, estado)` · `findByPlacaIgnoreCase(placa)` · `findByUsuarioIgnoreCase(usuario)` · `findByUsuarioIgnoreCaseAndEstado(usuario, estado)` · `findByLiberacionPendienteTrue()` · `findByUsuarioIgnoreCaseAndLiberacionPendienteTrue(usuario)` · `countByLiberacionPendienteTrue()`.

#### Consultas derivadas de espacios
`findByCodigo(codigo)` · `findByOcupadoFalse()` · `findByZonaAndOcupadoFalse(zona)` · `findByTipoVehiculoAndOcupadoFalse(tipoVehiculo)` · `countByOcupadoTrue()` · `countByZona(zona)` · `countByZonaAndOcupadoTrue(zona)` (las dos últimas **no se usan**: `ocupacion()` agrupa en memoria).

#### Consultas derivadas de vehículos
`findByPlacaIgnoreCase(placa)` · `findByPropietarioContainingIgnoreCase(propietario)` (**no usada por ningún endpoint**) · `findByPropietarioUsernameIgnoreCase(propietarioUsername)`.

### 2.4 Bases de datos sin esquema propio
- **ms-ia:** sin datasource, sin JPA, sin tablas. Todo es cálculo en memoria contra ms-espacios/ms-tickets.
- **ms-auth:** sin datasource. "Tabla de usuarios" = realm `urbanpark` en Keycloak (`keycloak/urbanpark-realm.json` es el seed, importado con `--import-realm`).

### 2.5 Notas de migración a MySQL (reimplementación)
1. Cambiar el conector/dialecto a **MySQL** (`MySql.EntityFrameworkCore` / `Pomelo.EntityFrameworkCore.MySql`); en este proyecto sería reemplazar `org.postgresql` por `com.mysql:mysql-connector-j` y el dialecto `PostgreSQLDialect` por `MySQLDialect` (solo `ms-espacios` lo declara explícitamente; los otros dos usan autodetección).
2. `ddl-auto: update` → en EF Core equivaldría a `EnsureCreated()`/migraciones automáticas. **Recomendado:** migraciones explícitas para controlar índices.
3. `LOCAL DATETIME` en MySQL no guarda zona horaria; el código Java usa `LocalDateTime` (sin zona). Mantener `DATETIME(6)` conserva la semántica exacta.
4. `Boolean` → `TINYINT(1)`; `bool` de C# mapea correctamente.
5. Añadir índices sobre `placa`, `estado`, `espacio_id`, `usuario` y (parcial) `liberacion_pendiente = 1` para replicar el rendimiento esperado de las consultas de negocio.
6. Las columnas `codigo` son `UNIQUE` en las 3 tablas con datos (`espacios`, `vehiculos`, `tickets`).

---

## 3. REGLAS DE NEGOCIO

### 3.1 Validación de placa
Implementada **dos veces** (duplicada) con la misma lógica en `ms-tickets.TicketService.validarPlaca` y `ms-vehiculos.VehiculoService.crear`:

```
1. Si placa == null || isBlank()  → 400 "La placa es obligatoria"   (ms-vehiculos: "La placa es obligatoria"; ms-tickets: "La placa es obligatoria")
2. p = placa.trim().toUpperCase().replace(" ", "")     // quita TODOS los espacios y pasa a mayúsculas
3. Válida si:  p.matches("^[A-Z0-9]{2,8}(-?[A-Z0-9]{1,4})?$")  Y  4 <= p.length() <= 12
   Si no → 400 "Placa inválida. Usa formato tipo ABC-123 o ABC123 (4 a 12 caracteres)."   (ms-tickets)
          400 "Placa inválida. Usa formato tipo ABC-123"                                  (ms-vehiculos)
```

**Formatos aceptados:** `ABC123` (6), `ABC-123` (7), `AB12` (4), `A1234567` (8), `ABC1234` (7), `ABCD-1` (6)…
**Rechazados:** menos de 4 caracteres (`AB1`), más de 12, con espacios internos (se eliminan primero), con caracteres especiales (`ABC_123`, `A@123`), guion en posición inicial, más de un guion.
**Diferencias sutiles entre servicios:**
- `ms-tickets` compara el **normalizado** contra el regex y comprueba la longitud **después** de normalizar.
- `ms-vehiculos` comprueba `placa.length()` sobre el string **sin normalizar** pero el regex sobre el **normalizado**; con espacios iniciales/finales el resultado puede diferir (p. ej. `" ABC-123 "` tiene longitud 9 sin normalizar y pasa, pero el regex se evalúa sobre `ABC-123`).
- `ms-vehiculos` **no** normaliza antes de `findByPlacaIgnoreCase`, pero sí asigna `v.setPlaca(placa)` normalizado antes de la búsqueda → coherente.
- `ms-ia` **no** aplica esta validación; delega en `ms-tickets` al llamar a `/abrir` (solo hace `trim().toUpperCase()`, sin quitar espacios internos → una placa con espacio interno sería rechazada por ms-tickets con 400).

**La placa es `UNIQUE` en `vehiculos`** (nivel BD) y **`UNIQUE` en `tickets.codigo`**, pero **`tickets.placa` NO es única**: la unicidad de placa se garantiza solo por regla de negocio (no puede haber 2 tickets ABIERTO con la misma placa).

### 3.2 Cálculo del cobro ⭐ (fórmula EXACTA)

En `TicketService.cerrar` (y replicada en `cerrarDuplicadosMismaPlaza`):

```java
long minutos = Math.max(1, Duration.between(entrada, salida).toMinutes());  // mínimo 1 minuto
double horas = Math.ceil(minutos / 60.0);                                   // redondeo HACIA ARRIBA a hora completa
double tarifa = tarifaHora != null ? tarifaHora : 0;
montoTotal = Math.round(horas * tarifa * 100.0) / 100.0;                    // redondeo a 2 decimales (HALF_UP)
```

**Formalmente, en C#:**
```csharp
var minutos = Math.Max(1, (int)(salida - entrada).TotalMinutes);
var horas   = Math.Ceiling(minutos / 60.0);
var tarifa  = tarifaHora ?? 0;
var monto   = Math.Round(horas * tarifa, 2, MidpointRounding.AwayFromZero); // espejo de Math.round
```

**Reglas derivadas:**
- **Fracciones:** cualquier fracción de hora se cobra como **hora completa** (`Math.ceil`). 1 min → 1 h; 60 min → 1 h; 61 min → 2 h; 120 min → 2 h; 121 min → 3 h.
- **Mínimo facturable: 1 hora** siempre (incluso si `entrada == salida`, por el `Math.max(1, ...)`).
- **Tope máximo:** **NO EXISTE**. No hay tarifa plana por día, ni límite de horas, ni tope de importe, ni tarifa nocturna.
- **Descuentos:** **NO EXISTEN**. No hay descuentos, promociones, membresías ni recargos.
- **Tarifa por tipo de vehículo:** no se aplica en el cobro; se usa la `tarifaHora` **de la plaza** copiada al ticket en el momento de abrir. Si la plaza cambia de tarifa después, el ticket abierto **conserva la tarifa original** (comportamiento deseado: precio pactado al entrar).
- **Redondeo monetario:** `Math.round(x * 100) / 100` = redondeo a 2 decimales con `HALF_UP` para positivos. Ej.: horas=3, tarifa=2.8 → 8.4; horas=2, tarifa=3.5 → 7.0.
- **`montoTotal` en tickets ABIERTO:** `null`. El "por cobrar" mostrado en reportes es un **estimado en vivo** calculado igual (§6.4).

### 3.3 Apertura de ticket — reglas y validaciones (orden exacto)
1. `username = jwt.preferred_username` (dueño del ticket).
2. Validar placa (§3.1) → normalizada.
3. `espacioId` no nulo y `> 0`, si no → `400 "Debes indicar una plaza válida"`.
4. `findByPlacaAndEstado(placa, "ABIERTO")` → si existe → `409` con el código del ticket conflictivo.
5. `findByEspacioIdAndEstado(espacioId, "ABIERTO")` → si existe → `409` con código y placa del conflicto.
6. Llamar a `ms-espacios POST /{id}/ocupar` con el token del usuario:
   - éxito → usar `id`, `codigo`, `zona`, `tarifaHora` de la **respuesta**;
   - error → mapear según `mapEspaciosError` (§1.3.10).
7. Crear el ticket: `codigo = "TK-" + 8 primeros chars de UUID en mayúsculas`, `entrada = LocalDateTime.now()`, `estado = "ABIERTO"`, `placa`, `espacioId`, `espacioCodigo`, `zona`, `tarifaHora`, `usuario`, `liberacionPendiente = false`, `salida = null`, `montoTotal = null`.
8. Guardar.

**No se valida** que el `tipo` del vehículo coincida con el `tipoVehiculo` de la plaza. **No se valida** que la placa exista en `ms-vehiculos` (los servicios son independientes). **No se valida** que la plaza pertenezca a una zona permitida.

**Orden de comprobaciones importante:** primero placa, luego plaza. Si ambos conflictos existen, gana el de placa.

### 3.4 Unicidad e invariantes
- **Una placa → como máximo 1 ticket ABIERTO** (regla en código, sin constraint de BD).
- **Una plaza → como máximo 1 ticket ABIERTO** (regla en código, sin constraint de BD).
- **Vehículos:** `placa` única por BD (`UNIQUE`) + verificación en servicio (`409`).
- **Plazas:** `codigo` único por BD (`UNIQUE`).
- **Tickets:** `codigo` único por BD (`UNIQUE`); probabilidad de colisión por truncar el UUID a 8 hex es negligible pero existe.
- ⚠️ **Race condition:** las comprobaciones 4/5 y el `ocupar` no están en una transacción distribuida ni con bloqueo; dos peticiones concurrentes con la misma placa pueden crear 2 tickets. `@Transactional` no se usa en `TicketService`. La "reconciliación" (§3.6) existe precisamente para reparar esta inconsistencia.

### 3.5 Estados posibles

**Ticket (`estado`):** `ABIERTO` → `CERRADO`. Solo dos estados; no hay `PAGADO`, `ANULADO`, `PERDIDO`.
- `ABIERTO`: `entrada` con valor, `salida = null`, `montoTotal = null`.
- `CERRADO`: `salida` con valor, `montoTotal` calculado.
- Flag ortogonal **`liberacionPendiente`**: `true` = el ticket ya se cobró/cerró pero la plaza **no** se pudo marcar libre (ms-espacios caído). Se limpia al sincronizar (§3.7).

**Plaza (`ocupado`):** `false` (LIBRE) ↔ `true` (OCUPADA). No hay estados intermedios (reservada, mantenimiento, bloqueada).

**Roles/estados de usuario (Keycloak):** `enabled` true/false; `emailVerified`; roles `CLIENTE`, `OPERADOR`, `ADMIN`.

### 3.6 Control de aforo / ocupación
- **Aforo = nº de filas en `espacios`** (20 inicialmente). No hay un campo de "capacidad máxima" independiente.
- **Ocupación global %** = `round(ocupados * 100 / total)` (redondeo entero).
- **Ocupación por zona %** = `round(ocupadosZona * 100 / totalZona)`.
- **Disponibilidad:** filtro `ocupado = false`. La asignación IA consulta `/api/espacios/disponibles?tipo=<TIPO>`.
- **Sincronización de la verdad:** la **fuente de verdad de la ocupación es `espacios.ocupado`**, mantenida por ms-tickets al abrir (`ocupar`) y cerrar (`liberar`) tickets. Como puede desincronizarse (caídas, cierres manuales, race conditions), existe el endpoint de reconciliación.

**Algoritmo de `POST /api/tickets/reconciliar-plazas` (exacto):**
1. **Cerrar duplicados:** agrupar todos los tickets ABIERTO por `espacioId`; en cada grupo con >1, ordenar por `entrada` ascendente (los `entrada == null` van al final) y **cerrar todos menos el más antiguo**. A los cerrados se les asigna `salida = now()`, `estado = CERRADO` y `montoTotal` con la fórmula de §3.2 (usando `entrada` del ticket, o `now()` si era nulo). **No se libera la plaza** de los duplicados.
2. Calcular `plazasConTicket` = conjunto de `espacioId` de los ABIERTO restantes.
3. `GET /api/espacios` (lista completa). Si falla → `503 "No se pudieron listar plazas para reconciliar"`.
4. Para **cada** plaza:
   - `debeEstarOcupada = plazasConTicket.contains(plaza.id)`, `estaOcupada = plaza.ocupado`.
   - `debeEstarOcupada && !estaOcupada` → `POST /ocupar`; éxito → `ocupadas++`; `409` → `yaOk++` (ya estaba ocupada de hecho); otro error → se añade a `detalle`.
   - `!debeEstarOcupada && estaOcupada` → `POST /liberar`; éxito → `liberadas++` + `limpiarPendientesDePlaza(id)`; error → `detalle`.
   - Coinciden → `yaOk++`; si `!debeEstarOcupada`, también `limpiarPendientesDePlaza(id)`.
5. Recalcular `abiertos` y `plazasConTicket`.
6. `GET /api/espacios/ocupacion` (si falla → `{}`).
7. Calcular `cuadran = (ocupados == plazasConTicket.size()) && (abiertos.size() == plazasConTicket.size())`.
8. Devolver el mapa de §1.3.8.

**`limpiarPendientesDePlaza(espacioId)`:** recorre `findByLiberacionPendienteTrue()` y pone `liberacionPendiente = false` en los que coincidan con ese `espacioId` (no libera plaza, solo limpia el flag).

### 3.7 Cierre de ticket con ms-espacios caído (patrón de "liberación pendiente")
1. `cerrar(id)`: obtener + autorizar; si `CERRADO` → `409`.
2. Intentar `POST /api/espacios/{espacioId}/liberar`:
   - Éxito → `liberacionPendiente = false`.
   - `404` → la plaza no existe → ignorar, `liberacionPendiente = false`.
   - `status < 0` (conexión/timeout), `502`, `503`, `504`, `>= 500` → **el cierre continúa** con `liberacionPendiente = true`.
   - Otro status → **se aborta** el cierre con error mapeado.
3. **El cobro nunca depende de ms-espacios.**
4. Reintento posterior: `POST /api/tickets/sincronizar-liberaciones` recorre los pendientes y:
   - Si existe un ticket ABIERTO en esa plaza → cancela el pendiente (`sinCambio++`, detalle `"<CODIGO>: hay ticket abierto, se cancela pendiente"`).
   - Si `liberar` funciona → `liberacionPendiente = false`, `liberadas++`, detalle `"<CODIGO> → plaza <CODIGO_PLAZA> disponible"`.
   - Si `404` → `liberacionPendiente = false`, `liberadas++`, detalle `"<CODIGO>: plaza no existe, pendiente limpiado"`.
   - Si ms-espacios sigue caído → `fallidas++`, detalle `"<CODIGO>: ms-espacios aún no disponible"`.
   - Otro error → `fallidas++`, detalle `"<CODIGO>: error <status>"`.

### 3.8 Reglas de propiedad y visibilidad (autorización a nivel de dato)
| Recurso | CLIENTE | OPERADOR | ADMIN |
|---|---|---|---|
| Tickets creados por él | ve/cierra | — | — |
| Tickets en general | no | sí | sí |
| Vehículos propios | ve/crea/edita* | — | — |
| Vehículos en general | no | sí (edita/elimina) | sí |
| Plazas | lee | lee/crea/edita/ocupa/libera | todo |
| Usuarios (Keycloak) | no | no | todo |
| Reportes IA / Resilience / Reconciliación | no | no | sí |
| IA recomendar/asignar | sí | **no** | sí |
| Perfil propio | sí (GET/PUT/cambiar contraseña) | sí | sí |

\* `PUT /api/vehiculos/{id}` exige OPERADOR/ADMIN, por lo que el CLIENTE **no** puede editar ni sus propios vehículos (solo crear y listar). `DELETE` tampoco.

**Regla de visibilidad de tickets:** `usuario` del ticket se compara con `preferred_username` con `equalsIgnoreCase` (tolerante a mayúsculas). Si `ticket.usuario` es `null` (dato legado), un CLIENTE recibe `403`.

### 3.9 Validaciones de entrada — resumen
| Entidad | Campo | Regla |
|---|---|---|
| Ticket | placa | §3.1 |
| Ticket | espacioId | `> 0` y no nulo |
| Vehículo | placa | §3.1 + única |
| Vehículo | propietario | no vacío |
| Vehículo | tipo | default `AUTO`, mayúsculas |
| Usuario | username | 4–50, no blanco |
| Usuario | email | formato email, no blanco |
| Usuario | firstName/lastName | no blanco |
| Usuario | password | 6–100 |
| Usuario | role | exactamente `CLIENTE`\|`OPERADOR`\|`ADMIN` |
| Reporte | fechaHasta | no anterior a fechaDesde |
| IA asignar | placa | no vacía (validación completa delegada a ms-tickets) |
| Plaza (crear/editar) | codigo/zona/tipoVehiculo/tarifaHora | obligatorios por esquema BD |

---

## 4. SEGURIDAD

### 4.1 Roles del realm
Definidos en `keycloak/urbanpark-realm.json` como **realm roles**:

| Rol | Descripción (literal del realm) |
|---|---|
| `CLIENTE` | `"Conductor / cliente del estacionamiento"` |
| `OPERADOR` | `"Operador de plaza"` |
| `ADMIN` | `"Administrador del sistema"` |

### 4.2 Usuarios demo (contraseñas en claro, del realm JSON)

| Usuario | Contraseña | Roles | Email | Nombre |
|---|---|---|---|---|
| `cliente1` | `cliente123` | `CLIENTE` | cliente1@urbanpark.local | Ana Cliente |
| `cliente2` | `cliente123` | `CLIENTE` | cliente2@urbanpark.local | Pedro Cliente |
| `cliente3` | `cliente123` | `CLIENTE` | cliente3@urbanpark.local | María Cliente |
| `operador1` | `operador123` | `OPERADOR` | operador1@urbanpark.local | Luis Operador |
| `admin1` | `admin123` | `ADMIN`, `OPERADOR` | admin1@urbanpark.local | Admin Urban |
| `service-account-urbanpark-backend` | (sin contraseña; service account) | client roles de `realm-management`: `manage-users`, `view-users`, `query-users`, `view-realm` | — | — |

Todas las credenciales tienen `temporary: false`.

**Usuarios administradores de Keycloak:** `admin` / `admin` (variables `KEYCLOAK_ADMIN` / `KEYCLOAK_ADMIN_PASSWORD` del compose).

### 4.3 Clientes de Keycloak

**`urbanpark-web`** (público, para el frontend):
- `publicClient: true`, `directAccessGrantsEnabled: true` (habilita ROPC: `grant_type=password`), `standardFlowEnabled: true`, `implicitFlowEnabled: false`, `fullScopeAllowed: true`.
- `redirectUris`: `["http://localhost/*", "http://localhost:80/*", "*"]`; `webOrigins: ["*"]`.
- `attributes`: `pkce.code.challenge.method = "S256"`.
- `defaultClientScopes`: `web-origins`, `acr`, `roles`, `profile`, `email`.
- `optionalClientScopes`: `address`, `phone`, `offline_access`, `microprofile-jwt`.
- **Sin secret.**

**`urbanpark-backend`** (confidencial, service account para ms-auth):
- `publicClient: false`, **`secret: "urbanpark-secret"`**, `serviceAccountsEnabled: true`, `directAccessGrantsEnabled: true`, `standardFlowEnabled: true`, `fullScopeAllowed: true`.
- Service account con roles de cliente `realm-management`: `manage-users`, `view-users`, `query-users`, `view-realm`.

**Configuración del realm:**
- `realm: "urbanpark"`, `enabled: true`, `sslRequired: "none"`.
- `registrationAllowed: true` (¡el registro público está habilitado en el realm!), `loginWithEmailAllowed: true`, `duplicateEmailsAllowed: false`, `resetPasswordAllowed: true`, `editUsernameAllowed: false`.

### 4.4 Validación del JWT (EXACTA — punto crítico)

Los 5 microservicios usan el **mismo patrón**:

```java
@Bean JwtDecoder jwtDecoder() {
    return NimbusJwtDecoder.withJwkSetUri(jwkSetUri).build();
}
```
con
```yaml
spring.security.oauth2.resourceserver.jwt.jwk-set-uri: ${SPRING_SECURITY_OAUTH2_RESOURCESERVER_JWT_JWK_SET_URI:http://localhost:8080/realms/urbanpark/protocol/openid-connect/certs}
```

| Aspecto | Comportamiento real |
|---|---|
| **Firma** | **SÍ se valida** (RS256 con las claves públicas del JWKS de Keycloak) |
| **`iss` (issuer)** | **NO se valida** — a propósito. Los comentarios del código lo dicen: *"Solo JWKS: evita fallos por issuer (localhost vs localhost:80 vs :8080)"* y *"Valida firma con JWKS; no exige issuer exacto"*. Al construir el decoder solo con `jwkSetUri` (sin `JwtDecoders.fromIssuerLocation`), `NimbusJwtDecoder` **no** aplica ningún `JwtValidators` de issuer por defecto. |
| **`aud` (audience)** | **NO se valida** — no hay `JwtClaimValidator` ni config de audience |
| **`exp` / `nbf`** | **SÍ** — `NimbusJwtDecoder` aplica por defecto `JwtValidators.createDefault()` que valida `exp` y `nbf` |
| **`azp`, `typ`, `scope`** | no se validan |
| **Claim de roles** | `realm_access.roles` (array de strings) |
| **Claim de identidad** | `preferred_username` |

**Conversión de roles a autoridades (idéntica en los 5 servicios):**
```java
Map<String,Object> realmAccess = jwt.getClaim("realm_access");
List<String> roles = (List<String>) realmAccess.get("roles");
// autoridad = "ROLE_" + rol
```
→ `hasRole('ADMIN')` en `@PreAuthorize` busca la autoridad `ROLE_ADMIN`, que proviene de `realm_access.roles` conteniendo `"ADMIN"`.
⚠️ **NO se leen `resource_access` (client roles)** → los client roles no otorgan autoridad de aplicación.

**Configuración común de la cadena de filtros:**
```java
http.csrf(csrf -> csrf.disable())
    .sessionManagement(s -> s.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
    .oauth2ResourceServer(oauth -> oauth.jwt(jwt -> jwt.jwtAuthenticationConverter(jwtAuthConverter())));
```

### 4.5 Matriz endpoint ↔ rol exigido

| Endpoint | Exigencia |
|---|---|
| `POST /api/auth/register` | **público** |
| `GET /api/ia/salud` | **público** |
| `GET /actuator/**` | **público** (en espacios, vehiculos, tickets, auth) |
| `GET /api/espacios*` | `authenticated` |
| `POST/PUT /api/espacios*` | `hasAnyRole('OPERADOR','ADMIN')` (excepto ocupar/liberar) |
| `POST /api/espacios/{id}/ocupar` · `/liberar` | `hasAnyRole('CLIENTE','OPERADOR','ADMIN')` |
| `GET/POST /api/vehiculos` · `GET /{id}` · `GET /placa/{placa}` | `hasAnyRole('CLIENTE','OPERADOR','ADMIN')` |
| `PUT/DELETE /api/vehiculos/{id}` | `hasAnyRole('OPERADOR','ADMIN')` |
| `GET /api/tickets` · `/abiertos` · `/{id}` | `hasAnyRole('CLIENTE','OPERADOR','ADMIN')` |
| `POST /api/tickets/abrir` · `/{id}/cerrar` | `hasAnyRole('CLIENTE','OPERADOR','ADMIN')` |
| `POST /api/tickets/sincronizar-liberaciones` | `hasAnyRole('CLIENTE','OPERADOR','ADMIN')` |
| `GET /api/tickets/ocupacion` | `hasRole('ADMIN')` |
| `POST /api/tickets/reconciliar-plazas` | `hasRole('ADMIN')` |
| `GET/POST /api/tickets/demo/espacios/*` | `hasRole('ADMIN')` |
| `POST /api/ia/recomendar` · `/asignar` | `hasAnyRole('CLIENTE','ADMIN')` |
| `POST /api/ia/reporte-tickets` | `hasRole('ADMIN')` |
| `GET /api/ia/ollama` | `authenticated` (sin `@PreAuthorize`) |
| `/api/admin/users/**` | `hasRole('ADMIN')` (filtro + `@PreAuthorize` de clase) |
| `/api/perfil/**` | `authenticated` |

### 4.6 Propagación del token (service-to-service)
- `ms-tickets` recibe `@RequestHeader("Authorization") String authorization` en `abrir`, `cerrar`, `ocupacion`, `reconciliar-plazas`, `sincronizar-liberaciones` y lo reenvía literal a `ms-espacios` vía Feign (`@RequestHeader("Authorization")`).
- `ms-ia` hace lo mismo hacia `ms-espacios` y `ms-tickets`: usa el **token del usuario llamante**, no un token de servicio. Consecuencia: si un `ADMIN` llama a `/api/ia/reporte-tickets`, todas las llamadas internas viajan con privilegios de ADMIN (lo cual es necesario, porque `reconciliar-plazas` exige ADMIN).
- **No hay `client_credentials` en las llamadas entre microservicios** (excepto ms-auth → Keycloak Admin API).

### 4.7 Obtención de token (frontend / Postman)
```
POST http://localhost:8080/realms/urbanpark/protocol/openid-connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password&client_id=urbanpark-web&username=cliente1&password=cliente123
```
Refresco: `grant_type=refresh_token&client_id=urbanpark-web&refresh_token=<token>` (el frontend refresca 15 s antes del vencimiento).

---

## 5. RESILIENCIA (Resilience4j entre ms-tickets y ms-espacios)

### 5.1 Dónde está aplicado
**Únicamente** en `TicketService.ocupacionRemota(String authorization)` (línea 366-374 de `TicketService.java`), que respalda el endpoint `GET /api/tickets/ocupacion`:

```java
@CircuitBreaker(name = "espaciosClient", fallbackMethod = "ocupacionFallback")
@Retry(name = "espaciosClient")
public Map<String, Object> ocupacionRemota(String authorization) {
    Map<String, Object> remote = espaciosClient.ocupacion(authorization);
    Map<String, Object> data = new HashMap<>(remote);
    data.put("origen", "ms-espacios (live)");
    data.put("resilience", "Circuit Breaker + Retry OK");
    return data;
}
```

**Todos los demás métodos** (`abrir`, `cerrar`, `reconciliarPlazas`, `sincronizarLiberacionesPendientes`) llaman a Feign **sin** Circuit Breaker ni Retry; manejan `FeignException` manualmente.

**Feign `circuitbreaker` desactivado globalmente** (para no aplicar Resilience4j a todo Feign):
```yaml
spring.cloud.openfeign.circuitbreaker.enabled: false
```

### 5.2 Configuración EXACTA de umbrales y tiempos

```yaml
resilience4j:
  circuitbreaker:
    instances:
      espaciosClient:
        slidingWindowSize: 10
        failureRateThreshold: 50
        waitDurationInOpenState: 10s
        permittedNumberOfCallsInHalfOpenState: 3
        automaticTransitionFromOpenToHalfOpenEnabled: true
  retry:
    instances:
      espaciosClient:
        maxAttempts: 3
        waitDuration: 500ms
        enableExponentialBackoff: true
        exponentialBackoffMultiplier: 2
```

**Circuit Breaker `espaciosClient`:**

| Parámetro | Valor | Significado |
|---|---|---|
| `slidingWindowSize` | **10** | ventana de 10 llamadas |
| `slidingWindowType` | *(no definido)* → **`COUNT_BASED`** por defecto | ventana por nº de llamadas |
| `minimumNumberOfCalls` | *(no definido)* → **100** por defecto de Resilience4j | ⚠️ **con ventana de 10 y mínimo 100, el circuito NO puede abrirse por tasa de fallos** en la configuración tal como está: el mínimo de llamadas nunca se alcanza. En la práctica, la apertura solo ocurrirá si se alcanza el mínimo de llamadas por defecto o si se ajusta `minimumNumberOfCalls`. **Punto a corregir en la reimplementación.** |
| `failureRateThreshold` | **50** (%) | abre si ≥50% de fallos |
| `waitDurationInOpenState` | **10 s** | tiempo en OPEN antes de pasar a HALF_OPEN |
| `permittedNumberOfCallsInHalfOpenState` | **3** | llamadas de prueba en HALF_OPEN |
| `automaticTransitionFromOpenToHalfOpenEnabled` | **true** | transición automática OPEN→HALF_OPEN tras 10 s |
| `recordExceptions` | *(no definido)* → todas las excepciones cuentan como fallo | — |
| `ignoreExceptions` | *(no definido)* → ninguna | — |
| `slowCallDurationThreshold` / `slowCallRateThreshold` | *(no definidos)* → **deshabilitados** | no hay detección de lentitud |

**Retry `espaciosClient`:**

| Parámetro | Valor | Significado |
|---|---|---|
| `maxAttempts` | **3** | 1 intento inicial + 2 reintentos |
| `waitDuration` | **500 ms** | espera inicial |
| `enableExponentialBackoff` | **true** | backoff exponencial |
| `exponentialBackoffMultiplier` | **2** | factor |
| `retryExceptions` | *(no definido)* → todas | — |
| `ignoreExceptions` | *(no definido)* → ninguna | — |
| `randomizedWaitFactor` | *(no definido)* | — |

**Secuencia temporal resultante (peor caso):** intento 1 → falla → espera **500 ms** → intento 2 → falla → espera **1000 ms** → intento 3 → falla → se propaga.
**Total de espera entre reintentos: 1.5 s** (+ el tiempo de cada intento HTTP).
**Cada fallo de los 3 intentos cuenta como 1 fallo en el Circuit Breaker** (el CB envuelve al Retry: el orden de aspectos de Resilience4j es Retry externo / CB interno según la configuración por defecto de Spring Boot de Resilience4j — en la práctica, el CB cuenta cada intento fallido).

### 5.3 Fallback

```java
private Map<String, Object> ocupacionFallback(String authorization, Throwable ex) {
    return Map.of(
        "fallback", true,
        "origen", "ms-tickets fallback",
        "mensaje", "Resilience4j activó fallback: " + ex.getMessage(),
        "total", 0,
        "ocupados", 0,
        "libres", 0
    );
}
```

- **Firma obligatoria:** el fallback recibe los **mismos argumentos** del método original (`String authorization`) **más** el `Throwable`. En ASP.NET Core/Polly el equivalente es una `Func<..., Exception, Task<T>>` con `FallbackAsync`.
- **Devuelve siempre HTTP 200** con `fallback: true`; nunca propaga el error al cliente.
- `ex.getMessage()` es el mensaje de la última excepción (p. ej. `Connection refused` o `FeignException$ServiceUnavailable: [503] during [GET] to [http://ms-espacios:8081/api/espacios/ocupacion]`).

### 5.4 Endpoints afectados por la resiliencia
| Endpoint | ¿CB/Retry? | Comportamiento con ms-espacios caído |
|---|---|---|
| `GET /api/tickets/ocupacion` | **SÍ** | 3 intentos con backoff → fallback HTTP 200 `{fallback:true, total:0, ocupados:0, libres:0}` |
| `POST /api/tickets/abrir` | no | `503 "ms-espacios no disponible (Resilience/Feign). ¿Está caído el servicio? Detalle: <body>"` |
| `POST /api/tickets/{id}/cerrar` | no | **cierra y cobra igual**; marca `liberacionPendiente = true` |
| `POST /api/tickets/reconciliar-plazas` | no | `503 "No se pudieron listar plazas para reconciliar"` (o errores por plaza en `detalle`) |
| `POST /api/tickets/sincronizar-liberaciones` | no | incrementa `fallidas`, mensaje `"Aún no se pudo sincronizar (¿ms-espacios sigue caído?)"` |

### 5.5 Observabilidad
```yaml
management.endpoints.web.exposure.include: health,info,circuitbreakers,circuitbreakerevents
```
→ `GET /actuator/health`, `/actuator/info`, `/actuator/circuitbreakers`, `/actuator/circuitbreakerevents` (permitAll).

### 5.6 Demo de resiliencia (endpoints de control Docker)
`DockerDemoService` (ms-tickets) ejecuta el CLI `docker` mediante `ProcessBuilder`, con **timeout de ejecución de 45 s** (si expira, `destroyForcibly()` y `exitCode 124`), `redirectErrorStream(true)`, salida leída en UTF-8, filtro `name=ms-espacios`:
- `statusEspacios()`: `docker ps -q -f name=ms-espacios` (running) y, si no, `docker ps -aq -f name=ms-espacios`.
- `stopEspacios()`: `docker stop <id>`.
- `startEspacios()`: `docker start <id>`.

Requiere el montaje `/var/run/docker.sock:/var/run/docker.sock` en `ms-tickets` y `docker-cli` en la imagen (el `Dockerfile` de ms-tickets hace `apk add --no-cache docker-cli`).

---

## 6. IA — ANÁLISIS DETALLADO DE ms-ia

### 6.1 Visión general
`ms-ia` no tiene base de datos. Es un orquestador que:
1. consulta plazas libres y ocupación a `ms-espacios`;
2. construye un prompt y lo envía a **Ollama** (`POST /api/generate`);
3. parsea la respuesta **a mano con expresiones regulares** (sin librería JSON);
4. si el modelo falla, aplica un **fallback heurístico local**;
5. para `/asignar`, abre el ticket real en `ms-tickets`.

### 6.2 Llamada HTTP a Ollama — formato EXACTO

**Endpoint:** `POST {ollama.base-url}/api/generate` (la barra final de `base-url` se elimina con `trimSlash`).
**Headers:** `Content-Type: application/json`.
**Timeout:** `Duration.ofSeconds(timeoutSeconds)` → `ollama.timeout-seconds`, **default 90**, overridable con `OLLAMA_TIMEOUT`.
**Sin streaming** (`"stream": false`).

**Cuerpo JSON construido por concatenación de strings (`OllamaService.callGenerate`):**

```json
{
  "model": "<ollama.model>",
  "prompt": "<prompt escapado>",
  "format": "json",
  "options": {
    "temperature": 0.3,
    "num_predict": 200
  }
}
```

- `"format": "json"` **solo** cuando `jsonFormat = true` (método `generate(...)`, usado por `/recomendar` y `/asignar`). Para `generateText(...)` (reportes) la línea se **omite** por completo.
- `num_predict`: **200** para `generate` (asignación), **350** para `generateText` (reportes).
- **`temperature`: 0.3** en ambos casos.
- **No se envían** `top_p`, `top_k`, `seed`, `stop`, `system`, `keep_alive`.
- El escapado del prompt (`toJsonString`) reemplaza `\` → `\\`, `"` → `\"`, `\n` → `\n` y elimina `\r`. **No escapa tabuladores ni otros caracteres de control** (posible JSON inválido si el prompt tuviera un `\t`).

**Parámetros del modelo:**
| Parámetro | Config | Default | Override env |
|---|---|---|---|
| Modelo | `ollama.model` | **`llama3.2:3b`** | `OLLAMA_MODEL` |
| Base URL | `ollama.base-url` | `http://localhost:11434` | `OLLAMA_BASE_URL` |
| Timeout | `ollama.timeout-seconds` | **90** | `OLLAMA_TIMEOUT` |

**Verificación de disponibilidad (`GET /api/tags`):** timeout de **5 s**; disponible si `200 ≤ status < 300`.

### 6.3 PROMPT EXACTO de asignación (`/recomendar` y `/asignar`)

Generado en `RecomendacionService.seleccionarConOllama` con un *text block* de Java y `.formatted(tipo, preferencia, zonas, listaPlazas)`. **Texto literal (con los marcadores sustituidos):**

```
Eres el motor de asignación de Urban Park (estacionamiento).
Elige UNA plaza disponible para un vehículo tipo {tipo}.
Preferencia del conductor: {preferencia}
Ocupación por zona (%): {zonas}

Plazas libres (JSON):
[{listaPlazas}]

Responde SOLO un JSON válido con esta forma exacta:
{"codigo":"CODIGO_PLAZA","motivo":"explicacion breve en espanol"}

Reglas:
- codigo debe existir en la lista de plazas libres.
- Prioriza menor ocupacion de zona, luego tarifa y preferencia del conductor.
- No inventes plazas.
```

**Sustituciones:**
- `{tipo}` = `tipoVehiculo != null ? tipoVehiculo.toUpperCase() : "AUTO"`.
- `{preferencia}` = `preferencia` si no es nulo/blanco, si no literal **`sin preferencia`**.
- `{zonas}` = el mapa `ocupacion.zonas` de ms-espacios serializado con `Map.toString()` de Java → formato `{Zona A - Centro={porcentaje=25, total=4, libres=3, ocupados=1}, Zona B - Norte={...}}`. Si `ocupacion` falló, `{}` (y entonces `zonaOcupacion()` devuelve el default 50 en el fallback).
- `{listaPlazas}` = plazas libres unidas por `",\n"`, **una por línea**, cada una con este formato EXACTO:

```
{"id":%d,"codigo":"%s","zona":"%s","tarifa":%.2f,"nivel":%s,"descripcion":"%s"}
```

  donde `tarifa` es `%.2f` (0 si nula), `nivel` es el entero o el literal `null`, y `descripcion` se sanea con `safe()` (comillas dobles → comillas simples, saltos de línea → espacio). Ejemplo real:

```
[{"id":1,"codigo":"A-01","zona":"Zona A - Centro","tarifa":3.50,"nivel":1,"descripcion":"Plaza techada cerca de ascensor"},
{"id":2,"codigo":"A-02","zona":"Zona A - Centro","tarifa":3.50,"nivel":1,"descripcion":"Plaza estándar"}]
```

**Formato de respuesta esperado (impuesto por el prompt + `format: json`):**
```json
{"codigo":"A-02","motivo":"Zona A tiene baja ocupación y tarifa media"}
```
**`motivo` se pide SIN acentos** ("explicacion breve en espanol" — el prompt está escrito sin tildes intencionadamente).

### 6.4 Segundo prompt EXACTO — reporte narrativo (`/reporte-tickets`)

Generado en `ReporteService.generarTextoOllama` con `.formatted(periodo, datos)`, enviado por `generateText` (sin `format: json`, `num_predict: 350`):

```
Eres analista de Urban Park. Escribe un resumen MUY CLARO en español para el administrador.
Periodo: {periodo}.
Reglas:
1) Primero di cuántas plazas están ocupadas/libres AHORA y menciona números de plaza si hay.
2) Luego di cuánto se cobró y cuánto falta por cobrar en el periodo.
3) Cierra con UNA recomendación práctica (máximo 1 frase).
Usa lenguaje simple. Máximo 4 oraciones cortas. Sin markdown, sin listas, sin jerga técnica.

DATOS:
{datos}
```

**`{periodo}`** = `"del día <fecha>"` si `desde == hasta`, si no `"del <desde> al <hasta>"`.
**`{datos}`** = el bloque `resumenDatos` construido con `.formatted(...)`:

```
Resume de forma clara para un gerente de estacionamiento.
No mezcles ocupación actual con tickets del periodo.

AHORA EN EL PARQUEADERO:
- Total plazas: {plazasTotal} | Ocupadas: {plazasOcupadas} | Libres: {plazasLibres} | Ocupación: {plazasPct}%
- Tickets abiertos ahora: {ticketsAbiertosAhora} | ¿Cuadra con plazas? {SÍ|NO}
- Plazas ocupadas (número + placa): {plazasOcupadasTexto}

PERIODO DE TICKETS ({periodo}):
- Tickets del periodo: {tickets.size()} (cerrados: {cerrados}, abiertos en periodo: {abiertosPeriodo})
- Recaudado: ${recaudado} | Por cobrar ahora: ${porCobrar}
- Por zona: {porZona}
```

`plazasOcupadasTexto` = `"ninguna"` o `"A-01 (ABC-123), B-03 (XYZ-789)"`.
`porZona` = `Map.toString()` → `{Zona A - Centro=3, Zona C - Sur=1}`.

### 6.5 Parseo de la respuesta (manual, sin librería JSON)

**Paso 1 — extraer el campo `response` del envelope de Ollama** (`OllamaService.extractResponseField`, parser carácter a carácter):
1. `indexOf("\"response\"")`; si no está → devuelve el **cuerpo completo**.
2. Busca `:` tras la clave, luego la primera `"` → inicio del string.
3. Recorre los caracteres manejando escapes: `\n` → salto de línea, `\t` → tabulador, `\"` → comilla, `\\` → barra; cualquier otro `\X` → `X`.
4. Termina en la siguiente `"` sin escapar.
5. Devuelve `sb.toString().trim()`.

**Paso 2 — extraer el objeto JSON del texto del modelo** (`OllamaService.extractJsonObject`):
- `null` si el texto es nulo.
- Si el texto (trim) **empieza por `{` y termina por `}`** → se devuelve tal cual.
- Si no, aplica el regex **`\{[\s\S]*?\}`** (no-greedy) y devuelve el **primer** objeto encontrado.
- Si no hay coincidencia → `null` → se lanza `IllegalStateException("Ollama no devolvió JSON: " + raw)` → dispara el fallback heurístico.
- ⚠️ El regex no-greedy falla con JSON que contenga objetos anidados con `}` antes del cierre (aquí no ocurre porque el formato pedido es plano).

**Paso 3 — extraer los campos** (`OllamaService.readJsonString`):
```java
Pattern.compile("\"" + Pattern.quote(field) + "\"\\s*:\\s*\"([^\"]*)\"")
```
→ regex `"codigo"\s*:\s*"([^"]*)"` y `"motivo"\s*:\s*"([^"]*)"`.
- **No soporta** valores con comillas escapadas ni secuencias de escape dentro del valor.
- Si `codigo` es `null` o blanco → `IllegalStateException("JSON sin codigo: " + json)` → fallback.

**Paso 4 — validar contra el catálogo real:**
```java
EspacioDto elegido = libres.stream()
    .filter(e -> codigo.equalsIgnoreCase(e.getCodigo()))
    .findFirst()
    .orElseThrow(() -> new IllegalStateException("Ollama eligió codigo inválido '" + codigo + "'"));
```
→ **anti-alucinación:** si el modelo inventa una plaza, se lanza excepción y se aplica el fallback heurístico.

**Paso 5 — construir la explicación:** `motivo` si no es nulo; si no, `"Ollama asignó la plaza <CODIGO> en <ZONA>"`.

**Manejo de errores HTTP de Ollama:** si `statusCode` fuera de `[200,300)` → `IllegalStateException("Ollama HTTP " + status + ": " + body)` → fallback.

### 6.6 Fallback heurístico (cuando el modelo falla)

Se activa ante **cualquier** excepción en el bloque `try` (Ollama inalcanzable, timeout, JSON inválido, `codigo` ausente, `codigo` inexistente) — `catch (Exception ex)`.

**Ordenación determinista (`fallbackHeuristico`):**

```java
libres.stream().sorted(
    Comparator.comparingDouble(e -> zonaOcupacion(zonas, e.getZona()))   // 1º: % ocupación de la zona (ASC)
              .thenComparingDouble(e -> e.getTarifaHora() != null ? e.getTarifaHora() : 99)  // 2º: tarifa (ASC, null=99)
              .thenComparing(e -> preferenciaNivel(e, preferencia))     // 3º: afinidad con la preferencia (ASC)
).findFirst().orElse(libres.get(0));
```

- **`zonaOcupacion(zonas, zona)`:** si `zonas[zona]` es un `Map` con `porcentaje` numérico → ese valor; si no → **50** (neutral).
- **`preferenciaNivel(e, preferencia)`:** normaliza la preferencia a minúsculas; devuelve:
  - si contiene `"cerca"`, `"entrada"` o `"rapido"` → `Math.abs(nivel)` (favorece niveles cercanos a 0, es decir, planta baja);
  - si contiene `"barato"` o `"econom"` → `round(tarifaHora * 10)` (favorece tarifas bajas);
  - si `preferencia == null` o no coincide → **0** (todos empatan);
  - si `nivel == null` → se trata como 0.

**Valores devueltos por el fallback:**
- `motivo` = `"Se usó asignación automática local (Ollama no respondió bien): " + shortMsg(ex.getMessage())` (truncado a 120 caracteres + `…`).
- `explicacion` = `String.format("Se eligió %s en %s por menor ocupación / tarifa.", best.getCodigo(), best.getZona())`.
- `proveedorIa` = **`"heuristica-local"`**.
- `alternativas` = las 3 primeras plazas del conjunto de candidatos distintas de la elegida, en el orden original de la lista de `/disponibles` (NO en el orden heurístico).

**Fallback del reporte narrativo (`generarTextoOllama`):** texto fijo:
```
"Ahora el parqueadero tiene plazas ocupadas que deben coincidir con tickets abiertos. "
+ "En el periodo " + periodo + " revisa lo cobrado y lo pendiente. "
+ "Prioriza cerrar tickets abiertos antiguos."
```
y `proveedorIa = "heuristica-local"`. También si el texto devuelto queda en blanco tras reemplazar `\\n` por espacio.

### 6.7 Lógica del endpoint `/recomendar` (paso a paso)

```
1. tipo = (tipoVehiculo != null) ? tipoVehiculo.toUpperCase() : "AUTO"
2. libres ← GET {espacios}/api/espacios/disponibles?tipo={tipo}   [con el token del usuario]
   - FeignException → 503 "No se pudo consultar plazas libres. Verifica que el servicio de espacios esté activo."
   - vacío/nulo     → 404 "No hay plazas LIBRES para vehículos tipo {tipo}. Cierra algún ticket abierto o elige otro tipo."
3. ocupacion ← GET {espacios}/api/espacios/ocupacion; si falla → {} (tolerante, NO falla la petición)
4. zonas = ocupacion["zonas"] si es Map, si no {}
5. Prompt (§6.3) con la lista de plazas libres
6. Ollama /api/generate (format=json, temp 0.3, num_predict 200, timeout 90s)
7. Parseo: extractResponseField → extractJsonObject → readJsonString("codigo") → validación contra `libres`
   - cualquier fallo → fallback heurístico
8. alternativas = primeras 3 plazas de `libres` (orden de la API) con `id != best.id` → "CODIGO (ZONA)"
9. Devuelve RecomendacionResponse
```
**`/recomendar` NO modifica ningún estado** (no ocupa la plaza, no crea ticket).

### 6.8 Lógica del endpoint `/asignar` (paso a paso)

```
1. placa vacía → 400 "Debes indicar la placa del vehículo (ej. ABC-123)."
2. placaNorm = placa.trim().toUpperCase()     (NO se quitan espacios internos)
3. Selección de plaza: idéntico a los pasos 1–7 de /recomendar (mismo prompt, mismo modelo, mismo fallback)
4. POST {tickets}/api/tickets/abrir  body {"placa": placaNorm, "espacioId": best.id}  [token del usuario]
   TicketDto resp = ...
5. Construye AsignacionResponse con datos de la plaza elegida + placa/ticketId/ticketCodigo del ticket creado,
   asignado = true
6. Errores:
   - FeignException → mapTicketError (409/404/5xx/otros, §1.4.2)
   - ResponseStatusException → se propaga tal cual
   - cualquier otra Exception → 502 "Ollama eligió la plaza {CODIGO}, pero no se pudo crear el ticket. ..."
```
**`/asignar` SÍ modifica estado:** `ms-tickets` marca la plaza como ocupada y crea el ticket ABIERTO.
**Riesgo conocido:** entre la elección de la plaza (paso 3) y el `abrir` (paso 4) la plaza puede ser ocupada por otro proceso → `abrir` devuelve 409 → se mapea a los mensajes de conflicto de §1.4.2.

### 6.9 Lógica de `/reporte-tickets` (paso a paso)
```
1. desde = req.fechaDesde ?? today;  hasta = req.fechaHasta ?? today
2. hasta < desde → 400 "La fecha hasta no puede ser anterior a la fecha desde"
3. sync ← POST {tickets}/api/tickets/reconciliar-plazas   (si falla, se ignora: sync = {})
4. todos ← GET {tickets}/api/tickets   (si falla → 503 "No se pudieron leer los tickets para el reporte.")
5. rango = [desde 00:00:00, hasta 23:59:59.999999999]
   tickets = todos con `entrada` dentro del rango
   abiertosAhora = TODOS los tickets con estado ABIERTO (no solo del periodo)
6. abiertosPeriodo = tickets ABIERTO en el periodo
   cerrados        = tickets CERRADO en el periodo
   recaudado       = Σ montoTotal de CERRADOS del periodo
   porCobrar       = Σ estimadoAbierto(t) para TODOS los abiertosAhora
7. porZona = agrupación de `tickets` (del periodo) por `zona`, contando
8. ocupacion ← GET {espacios}/api/espacios/ocupacion; si falla y sync.ocupacion es Map → se usa sync.ocupacion
9. cuadran = (plazasOcupadas == distinct(espacioId de abiertosAhora))
            && (plazasOcupadas == sync.plazasOcupadasTrasSync ?? plazasOcupadas)
10. ocupadasDetalle = abiertosAhora ordenados por `espacioCodigo` (como String) → {plaza, placa, zona, cliente, ticket, porCobrar}
11. hallazgos = 6–7 frases locales (ver abajo)
12. resumenDatos → prompt narrativo → Ollama generateText (temp 0.3, num_predict 350, timeout 90s)
    → fallback a texto fijo si falla
13. Devuelve ReporteTicketsResponse con metricas
```

**`estimadoAbierto(t)`** (cálculo del "por cobrar" en vivo, misma fórmula que el cobro real):
```java
minutos = max(1, Duration.between(entrada, now()).toMinutes())
horas   = ceil(minutos / 60.0)
monto   = round(horas * tarifaHora * 100) / 100
// si entrada == null o tarifaHora == null → 0 ; si el parseo falla → devuelve tarifaHora
```

**`hallazgos` (texto EXACTO de cada frase, en orden):**
1. `"Ahora hay <plazasOcupadas> plazas ocupadas de <plazasTotal> (<plazasPct>%). Libres: <plazasLibres>."`
2. `"No hay clientes estacionados en este momento."` **o** `"Plazas ocupadas por clientes: <plazasOcupadasTexto>."`
3. `"Los números cuadran: cada plaza ocupada tiene su ticket abierto."` **o** `"Revisar: plazas ocupadas (<N>) no coinciden con tickets abiertos (<M>)."`
4. `"En el periodo <periodo> hubo <N> tickets (<cerrados> cerrados, <abiertosPeriodo> aún abiertos)."`
5. `String.format("Dinero cobrado en el periodo: $%.2f.", recaudado)`
6. `String.format("Dinero pendiente por cobrar ahora: $%.2f.", porCobrar)`
7. (solo si `porZona` no está vacío) `"Movimiento por zona en el periodo: <zona>: <n> · <zona>: <n>."`

**Parseo de fechas ISO de tickets (`parseFecha`):** si la longitud es ≥19 usa los primeros 19 caracteres y `LocalDateTime.parse`; si falla, intenta los primeros 10 como `LocalDate` + `atStartOfDay()`; si falla, `null` (el ticket se excluye del periodo).

### 6.10 Resumen de proveedores de IA reportados
| Situación | `proveedorIa` |
|---|---|
| Ollama respondió y el código es válido | `ollama/llama3.2:3b` (o el modelo configurado) |
| Cualquier fallo en asignación | `heuristica-local` |
| Reporte narrativo por Ollama | `ollama/<modelo>` |
| Reporte narrativo fallback | `heuristica-local` |

---

## 7. INFRAESTRUCTURA

### 7.1 Puertos

| Componente | Puerto interno | Publicado al host | Notas |
|---|---|---|---|
| `nginx` | 80 | **8090:80** | **punto de entrada de la aplicación: http://localhost:8090** |
| `keycloak` | 8080 | **8091:8080** | admin console en http://localhost:8091 (admin/admin) |
| `ms-espacios` | 8081 | no publicado | accesible solo dentro de `urban-net` |
| `ms-vehiculos` | 8082 | no publicado | — |
| `ms-tickets` | 8083 | no publicado | — |
| `ms-ia` | 8084 | no publicado | — |
| `ms-auth` | 8085 | no publicado | — |
| `db-espacios` / `db-vehiculos` / `db-tickets` | 5432 | no publicado | PostgreSQL 16 |
| `ollama` (perfil `ollama-docker`) | 11434 | **no publicado** (comentario: "Sin mapear 11434 al host para no chocar con Ollama local") | — |

**Puertos de desarrollo local (fuera de Docker, valores por defecto de los `application.yml`):**
- `db_espacios` → `jdbc:postgresql://localhost:5433/db_espacios`
- `db_vehiculos` → `jdbc:postgresql://localhost:5434/db_vehiculos`
- `db_tickets` → `jdbc:postgresql://localhost:5435/db_tickets`
- Keycloak → `http://localhost:8080`

### 7.2 Variables de entorno del docker-compose

**Bases de datos (idénticas en las 3):**
| Variable | Valor |
|---|---|
| `POSTGRES_DB` | `db_espacios` / `db_vehiculos` / `db_tickets` |
| `POSTGRES_USER` | `urban` |
| `POSTGRES_PASSWORD` | `urban123` |

**`keycloak`:**
| Variable | Valor |
|---|---|
| `KEYCLOAK_ADMIN` | `admin` |
| `KEYCLOAK_ADMIN_PASSWORD` | `admin` |
| `KC_HTTP_PORT` | `8080` |
| `KC_HOSTNAME` | `localhost` |
| `KC_HOSTNAME_PORT` | `8091` |
| `KC_HOSTNAME_STRICT` | `false` |
| `KC_HOSTNAME_STRICT_HTTPS` | `false` |
| `KC_HTTP_ENABLED` | `true` |
| `KC_PROXY_HEADERS` | `xforwarded` |
| `hostname` (compose) | `keycloak` |
| `command` | `["start-dev", "--import-realm"]` |

**`ms-espacios`, `ms-vehiculos`:** `SPRING_DATASOURCE_URL`, `SPRING_DATASOURCE_USERNAME=urban`, `SPRING_DATASOURCE_PASSWORD=urban123`, `SPRING_SECURITY_OAUTH2_RESOURCESERVER_JWT_JWK_SET_URI=http://keycloak:8080/realms/urbanpark/protocol/openid-connect/certs`.

**`ms-tickets`:** lo anterior + `ESPACIOS_URL=http://ms-espacios:8081`.

**`ms-ia`:** `ESPACIOS_URL=http://ms-espacios:8081`, `TICKETS_URL=http://ms-tickets:8083`, `OLLAMA_BASE_URL=http://host.docker.internal:11434`, `OLLAMA_MODEL=${OLLAMA_MODEL:-llama3.2:3b}`, `SPRING_SECURITY_OAUTH2_RESOURCESERVER_JWT_JWK_SET_URI=...`.
Extra hosts: `host.docker.internal:host-gateway`.

**`ms-auth`:** `KEYCLOAK_SERVER_URL=http://keycloak:8080`, `KEYCLOAK_REALM=urbanpark`, `KEYCLOAK_ADMIN_CLIENT_ID=urbanpark-backend`, `KEYCLOAK_ADMIN_CLIENT_SECRET=urbanpark-secret`, `SPRING_SECURITY_OAUTH2_RESOURCESERVER_JWT_JWK_SET_URI=...`.

**`ollama-init`:** `OLLAMA_MODEL=${OLLAMA_MODEL:-llama3.2:3b}`.

**Variables con default en los `application.yml` (no definidas en el compose):** `OLLAMA_TIMEOUT` (90).

### 7.3 Redes
- **Una sola red definida:** `urban-net`, driver **`bridge`**, con IPAM explícito **subnet `172.25.0.0/16`**.
- **Todos** los servicios están conectados a `urban-net` (incluidos nginx, keycloak y las 3 BD).

### 7.4 Volúmenes
| Volumen | Montado en | Contenido |
|---|---|---|
| `vol_espacios` | `db-espacios:/var/lib/postgresql/data` | datos PostgreSQL de espacios |
| `vol_vehiculos` | `db-vehiculos:/var/lib/postgresql/data` | datos PostgreSQL de vehículos |
| `vol_tickets` | `db-tickets:/var/lib/postgresql/data` | datos PostgreSQL de tickets |
| `vol_ollama` | `ollama:/root/.ollama` | modelos descargados de Ollama |

**Bind mounts:**
| Host | Contenedor | Modo |
|---|---|---|
| `./keycloak/urbanpark-realm.json` | `/opt/keycloak/data/import/urbanpark-realm.json` | rw |
| `./nginx/nginx.conf` | `/etc/nginx/nginx.conf` | `:ro` |
| `./frontend` | `/usr/share/nginx/html` | `:ro` |
| `/var/run/docker.sock` | `/var/run/docker.sock` (solo en `ms-tickets`) | rw — **riesgo de seguridad: acceso al daemon Docker** |

**`depends_on`:**
- `ms-espacios` ← `[db-espacios, keycloak]`
- `ms-vehiculos` ← `[db-vehiculos, keycloak]`
- `ms-tickets` ← `[db-tickets, ms-espacios, keycloak]`
- `ms-ia` ← `[ms-espacios, ms-tickets, keycloak]`
- `ms-auth` ← `[keycloak]`
- `nginx` ← `[ms-espacios, ms-vehiculos, ms-tickets, ms-ia, ms-auth, keycloak]`
- `ollama-init` ← `[ollama]`

**`restart`:** `unless-stopped` solo en `keycloak` y `nginx`. Los microservicios y las BD no lo declaran.

**Perfiles:** `ollama-docker` agrupa `ollama` + `ollama-init`. `ollama-init` (imagen `curlimages/curl:8.5.0`) espera hasta 60 intentos × 2 s a que `http://ollama:11434/api/tags` responda y luego hace `curl -sf http://ollama:11434/api/pull -d '{"name":"<modelo>"}'`.

### 7.5 Rutas de Nginx (`nginx/nginx.conf`)

Configuración: `worker_connections 1024`, `sendfile on`, `keepalive_timeout 65`, `client_max_body_size 10m`.
`server` en `listen 80`, `server_name localhost`.

**Upstreams:**
```nginx
upstream keycloak_up { server keycloak:8080; }
upstream espacios_up { server ms-espacios:8081; }
upstream vehiculos_up { server ms-vehiculos:8082; }
upstream tickets_up  { server ms-tickets:8083; }
upstream ia_up       { server ms-ia:8084; }
upstream auth_up     { server ms-auth:8085; }
```

| `location` | Destino | Notas |
|---|---|---|
| `~* \.(js\|css)$` | `/usr/share/nginx/html` (estático) | `Cache-Control: "no-cache, must-revalidate"` |
| `/` | `/usr/share/nginx/html`, `index index.html`, `try_files $uri $uri/ /index.html` | SPA fallback |
| `/realms/` | `http://keycloak_up/realms/` | + `Host`, `X-Real-IP`, `X-Forwarded-For`, `X-Forwarded-Proto`, `X-Forwarded-Host`, **`X-Forwarded-Port 8090`** |
| `/resources/` | `http://keycloak_up/resources/` | sin cabeceras extra |
| `/api/auth` | `http://auth_up` | + `Authorization`, `Host`, `X-Forwarded-For` |
| `/api/admin` | `http://auth_up` | idem |
| `/api/perfil` | `http://auth_up` | idem |
| `/api/espacios` | `http://espacios_up` | idem |
| `/api/vehiculos` | `http://vehiculos_up` | idem |
| `/api/tickets` | `http://tickets_up` | idem |
| `/api/ia` | `http://ia_up` | idem + `proxy_connect_timeout 10s`, `proxy_send_timeout 180s`, **`proxy_read_timeout 180s`** (por la latencia de Ollama) |

**Observaciones importantes para la reimplementación:**
- Los `proxy_pass` de `/api/*` **no llevan barra final ni URI**, por lo que nginx pasa la **URI completa sin reescribir** (p. ej. `/api/espacios/1/ocupar` → `ms-espacios:8081/api/espacios/1/ocupar`). Correcto porque los controllers ya mapean `/api/...`.
- **No hay** `location /actuator` expuesto a través de nginx (los actuator solo son accesibles dentro de la red).
- **No hay** ninguna ruta hacia los puertos de las bases de datos.
- El frontend se sirve desde `/usr/share/nginx/html` (bind mount de `./frontend`).
- El acceso debe hacerse por `http://localhost:8090` (el `KC_HOSTNAME`/`X-Forwarded-Port` están configurados para ese puerto).

### 7.6 Arranque
- **Recomendado:** `docker compose up --build` (o el script `arrancar.bat`, que verifica que Docker esté instalado y en ejecución antes de levantar; mensajes: App `http://localhost:8090`, Keycloak `http://localhost:8091`, admin/admin).
- **Con Ollama en Docker:** `docker compose --profile ollama-docker up --build` (y cambiar `OLLAMA_BASE_URL` a `http://ollama:11434`).
- **Imágenes:** todos los microservicios usan build multi-stage `eclipse-temurin:17-jdk-alpine` (compila con Maven) → `eclipse-temurin:17-jre-alpine`. Solo `ms-tickets` añade `apk add --no-cache docker-cli`.

### 7.7 Dependencias Maven por servicio

| Servicio | Dependencias clave |
|---|---|
| `ms-espacios` | web, data-jpa, security, oauth2-resource-server, validation, postgresql, lombok |
| `ms-vehiculos` | web, data-jpa, security, oauth2-resource-server, validation, postgresql, lombok |
| `ms-tickets` | web, data-jpa, security, oauth2-resource-server, **aop**, **openfeign**, **circuitbreaker-resilience4j**, **resilience4j-spring-boot3**, postgresql, lombok, spring-cloud 2023.0.1 |
| `ms-ia` | web, security, oauth2-resource-server, **openfeign**, lombok, spring-cloud 2023.0.1 (**sin JPA/persistencia**) |
| `ms-auth` | web, **webflux** (WebClient), security, oauth2-resource-server, validation, lombok (**sin JPA**) |

Todos heredan de `spring-boot-starter-parent:3.2.5`, `java.version: 17`, con `spring-boot-maven-plugin`.

---

## 8. RESUMEN DE PUNTOS CRÍTICOS PARA LA REIMPLEMENTACIÓN EN ASP.NET CORE + MYSQL

1. **Circuit Breaker:** `minimumNumberOfCalls` no está definido → con `slidingWindowSize: 10` el CB difícilmente abre. En Polly/C# hay que decidir explícitamente el mínimo (p. ej. 5) para que la demo funcione como se espera.
2. **Retry:** 3 intentos, 500 ms inicial, backoff ×2 → 500 ms + 1000 ms. En Polly: `WaitAndRetryAsync(2, r => TimeSpan.FromMilliseconds(500 * Math.Pow(2, r-1)))`.
3. **Fallback:** firma obligatoria `(argumentos..., Throwable)`; devolver **200**, no error.
4. **Parser de Ollama:** replicar `extractResponseField` (parser manual de escapes) + `extractJsonObject` (regex `\{[\s\S]*?\}`) + `readJsonString`. En C# es más seguro usar `System.Text.Json` con `JsonDocument`, pero para paridad exacta de comportamiento conviene replicar los casos límite (o documentar la mejora).
5. **Prompt:** respetar exactamente el texto, incluida la **ausencia de tildes** y la línea `{"codigo":"CODIGO_PLAZA","motivo":"..."}`; el modelo pequeño depende de ese formato.
6. **`format: "json"`, `temperature: 0.3`, `num_predict: 200/350`, `stream: false`, timeout 90 s.** El `format` **se omite** en el reporte narrativo.
7. **Cobro:** `max(1 minuto)` → `ceil` a horas → `round(x*100)/100`. Sin topes ni descuentos.
8. **`liberacionPendiente`:** patrón clave de tolerancia a fallos en el cierre; el cobro **nunca** depende de ms-espacios.
9. **JWT:** validar **firma + exp/nbf** y leer `realm_access.roles`; **no** validar `iss` ni `aud` (o hacerlo configurable, documentando que el original no lo hacía).
10. **`preferred_username`** como clave de propiedad de tickets y vehículos (comparación case-insensitive).
11. **Rutas y códigos:** respetar los códigos exactos (p. ej. `DELETE /api/vehiculos/{id}` devuelve **200 vacío**, no 204; `POST` de creación devuelve **200**, no 201, salvo los dos casos que usan `ResponseEntity.status(CREATED)` en ms-auth).
12. **Mensajes de error:** copiarlos literalmente (están en §1); el frontend hace *fallback* a mensajes genéricos cuando no los reconoce.
13. **`POST /api/tickets/ocupacion` es ADMIN**, y **`/api/ia/recomendar|asignar` excluyen a OPERADOR** — detalles fáciles de errar.
14. **nginx** pasa la URI completa sin reescribir; y **no** expone `/actuator`.
15. **`ms-auth` no tiene BD**: los usuarios viven en Keycloak; la reimplementación debe decidir si mantiene Keycloak o migra la identidad (afecta a `ms-auth` y a la validación JWT de los 5 servicios).
