# Arquitectura — Urban Park móvil

## 1. Equivalencia con el proyecto original

El sistema original era un frontend HTML + 5 microservicios Java + Keycloak + 3 bases PostgreSQL + Ollama.
Esta versión móvil conserva **el mismo contrato HTTP y las mismas reglas de negocio**, pero unifica el
backend en un solo proceso y cambia las piezas de infraestructura:

| Original | Versión móvil | Motivo |
|---|---|---|
| `frontend/` (HTML + JS vanilla) | **`UrbanPark.App/`** (.NET MAUI, C#) | La app para celular que se pidió |
| 5 microservicios Spring Boot (`ms-auth`, `ms-espacios`, `ms-vehiculos`, `ms-tickets`, `ms-ia`) | **`UrbanPark.Api/`** (ASP.NET Core) con un controlador por dominio | Mismo contrato, un solo despliegue: sencillo de ejecutar desde el celular |
| Nginx (proxy en :80/:8090) | No hace falta | La app llama directo al API |
| **Keycloak** (JWT RS256, realm `urbanpark`) | **JWT propio** (HS256) + tabla `usuarios` en MySQL | Se pidió MySQL como base y Google como autenticador |
| **PostgreSQL** ×3 (`db_espacios`, `db_vehiculos`, `db_tickets`) | **MySQL** ×1 (`urbanpark`) con 4 tablas | Se pidió «una base MySQL» |
| **Ollama** (`llama3.2:3b`) | **API de DeepSeek** (`deepseek-chat`) | Se pidió atar la app a DeepSeek |
| Docker CLI (`docker stop ms-espacios`) | Bandera de simulación `Demo:EspaciosCaido` | En un solo proceso no hay contenedor que apagar |
| Resilience4j (Circuit Breaker + Retry) | Portado a mano en `ResilienciaService` | Se conservan los parámetros y el fallback |
| `localStorage` para el token | `SecureStorage` (cifrado) | Almacenamiento seguro en el dispositivo |

**Lo que se mantiene idéntico:** rutas, verbos, roles exigidos, mensajes de error, textos de la interfaz,
fórmula de cobro, validación de placa, datos semilla (20 plazas, 4 vehículos, 5 usuarios) y la regla de
pantallas por rol.

---

## 2. Estructura de la API

```
Login local / Google / Registro
        │  (JWT propio)
        ▼
   UrbanPark.App  ──HTTP──►  UrbanPark.Api  ──►  MySQL (urbanpark)
                                    │
                                    └──►  API de DeepSeek (asignación, reporte y chat)
```

### Capas del API

| Capa | Archivos | Responsabilidad |
|---|---|---|
| Arranque | `Program.cs` | JWT, Swagger, CORS, inicialización de la base |
| Datos | `Data/Db.cs`, `Data/InicializadorDb.cs` | Conexión MySQL, esquema y datos semilla |
| Modelos | `Modelos/Entidades.cs`, `Modelos/Dtos.cs`, `Modelos/Reglas.cs` | Entidades, contrato JSON y reglas de negocio |
| Repositorios | `Repositorios/*.cs` | SQL explícito y parametrizado (sin ORM) |
| Servicios | `Servicios/*.cs` | Contraseñas, JWT, Google, DeepSeek, Circuit Breaker, tickets |
| Controladores | `Controladores/*.cs` | Endpoints REST, autorización por rol y códigos de error |

---

## 3. Endpoints

Todas las rutas son las mismas del sistema original. Roles: **P** = público, **A** = autenticado,
**C/O/D** = CLIENTE / OPERADOR / ADMIN.

### Autenticación (`/api/auth`)
| Método | Ruta | Rol | Descripción |
|---|---|---|---|
| POST | `/api/auth/register` | P | Registro público (CLIENTE / OPERADOR / ADMIN) → 201 |
| POST | `/api/auth/login` | P | Login con usuario y contraseña → `accessToken`, `refreshToken` |
| POST | `/api/auth/google` | P | Login con el `id_token` de Google |
| POST | `/api/auth/refresh` | P | Renueva el token de acceso |
| GET | `/api/auth/google/config` | P | Indica si el backend tiene Google configurado |
| GET | `/api/auth/salud` | P | Estado del servicio |
| GET | `/api/auth/totp/estado` | A | ¿El usuario tiene el autenticador de Google activo? |
| POST | `/api/auth/totp/preparar` | A | Genera el secreto y la URI `otpauth://` del QR |
| POST | `/api/auth/totp/confirmar` | A | Confirma el código y activa el segundo factor |
| POST | `/api/auth/totp/desactivar` | A | Desactiva el segundo factor (pide un código válido) |

### Perfil (`/api/perfil`)
| Método | Ruta | Rol |
|---|---|---|
| GET | `/api/perfil/me` | A |
| PUT | `/api/perfil/me` | A |
| PUT | `/api/perfil/me/password` | A |

### Usuarios (`/api/admin/users`) — solo ADMIN
| Método | Ruta |
|---|---|
| GET | `/api/admin/users` |
| GET | `/api/admin/users/{id}` |
| POST | `/api/admin/users` |
| PUT | `/api/admin/users/{id}` |
| PUT | `/api/admin/users/{id}/roles` (si el rol es ADMIN asigna también OPERADOR) |
| PUT | `/api/admin/users/{id}/password` |

### Sedes / parqueaderos (`/api/sedes`)
| Método | Ruta | Rol |
|---|---|---|
| GET | `/api/sedes?lat=&lng=&tipo=&preferencia=` → los 12 parqueaderos con ocupación y distancia | A |
| GET | `/api/sedes/{id}?lat=&lng=` → ficha con sus zonas | A |
| POST | `/api/sedes/recomendar` → el parqueadero que más conviene, con motivo y explicación | C, D |

### Estacionar — pase del puesto con QR (`/api/estacionar`)
| Método | Ruta | Rol |
|---|---|---|
| POST | `/api/estacionar` `{ placa, espacioId, modo }` → abre el ticket y devuelve el pase con `qrPayload`. Con `modo = "AHORA"` entra en el acto; con `modo = "RESERVA"` + `inicioReserva`/`finReserva` guarda el puesto para esa franja y devuelve `montoPrevisto` | C, O, D |
| GET | `/api/estacionar/{ticketId}` → refresca el pase (el valor sube con el tiempo) | C, O, D |
| POST | `/api/estacionar/{ticketId}/activar` → «ya llegué»: la reserva pasa a estancia activa | C, O, D |
| POST | `/api/estacionar/{ticketId}/cancelar` → anula la reserva y libera el puesto (no cobra) | C, O, D |

### Espacios (`/api/espacios`)
| Método | Ruta | Rol |
|---|---|---|
| GET | `/api/espacios?sedeId=` | A |
| GET | `/api/espacios/disponibles?tipo=&zona=&sedeId=` | A (si viene `tipo`, ignora `zona`) |
| GET | `/api/espacios/ocupacion?sedeId=` | A |
| GET | `/api/espacios/{id}` | A |
| POST | `/api/espacios` | O, D |
| PUT | `/api/espacios/{id}` (nunca cambia `ocupado`) | O, D |
| POST | `/api/espacios/{id}/ocupar` | C, O, D |
| POST | `/api/espacios/{id}/liberar` | C, O, D |

### Vehículos (`/api/vehiculos`)
| Método | Ruta | Rol |
|---|---|---|
| GET | `/api/vehiculos` (el CLIENTE solo ve los suyos) | C, O, D |
| GET | `/api/vehiculos/{id}` | C, O, D |
| GET | `/api/vehiculos/placa/{placa}` | C, O, D |
| POST | `/api/vehiculos` | C, O, D |
| PUT | `/api/vehiculos/{id}` (no se puede cambiar la placa) | O, D |
| DELETE | `/api/vehiculos/{id}` → 200 con cuerpo vacío | O, D |

### Tickets (`/api/tickets`)
| Método | Ruta | Rol |
|---|---|---|
| GET | `/api/tickets` (el CLIENTE solo los suyos) | C, O, D |
| GET | `/api/tickets/abiertos` | C, O, D |
| GET | `/api/tickets/{id}` | C, O, D |
| GET | `/api/tickets/ocupacion` ⭐ con Circuit Breaker + Retry, **siempre 200** | D |
| GET | `/api/tickets/resilience/estado` | D |
| GET | `/api/tickets/demo/espacios/status` | D |
| POST | `/api/tickets/demo/espacios/stop` | D |
| POST | `/api/tickets/demo/espacios/start` | D |
| POST | `/api/tickets/reconciliar-plazas` | D |
| POST | `/api/tickets/sincronizar-liberaciones` | C, O, D |
| POST | `/api/tickets/abrir` | C, O, D |
| POST | `/api/tickets/{id}/cerrar` | C, O, D |

### IA (`/api/ia`)
| Método | Ruta | Rol |
|---|---|---|
| POST | `/api/ia/recomendar` | C, D (**el OPERADOR recibe 403**, como en el original) |
| POST | `/api/ia/asignar` | C, D |
| POST | `/api/ia/reporte-tickets` | D |
| POST | `/api/ia/chat` | A (**nuevo**: chat con DeepSeek) |
| GET | `/api/ia/estado` (y alias `/api/ia/ollama`) | A |
| GET | `/api/ia/salud` | P |

---

## 4. Base de datos MySQL (`urbanpark`)

| Tabla | Campos principales |
|---|---|
| `usuarios` | `id`, `username` (único), `email`, `first_name`, `last_name`, `password_hash` (PBKDF2-SHA256), `roles` (`'ADMIN,OPERADOR'`), `enabled`, `google_sub` (único), `creado_en` |
| `sedes` | `id`, `codigo` (único: CEN, PLA…), `nombre`, `direccion`, `sector`, `latitud`, `longitud`, `telefono`, `horario`, `techado`, `vigilancia`, `acepta_reserva`, `tarifa_base`, `servicios`, `activa` |
| `espacios` | `id`, `codigo` (único: `CEN-A-07`), `zona`, `tipo_vehiculo`, `ocupado`, `tarifa_hora`, `nivel` (puede ser negativo), `descripcion`, `sede_id` |
| `vehiculos` | `id`, `placa` (única), `tipo`, `marca`, `color`, `propietario`, `propietario_email`, `propietario_username`, `descuento`, `activo` |
| `tickets` | `id`, `codigo` (único `TK-XXXXXXXX`), `placa`, `espacio_id`, `espacio_codigo`, `zona`, `entrada`, `salida`, `estado` (`ABIERTO` / `RESERVADO` / `CERRADO` / `ANULADO`), `tarifa_hora`, `monto_total`, `monto_descuento`, `usuario`, `liberacion_pendiente`, `modo` (`AHORA` / `RESERVA`), `salida_prevista` |

Índices añadidos sobre `estado`, `placa`, `espacio_id`, `usuario`, `liberacion_pendiente` y `sede_id`
(el original no tenía ninguno).

**Datos semilla:** 12 parqueaderos (`sedes`) con dirección y coordenadas reales de Quito,
**768 plazas** (4 zonas de 16 por sede: `CEN-A-01` … `EBA-D-16`, tarifas 1.5–5.0), 4 vehículos y
5 usuarios demo. El código de la plaza lleva la sede delante; quien venga de una base anterior
recibe las plazas `A-01…D-16` renombradas a `CEN-…` sin perder tickets.

---

## 5. Seguridad

- **Contraseñas:** PBKDF2-SHA256, 100.000 iteraciones, salt aleatorio de 16 bytes.
  Formato `pbkdf2$100000$<salt>$<hash>` (compatible entre el script SQL y la API).
- **JWT** (HS256) con los claims del sistema original: `preferred_username` (identidad),
  `role` (autoridades), `realm_access.roles` (compatibilidad), `exp`, `iss`, `jti` y `token_use`.
- **Duración:** 300 s el token de acceso (igual que el Keycloak original) y 7 días el de refresco.
  La app refresca automáticamente cuando faltan menos de 15 segundos.
- **Autorización:** `[Authorize(Roles = "…")]` en cada endpoint, replicando los `@PreAuthorize`.
- **Google:** el `id_token` se valida contra Google (`Google.Apis.Auth`) comprobando firma y audiencia.

---

## 6. Resiliencia

Se aplica **solo** en `GET /api/tickets/ocupacion`, igual que en el original
(instancia `espaciosClient` de Resilience4j):

| Parámetro | Valor |
|---|---|
| `slidingWindowSize` | 10 |
| `failureRateThreshold` | 50 % |
| `waitDurationInOpenState` | 10 s |
| `permittedNumberOfCallsInHalfOpenState` | 3 |
| Retry | 3 intentos, 500 ms con backoff exponencial ×2 (500 ms + 1000 ms) |
| Fallback | Devuelve **200** con `fallback: true` y el mensaje, nunca propaga el error |

**Mejora documentada:** el original no definía `minimumNumberOfCalls`, así que tomaba el valor por
defecto (100) con una ventana de 10 → **el circuito nunca podía abrirse**. Aquí el mínimo es el tamaño
de la ventana (10), que es el comportamiento que la demo pretende mostrar.

**Comportamiento con el servicio de espacios caído** (botón «Detener espacios» en la pantalla Resilience):

| Endpoint | Efecto |
|---|---|
| `GET /api/tickets/ocupacion` | 3 intentos + backoff → fallback 200 |
| `POST /api/tickets/abrir` | 503 con el mensaje de dependencia no disponible |
| `POST /api/tickets/{id}/cerrar` | **cierra y cobra igual**; `liberacionPendiente = true` |
| `POST /api/tickets/reconciliar-plazas` | 503 |
| `POST /api/tickets/sincronizar-liberaciones` | `fallidas++` y mensaje «Aún no se pudo sincronizar» |
| Endpoints `/api/espacios/*` | 503 (el servicio está «caído») |

---

## 7. IA con DeepSeek

Se conserva el prompt, el parseo y el respaldo del sistema original, sustituyendo Ollama por DeepSeek.

- **Llamada:** `POST {BaseUrl}/chat/completions` con `model = deepseek-chat`, `temperature = 0.3`,
  `response_format = {"type":"json_object"}` en la elección de plaza, `stream = false`, timeout 90 s.
- **Prompt de asignación:** pide un JSON `{"codigo":"…","motivo":"…"}` y recibe la lista de plazas libres
  en el mismo formato que enviaba el original; la ocupación por zona va como mapa.
- **Anti-alucinación:** si el `codigo` devuelto no existe entre las plazas libres reales, se descarta.
- **Respaldo heurístico** (cuando DeepSeek no responde o no está configurado): ordena por
  **menor ocupación de zona → menor tarifa → preferencia** («cerca/entrada/rápido» favorece planta baja,
  «barato/económico» favorece precio) y devuelve `proveedorIa = heuristica-local`.
- **Reporte:** pide a DeepSeek un resumen de máximo 4 frases; si falla usa un texto fijo de respaldo.
- **Chat:** nuevo endpoint `/api/ia/chat` que mantiene el historial de la conversación.

---

## 8. Diferencias y mejoras respecto al original

1. **Race condition corregida:** la ocupación de la plaza usa `UPDATE … WHERE ocupado = 0`, evitando que
   dos peticiones simultáneas ocupen la misma plaza (el original no usaba transacción ni bloqueo).
2. **Índices en `tickets`** para acelerar las consultas por estado, placa y usuario.
3. **Contraseñas con hash** (PBKDF2): el original delegaba en Keycloak.
4. **Circuit Breaker que sí puede abrirse** (ver §6).
5. **Chat con la IA** integrado en la app (no existía en el original).
6. **Token en `SecureStorage`** en lugar de `localStorage`.
7. `DELETE /api/vehiculos/{id}` mantiene el 200 con cuerpo vacío del original (no 204) por fidelidad.
