# URBAN PARK — Especificación funcional COMPLETA del frontend
### Documento de referencia para reimplementación en .NET MAUI (C#)

**Fuente analizada (recorrida íntegramente):**

| Archivo | Líneas | Rol |
|---|---|---|
| `frontend/index.html` | 526 | Estructura de todas las vistas, pestañas, formularios y modales |
| `frontend/js/app.js` | 1859 | Toda la lógica de cliente, llamadas HTTP, permisos, render |
| `frontend/css/styles.css` | 1613 | Identidad visual, tokens, tipografías, layout, animaciones |

**Contraste adicional (para precisión del contrato HTTP y de los valores numéricos):** controladores, modelos, DTOs y `application.yml` de `ms-espacios` (8081), `ms-vehiculos` (8082), `ms-tickets` (8083), `ms-ia` (8084), `ms-auth` (8085), `nginx/nginx.conf` y `keycloak/urbanpark-realm.json`. Cada vez que un dato proviene de esos archivos se cita la ruta y la línea.

**Convenciones:** `MUY IMPORTANTE` = comportamiento que debe replicarse exactamente (el backend depende de él). Los textos entre «comillas angulares» son literales de la interfaz y deben reproducirse carácter por carácter.

---

# 1. IDENTIDAD VISUAL

## 1.1 Tema general

Tema **"asfalto / estacionamiento técnico"**: fondo oscuro tipo pavimento con textura de ruido y carriles de tráfico, sobre el que flotan **paneles claros** (papel hueso) con borde superior de acento. Contraste alto entre el cromo oscuro (barra superior, tarjetas de datos, cajas de código) y el contenido claro (paneles, filas de lista, formularios de login). Esquinas **casi rectas**: `--radius: 2px` (styles.css:16). Estética industrial/automotriz con acento amarillo de señalización vial.

- `color-scheme: light` (styles.css:18) — aunque el fondo es oscuro, la app se declara como esquema claro.
- Sección activa por defecto: **Plazas** (index.html:299 `class="tab active"`).
- Fondo decorativo fijo: `.asphalt` (styles.css:48-63) con `radial-gradient` amarillo arriba-izquierda, `radial-gradient` teal abajo-derecha, degradado oscuro y `repeating-linear-gradient` de bandas de 56 px (`#222930` / `#1a2027`), base `#12171c`. Encima, un SVG de turbulencia fractal (`feTurbulence`, `baseFrequency 0.85`, `numOctaves 3`) con opacidad `0.55` y opacidad de rect `0.04` (styles.css:69-71).
- **Escena animada de la calle** (`.road-scene`, styles.css:79-130; HTML en index.html:13-130): un carril amarillo discontinuo (`repeating-linear-gradient` 36 px/64 px, `perspective(520px) rotateX(62deg)`, opacidad 0.35), una **plaza de parqueo dibujada** de 168×96 px con código `A-01` (`.stall-mark`) y tope de rueda, una **señal de parqueadero** con badge verde "P" y dos LEDs (ámbar = ACCESO → verde = SEGURO), y un **carro amarillo** (dos SVG, vista lateral y vista frontal) que entra, gira 90° y queda estacionado de frente. Toda la secuencia dura **11 s**.
- Animaciones: `lane-drift` 16 s lineal infinito; `car-park-in`, `car-turn-side`, `car-turn-front`, `glow-parked`, `head-secure`, `led-warn`, `led-ok`, `text-idle`, `text-ok` (11 s, `ease-in-out`, `forwards`); `plaza-mia-pulse` 1.8 s ×2; `rise` (entrada de paneles/paneles/tarjetas, `translateY(14px)` → 0); `pull-in` (el logo aparece desde `letter-spacing: 0.45em` + `blur(4px)`); `fade-in` (modales).
- Accesibilidad de movimiento: dos bloques `@media (prefers-reduced-motion: reduce)` (styles.css:432-463 y 1516-1522) que desactivan las animaciones; en el primero, forzosamente se muestra la vista frontal del carro, el LED verde, `read-ok` y el glow.
- Foco visible: `outline: 2px solid var(--line)` con `offset 2px` (styles.css:42-45). Selección de texto: fondo amarillo, texto `--ink` (styles.css:37-40).
- Responsive: `max-width: 720px` (barra superior en varias líneas, pestañas alineadas a la izquierda con `order: 3`, inputs de formularios en línea al 100 %, `session-pill` máx. 140 px) y `max-width: 640px` (escena de calle a escala 0.78, carril a opacidad 0.25). Contenedor principal `.main` con `max-width: 1120px` centrado y padding fluido `clamp(1rem, 3vw, 1.75rem)` (styles.css:697-701).

## 1.2 Paleta de colores (códigos hex exactos)

### Variables CSS (`:root`, styles.css:1-19)

| Variable | Hex / valor | Uso |
|---|---|---|
| `--ink` | `#0c0f12` | Texto principal, fondo de botones `secondary` |
| `--paper` | `#eef1ec` | Texto claro sobre oscuro (botón `ghost`) |
| `--line` | `#f2c51a` | **Amarillo de señalización**: acento principal, logo, bordes, chips activos, pestaña activa |
| `--line-dim` | `#c9a012` | Amarillo oscurecido (borde de placa, sombra de pestaña activa) |
| `--teal` | `#1a8f7c` | **Verde-teal corporativo**: botón primario, borde superior de panel, foco de inputs |
| `--teal-deep` | `#116457` | Hover del botón primario, enlaces dentro de `.hint` |
| `--asphalt` | `#14191f` | Fondo del `body`, base de `.stat` |
| `--asphalt-2` | `#1c232b` | Degradado de `.stat` |
| `--muted` | `#6a727a` | Texto secundario |
| `--danger` | `#d6452f` | Errores, plaza ocupada, borde de toast de error |
| `--ok` | `#2f9a55` | Éxito, plaza libre, badge LIBRE, toast OK |
| `--glass` | `rgba(12, 15, 18, 0.72)` | Fondo de la barra superior (con `blur(14px) saturate(1.2)`) |
| `--shadow` | `0 22px 50px rgba(0,0,0,0.35)` | Sombra fuerte (paneles, modales, toasts) |
| `--shadow-soft` | `0 8px 24px rgba(0,0,0,0.18)` | Sombra suave (tarjetas, hover) |
| `--radius` | `2px` | Radio único de todo el sistema |

### Colores literales usados fuera de las variables

| Hex | Dónde |
|---|---|
| `#12171c` | Base del asfalto, base de `.signal-head`, badge `badge-warn` (texto), base de `.ia-card` |
| `#222930`, `#1a2027` | Bandas del pavimento |
| `#1c232b`, `#14191f` | Degradado de `.stat` |
| `#1a2128`, `#12171c` | Degradado de `.ia-card` y `.tickets-resumen` |
| `#0e1318` | Fondo de `.code-box`, `.tr-card`, cajas de métricas, cabecera de tabla de reporte, inputs oscuros |
| `#f5f7f2` | Fondo de `.modal-card` |
| `#fafbf8` → `#e8ece4` | Degradado de `.panel` (styles.css:705) |
| `#e8ece5`, `#e7ebe4`, `#eef2ea`, `#e8f5ef` | Textos claros sobre fondos oscuros |
| `#9aa3ab`, `#8b959e`, `#6a727a`, `#c5ccd3`, `#d4dbd0` | Textos secundarios/grises |
| `#c2cac0`, `#c5ccc2`, `#b7bfb4`, `#9aa396` | Bordes de inputs y chips |
| `#4a545e`, `#3d4752` | Bordes de botón `ghost` y `session-pill` |
| `#d6452f` / `#e24a3a` / `#b33a28` / `#ff8f7a` / `#ff9a86` | Rojos de error, ocupación y plaza ocupada |
| `#2f9a55` / `#7dffb0` / `#3dff7a` / `#1e3a28` / `#1f8a62` / `#145a42` | Verdes de éxito, LED OK, badge "P" |
| `#e8a23a` / `#ffd27a` / `#c9a012` | Ámbar/naranja de advertencia y LED warn |
| `#cfe0bc` | Texto del bloque `<pre>` de JSON |
| `#e4f5f1`/`#9fd0c5`/`#145a50` | Chip de tipo **AUTO** |
| `#fff3e4`/`#e2c19a`/`#6b3d12` | Chip de tipo **MOTO** |
| `#e8eef8`/`#a8bdd9`/`#1a3a6b` | Chip de tipo **CAMIONETA** |
| `#e6eef9`/`#7aa0d4`/`#1a4a8a` | Chip de tipo **DISCAPACIDAD** |
| `#f3faf5` | Fondo inferior de plaza libre |
| `#fdf4f2` | Fondo inferior de plaza ocupada |
| `#fffef5` → `#fff3c4` | Fondo de la **plaza propia** (`plaza-mia`) con halo `rgba(242,197,26,0.35)` |

### Colores del carro SVG (index.html)

- Carrocería: `#ffe566` → `#f2c51a` → `#b8890a` (lateral) y `#ffe566` → `#f2c51a` → `#9a7208` (frontal).
- Vidrios: `#9ed7ff` → `#1a2a38` (lateral) y `#d4f0ff` → `#4a90b8` → `#0f2430` (frontal).
- Neumáticos `#0c0f12`, rines `#4a525a`, detalle amarillo `#f2c51a`, faros `#fff8d0`/`#fff`/`#ffe08a`.
- Placa: fondo `#f5f7f2`, borde `#c9a012`, texto `#0c0f12`, **contenido literal `UP-01`** en fuente Bebas Neue.
- Nombre del SVG frontal: badge circular `#0c0f12` con centro `#f2c51a`.

## 1.3 Tipografía

- **Carga:** Google Fonts `https://fonts.googleapis.com/css2?family=Bebas+Neue&family=DM+Sans:ital,opsz,wght@0,9..40,400;0,9..40,500;0,9..40,700;1,9..40,400&display=swap` (index.html:9). Con `preconnect` a `fonts.googleapis.com` y `fonts.gstatic.com` (`crossorigin`).
- **Fuente base del `:root`:** `"DM Sans", system-ui, sans-serif` (styles.css:17).
- **Familia de titulares:** `"Bebas Neue", sans-serif` — usada en:
  - `.brand-mark` (logo "URBAN PARK"), `clamp(2.6rem, 7vw, 4.4rem)`, `letter-spacing 0.14em`, `line-height 0.95`, con `text-shadow: 0 3px 0 #000, 0 0 40px rgba(242,197,26,0.25)`; variante `.sm` de `1.55rem` / `0.12em`.
  - `h1` del login: `clamp(1.7rem, 4vw, 2.15rem)`, `font-weight 400`, `letter-spacing 0.05em`.
  - `h2` de cada panel (`.panel-head h2`): `clamp(1.55rem, 3.5vw, 1.95rem)`, `0.06em`.
  - Encabezados `h3` de tarjetas (`1.55rem`), `h4` de secciones de reporte (`1.2rem`, `0.05em`), `.tickets-resumen-head h3` (`1.35rem`), `.resilience-subtitle` (`1.3rem`), `.tr` y `.modal-card h3` (`1.55rem`), `.perfil-form h3` (`1.35rem`), `th` de `.perfiles-table` (`1rem`, `0.07em`), `.code` de plaza (`1.65rem`, `0.07em`), `.plaza-cliente-info strong` (`1.25rem`), `td strong` de la tabla de reporte (`1.05rem`), `.stall-mark` y la placa `UP-01` del SVG.
- **Equivalencias MAUI sugeridas:** Bebas Neue → familia de display para títulos; DM Sans → fuente de cuerpo. Tamaños base: cuerpo 0.88–0.95 rem, `small` 0.72–0.8 rem, etiquetas 0.82–0.88 rem, cabeceras de tabla 0.68 rem en mayúsculas con `letter-spacing 0.06em`.
- **Monospace** (solo `.code-box` y `<pre>`): `ui-monospace, "Cascadia Code", Consolas, monospace`, 0.78 rem, `line-height 1.45`, color `#cfe0bc` sobre `#0e1318`.
- Detalles: `.stat strong` usa `font-variant-numeric: tabular-nums` (igual en `.tr-card strong`, `.plaza-tarifa` y las métricas de reporte). `textarea` comparte estilo de inputs aunque no se usa en el HTML.

## 1.4 Logo y títulos

- **Título de la pestaña del navegador / nombre de la app:** `Urban Park` (index.html:6 `<title>Urban Park</title>`; index.html:5 `lang="es"`).
- **Logo textual** (no hay imagen/bitmap): la marca es el texto `URBAN PARK`.
  - En login: `<p class="brand-mark">URBAN PARK</p>` (index.html:197), centrado, ocupando el 100 % del ancho, con tamaño ampliado `clamp(3rem, 8vw, 5rem)` (styles.css:527-531).
  - En la app (barra superior): `<span class="brand-mark sm">URBAN PARK</span>` (index.html:295), junto a la píldora de sesión.
- **Subtítulo / claim del login:** `<h1>Estacionamiento inteligente</h1>` (index.html:198).
- **Nombre del realm/identidad back:** `urbanpark`; cliente `urbanpark-web` ("Urban Park Web"); roles de realm `CLIENTE`, `OPERADOR`, `ADMIN` (keycloak/urbanpark-realm.json:2,19-20,11-15).
- Elementos de marca secundarios: la **señal "P"** verde de la escena, la matrícula `UP-01` del carro, el código `A-01` de la plaza dibujada y la palabra `ACCESO` que pasa a `SEGURO` en el letrero.

---

# 2. MAPA DE PANTALLAS

## 2.1 Estructura de vistas (dos "vistas" raíz, mutuamente excluyentes)

| Vista | Identificador | Visibilidad |
|---|---|---|
| Inicio de sesión | `#view-login` (`section.view.login-view`) | Cuando no hay sesión válida |
| Aplicación | `#view-app` (`section.view.app-view`) | Cuando hay sesión válida |

El cambio se hace con `showLogin()` (app.js:448) y `showApp()` (app.js:457), que alternan el atributo `hidden` de ambas secciones. `[hidden] { display: none !important; }` (styles.css:23-25).

Dentro de `#view-login` hay **3 bloques internos** que alternan:

| Bloque | Identificador | Contenido |
|---|---|---|
| Formulario de login | `#login-form` | Usuario, Contraseña, Iniciar sesión, Ver perfiles y permisos, Crear cuenta, `#login-error` |
| Formulario de registro | `#register-form` | 6 campos + Registrarme, Volver al login, `#register-error`, `#register-ok` |
| Panel de perfiles demo | `#panel-perfiles` | Tabla de 5 usuarios demo con botón «Usar» |

También en login, dos ayudas fijas (index.html:286-287): «Abre siempre: **http://localhost** (no el archivo HTML).» con enlace a `http://localhost` (`target="_blank" rel="noopener"`) y «Registro: **Crear cuenta** (CLIENTE / OPERADOR / ADMIN) o pestaña Usuarios (ADMIN).»

## 2.2 Pestañas de la aplicación (9 en total)

Todas las pestañas son botones `button.tab` con `data-tab` dentro de `nav.tabs` (index.html:298-308) y cada una activa un `div.panel` con id `#tab-<nombre>`. Navegación exclusiva (una sola visible) gestionada por `irATab(id)` (app.js:355-370): marca `active` en el botón y pone `hidden` en todos los paneles salvo el seleccionado.

| # | `data-tab` | Panel | Título visible | CLIENTE | OPERADOR | ADMIN |
|---|---|---|---|---|---|---|
| 1 | `plazas` | `#tab-plazas` | «Plazas y ocupación» | ✅ | ✅ | ✅ |
| 2 | `vehiculos` | `#tab-vehiculos` | «Vehículos» (CLIENTE ve «Mis vehículos») | ✅ | ✅ | ✅ |
| 3 | `tickets` | `#tab-tickets` | «Tickets entrada / salida» (CLIENTE ve «Mis tickets») | ✅ | ✅ | ✅ |
| 4 | `ia` | `#tab-ia` | «Asignación con Ollama» | ✅ | ❌ | ✅ |
| 5 | `reporte` | `#tab-reporte` | «Reporte IA» | ❌ | ❌ | ✅ |
| 6 | `resilience` | `#tab-resilience` | «Resilience4j» (+ píldora «Solo ADMIN») | ❌ | ❌ | ✅ |
| 7 | `usuarios` | `#tab-usuarios` | «Usuarios» | ❌ | ❌ | ✅ |
| 8 | `perfil` | `#tab-perfil` | «Mi perfil» | ✅ | ✅ | ✅ |
| 9 | `token` | `#tab-token` | «Sesión y JWT» | ✅ | ✅ | ✅ |

**Regla de permisos (literal, app.js:92-96):**

```js
const TABS_POR_ROL = {
  CLIENTE:  ["plazas", "vehiculos", "tickets", "ia", "perfil", "token"],
  OPERADOR: ["plazas", "vehiculos", "tickets", "perfil", "token"],
  ADMIN:    ["plazas", "vehiculos", "tickets", "ia", "reporte", "resilience", "usuarios", "perfil", "token"],
};
```

- El **rol efectivo es único y jerárquico: ADMIN > OPERADOR > CLIENTE** (`rolPrincipal()`, app.js:83-89). Un usuario ADMIN+OPERADOR (como `admin1`) se trata como ADMIN. Un OPERADOR que además tuviera CLIENTE **no** ve las pantallas de cliente.
- `aplicarPermisosUi()` (app.js:120-183) oculta (`tab.hidden = true`) los botones no permitidos y, si la pestaña activa deja de ser permitida, salta a `permitidas[0] || "plazas"`. También oculta los paneles no permitidos y aplica el atributo genérico `data-roles` (lista separada por comas; el elemento se oculta si el rol efectivo no está en la lista). **En el HTML actual ningún elemento usa `data-roles`.**
- Clic en una pestaña no permitida (defensa redundante, app.js:1181-1189): lanza el toast de error «No tienes acceso a esta pantalla con tu perfil» y no navega.

**Textos dinámicos según rol (app.js:152-182):**

| Elemento | CLIENTE | OPERADOR | ADMIN |
|---|---|---|---|
| `#tickets-title` | «Mis tickets» | «Tickets entrada / salida» | «Tickets entrada / salida» |
| `#tickets-scope-hint` | «Perfil CLIENTE: ves tu resumen (usados, pagado y por pagar) y solo tus propios tickets/placas.» | «Perfil OPERADOR: gestionas todos los tickets del parqueadero.» | (oculto) |
| `#vehiculos-title` | «Mis vehículos» | «Vehículos» | «Vehículos» |
| `#vehiculos-scope-hint` | «Solo se listan los vehículos registrados a tu usuario.» | (oculto) | (oculto) |

**Cromo permanente de la app** (visible en todas las pestañas):

- `header.topbar` **sticky** (`top: 0`, `z-index: 10`) con fondo `--glass` + `backdrop-filter: blur(14px) saturate(1.2)`, borde inferior de 3 px amarillo, padding `0.85rem 1.25rem`, sombra `0 8px 28px rgba(0,0,0,0.25)`.
  - Logo `URBAN PARK` (`.brand-mark.sm`) + `#session-user` (`.session-pill`) con el texto `usuario · ROL` (p. ej. `cliente1 · CLIENTE`) construido en `showApp()` (app.js:464-467); si no hay rol reconocido muestra los roles filtrados separados por coma.
  - `nav.tabs` centrado con las 9 pestañas.
  - Botón `#btn-logout` «Salir» (clase `btn ghost`).

---

# 3. PANTALLA POR PANTALLA

## 3.1 `#view-login` — Inicio de sesión

### 3.1.1 Formulario de login (`#login-form`)

| Campo | Etiqueta | Tipo de input | Valor inicial | Validación | Obligatorio |
|---|---|---|---|---|---|
| `#username` | «Usuario» | `text` | **`cliente1`** (valor precargado en el HTML) | ninguna en cliente | ✅ `required` |
| `#password` | «Contraseña» | `password` | **`cliente123`** (valor precargado) | ninguna en cliente | ✅ `required` |

Atributos: `#username` con `autocomplete="username"`; `#password` con `autocomplete="current-password"`. El panel de login tiene borde izquierdo de 7 px amarillo, fondo degradado claro `rgba(255,255,255,0.97)` → `rgba(232,236,228,0.95)`, ancho `min(920px, 100%)`, y un brillo radial amarillo de 160×160 px en la esquina superior derecha.

**Botones y acciones:**

| Botón | id | Clase | Acción exacta |
|---|---|---|---|
| «Iniciar sesión» | — | `btn primary` | `submit` → `login(username.trim(), password)` y luego `showApp()` (app.js:997-1008) |
| «Ver perfiles y permisos» | `#btn-perfiles` | `btn secondary` | Alterna `#panel-perfiles`. Su propio texto cambia a «Ocultar perfiles y permisos» cuando el panel está abierto, y vuelve a «Ver perfiles y permisos» al cerrarlo (app.js:980-987) |
| «Crear cuenta» | `#btn-mostrar-registro` | `btn ghost dark` | Muestra `#register-form` y oculta `#login-form` (`mostrarRegistro(true)`, app.js:1015-1023) |

**Errores:** `#login-error` es un `<p class="error" hidden>` que se rellena con `ex.message` y se muestra. Estilo `.error`: color `--danger`, 0.9 rem, fondo `rgba(214,69,47,0.08)`, borde izquierdo de 3 px rojo. Los mensajes posibles provienen de `login()` (app.js:185-223):

- «No se pudo conectar con Keycloak. Usa http://localhost — » + mensaje técnico (por defecto «Failed to fetch»).
- «Login fallido (» + `status` + «): » + cuerpo de la respuesta (p. ej. `Login fallido (401): {"error":"invalid_grant","error_description":"Invalid user credentials"}`).

### 3.1.2 Panel de perfiles y permisos (`#panel-perfiles`)

Tabla `table.perfiles-table` con encabezados **en este orden**: `Perfil` | `Usuario demo` | `Qué puede hacer` | (columna vacía para el botón). Contenido literal de las 5 filas (index.html:252-281):

| Perfil | Usuario demo | Qué puede hacer | Botón |
|---|---|---|---|
| **CLIENTE** | `cliente1 / cliente123` | «Solo: Plazas · Mis vehículos · Mis tickets · IA · JWT (sin ver datos de otros)» | «Usar» |
| **CLIENTE** | `cliente2 / cliente123` | (idéntico) | «Usar» |
| **CLIENTE** | `cliente3 / cliente123` | (idéntico) | «Usar» |
| **OPERADOR** | `operador1 / operador123` | «Solo: Plazas · Vehículos · Tickets · JWT · Liberar plazas. Sin IA, Reporte ni Resilience» | «Usar» |
| **ADMIN** | `admin1 / admin123` | «Todas las pantallas + **Usuarios** + **Reporte IA** + **Resilience**» | «Usar» |

Cada botón «Usar» (`button.btn.primary.btn-usar` con `data-user` y `data-pass`) rellena `#username` y `#password` con las credenciales de la fila, oculta `#login-error` y **no** envía el formulario (app.js:989-995). Estilo de la tabla: cabecera con fondo `--asphalt` y texto amarillo en Bebas Neue; celdas con borde inferior `1px solid #d5dbd1`; `code` con fondo `#eef1ec`.

### 3.1.3 Formulario de registro (`#register-form`)

Visible tras pulsar «Crear cuenta». `class="stack register-form"`, inicialmente `hidden`.

| Orden | Etiqueta | `name` | Tipo | Validación HTML | Obligatorio |
|---|---|---|---|---|---|
| 1 | «Usuario» | `username` | `text` | `minlength="4"`, `autocomplete="username"` | ✅ `required` |
| 2 | «Email» | `email` | `email` | formato email | ✅ `required` |
| 3 | «Nombre» | `firstName` | `text` | — | ✅ `required` |
| 4 | «Apellido» | `lastName` | `text` | — | ✅ `required` |
| 5 | «Contraseña» | `password` | `password` | `minlength="6"`, `autocomplete="new-password"` | ✅ `required` |
| 6 | «Perfil» | `role` | `select` | opciones `CLIENTE` (por defecto), `OPERADOR`, `ADMIN` | ✅ `required` |

Validaciones equivalentes en backend (`RegisterRequestDto`, ms-auth): `username` `@NotBlank @Size(min=4, max=50)`; `email` `@NotBlank @Email`; `firstName`/`lastName` `@NotBlank`; `password` `@NotBlank @Size(min=6, max=100)`; `role` `@NotBlank @Pattern("CLIENTE|OPERADOR|ADMIN")`. **MAUI debe replicar el mínimo de 4 caracteres para usuario y 6 para contraseña.**

**Botones:**

| Botón | id | Clase | Acción |
|---|---|---|---|
| «Registrarme» | — | `btn primary` | `submit`: `POST /api/auth/register` con `username`/`email`/`firstName`/`lastName`/`password` (crudo) y `role` (por defecto `CLIENTE`) |
| «Volver al login» | `#btn-cancelar-registro` | `btn ghost dark` | `mostrarRegistro(false)`: oculta el registro y vuelve a mostrar el login |

**Mensajes:**

- **Éxito** (`#register-ok`, clase `hint`): texto literal `"Cuenta creada como " + (data.role || role del formulario || "CLIENTE") + ". Ya puedes iniciar sesión."` → p. ej. «Cuenta creada como CLIENTE. Ya puedes iniciar sesión.». Tras el éxito también: se copia el `username` al `#username` del login, se **vacía** `#password` y se hace `reset()` del formulario de registro (app.js:1050-1056).
- **Error** (`#register-error`, clase `error`): el mensaje pasa por `mensajeAmigable(status, raw, "/api/auth/register")` (ver §5.6).

## 3.2 `#tab-plazas` — «Plazas y ocupación»

### Elementos

1. **Encabezado** `.panel-head`: `<h2>Plazas y ocupación</h2>` + botón `#btn-refresh-plazas` («Actualizar», `btn secondary`) → `loadPlazas()` con toast de error si falla (app.js:1315-1317).
2. **Caja de indicadores** `#ocupacion-box.ocupacion` — 4 tarjetas `.stat` generadas por `loadPlazas()` (app.js:507-512), cada una con etiqueta en mayúsculas y valor grande en amarillo:

| Etiqueta (span) | Valor (strong) | Origen |
|---|---|---|
| `Total` | `ocup.total` | número |
| `Ocupadas` | `ocup.ocupados` | número |
| `Libres` | `ocup.libres` | número |
| `% ocupación` | `ocup.porcentajeGlobal` + `%` | número entero |

3. **Aviso de plaza del cliente** `#plaza-cliente-info` (oculto por defecto, `informarPlazaCliente()`, app.js:630-665):
   - Si el usuario tiene un ticket ABIERTO: `**Estás estacionado en la plaza {espacioCodigo||espacioId}**` y debajo `Placa {placa} · ticket {codigo} · cierra el ticket al salir`. Además **modifica el letrero de la escena** `.stall-mark` escribiendo el número de plaza.
   - Si es personal (OPERADOR/ADMIN) y hay tickets abiertos: `**{n} plaza(s) ocupada(s) por clientes**` y debajo la lista `codigo (placa)` unida por ` · `. El `.stall-mark` pasa a mostrar el código de la primera plaza ocupada.
   - Si no hay nada: el bloque se oculta y `.stall-mark` vuelve a `A-01`.
4. **Cuadrícula de plazas** `#plazas-grid.grid-plazas` — `repeat(auto-fill, minmax(190px, 1fr))`, separación 0.8 rem. Cada plaza es un `article.plaza` con `min-height: 11.5rem`, borde de 2 px y hover con `translateY(-3px)` + sombra suave.

### Tarjeta de plaza: datos exactos que muestra (app.js:520-571)

| Zona de la tarjeta | Contenido | Notas |
|---|---|---|
| Badge superior derecho | `TU PLAZA` / `OCUPADA` / `LIBRE` | `TU PLAZA` (amarillo) solo si el ticket abierto del usuario es de esa plaza |
| Código grande | `p.codigo` (p. ej. `A-01`) | Bebas Neue 1.65 rem |
| Línea 1 del meta | `ID {p.id} · {p.zona}` | p. ej. `ID 3 · Zona A - Centro` |
| Línea 2 del meta | icono+etiqueta de `p.tipoVehiculo` y ` ${p.tarifaHora}/h · N{p.nivel ?? "-"}` | p. ej. `$3.5/h · N1`; si `nivel` es nulo muestra `N-` |
| Línea de ocupación | Si es **mi plaza**: `**Tu plaza {codigo}** · placa {placa} · ticket {codigo}`. Si hay ticket abierto de otro: `**Plaza {espacioCodigo||codigo}** · placa {placa}` + ` · cliente {usuario}` (si el ticket trae usuario). Si está ocupada sin ticket conocido: `**Plaza {codigo}** · ocupada` | Texto en rojo `--danger` (0.78 rem, negrita) |
| Zona de acciones | Ver tabla siguiente | `margin-top: auto`, `min-height: 2.4rem` |

### Acciones por estado de la plaza

| Situación | Control | Texto exacto | Acción |
|---|---|---|---|
| Es **mi** plaza (ticket abierto propio) | `<p class="hint plaza-hint plaza-mia-hint">` | «Aquí estás estacionado · cierra el ticket al salir» | Solo informativo |
| Plaza **libre** | `button.btn.primary.btn-usar-plaza` con `data-codigo` | «Usar esta plaza» | Salta a la pestaña Tickets, selecciona esa plaza en `#select-plaza`, mina el toast «Plaza {codigo} lista para abrir ticket» (tipo `info`) y pone el foco en el campo de placa del formulario de ticket |
| Plaza **ocupada** y el usuario es **personal** (OPERADOR/ADMIN) | `button.btn.secondary.btn-liberar-plaza` con `data-id` y `data-codigo` | «Liberar plaza» | Abre el modal de confirmación (ver abajo); si se confirma, `POST /api/espacios/{id}/liberar`, toast «Plaza {codigo} liberada» (`ok`) y recarga las plazas |
| Plaza ocupada y el usuario es **CLIENTE** | `<p class="hint plaza-hint">` | «Ocupada · plaza **{codigo}**» + ` · {placa}` si hay ticket | Solo informativo |

### Modales / confirmaciones de esta pantalla

**Confirmación de liberación de plaza** (modal genérico `#modal-confirm`):

- Título: `Liberar plaza`
- Mensaje: `` `¿Marcar ${codigo} como libre? Úsalo si no hay ticket abierto (plaza atascada).` ``
- Botones del modal genérico: «Cancelar» (`btn ghost dark`, id `#modal-cancel`) y «Confirmar» (`btn ok`, id `#modal-ok`).

### Estados de tarjeta (estilos)

- `.plaza.libre`: borde `--ok`, fondo `linear-gradient(180deg,#fff,#f3faf5)`, badge verde `--ok`.
- `.plaza.ocupada`: borde `--danger`, fondo `linear-gradient(180deg,#fff,#fdf4f2)`, `opacity 0.92`, badge rojo.
- `.plaza.plaza-mia`: borde **amarillo de 3 px**, fondo `#fffef5 → #fff3c4`, halo `0 0 0 3px rgba(242,197,26,0.35)`, badge y `.code` en amarillo con texto `--ink`, animación `plaza-mia-pulse` 1.8 s ×2, `z-index: 2`. Al renderizar, se hace `scrollIntoView({behavior:"smooth", block:"nearest"})` sobre la tarjeta propia (app.js:573-578).
- **Mensaje de estado vacío:** no aplica (la cuadrícula siempre tiene plazas; si el backend devolviera una lista vacía, la cuadrícula queda vacía sin mensaje).

### Mensajes de éxito/error

- Éxito: «Plaza {codigo} liberada» (toast `ok`); «Plaza {codigo} lista para abrir ticket» (toast `info`).
- Error: cualquier `e.message` del API (toast `err`).
- Toast adicional al entrar a la app si el usuario tiene ticket abierto: «Estás estacionado en la plaza {espacioCodigo||espacioId}» (`info`) — `showApp()`, app.js:476-482.

### Nota técnica (importante para MAUI)

`loadPlazas()` (app.js:501-609) hace **dos** peticiones en paralelo (`GET /api/espacios` y `GET /api/espacios/ocupacion`), guarda las plazas en `state.plazasCache`, **reconstruye el `<select>` de plazas libres del formulario de tickets**, re-registra los manejadores de los botones, y si ya hay tickets en caché vuelve a llamar `renderTickets()` (para repintar los avisos de ocupación cruzados). Cualquier cambio de plazas debe refrescar también la lista de tickets y viceversa.

## 3.3 `#tab-vehiculos` — «Vehículos» / «Mis vehículos»

### Estructura

1. `.panel-head` con `<h2 id="vehiculos-title">Vehículos</h2>` (sin botón de refresco).
2. `#vehiculos-scope-hint` (`p.hint`), oculto salvo para CLIENTE: «Solo se listan los vehículos registrados a tu usuario.»
3. **Formulario de alta** `#form-vehiculo.form-inline` (**no hay formulario de edición ni de borrado en la interfaz**):

| Orden | Campo (`name`) | Placeholder / Opciones | Tipo | Obligatorio | Validación |
|---|---|---|---|---|---|
| 1 | `placa` | placeholder «Placa» | `text` | ✅ `required` | Cliente: `validarPlacaFront` (ver §5.7) → aviso «Placa inválida. Ej: ABC-123» |
| 2 | `tipo` | opciones `AUTO` (defecto), `MOTO`, `CAMIONETA` | `select` | — | backend fuerza mayúsculas; defecto `AUTO` |
| 3 | `marca` | placeholder «Marca» | `text` | ❌ | — |
| 4 | `color` | placeholder «Color» | `text` | ❌ | — |
| 5 | `propietario` | placeholder «Propietario» | `text` | ✅ `required` | Validación explícita en JS: si está vacío → toast «El propietario es obligatorio» |

   El `tipo` **no** ofrece `DISCAPACIDAD` (solo existe como tipo de plaza). Los campos van en una sola fila que se envuelve (`flex-wrap`), con `align-items: flex-end`; en móvil cada input ocupa el 100 %.

4. **Botón del formulario:** «Registrar» (`btn primary`, `submit`). Acción: valida placa → valida propietario → `POST /api/vehiculos` → `reset()` del formulario → toast **«Vehículo registrado»** (`ok`) → `loadVehiculos()`.
5. **Lista** `#vehiculos-list.list` (`display: grid; gap: 0.6rem`). Cada vehículo es un `div.row` con borde izquierdo teal de 4 px. Contenido exacto (app.js:695-704):

```
**{placa}** · {icono+tipo} · {marca || "-"} {color || ""}
{propietario}                     ← en <small>
```

   - Si `marca` es nulo → `-`; si `color` está vacío se omite (queda un espacio).
   - Botón a la derecha: «Usar placa» (`btn secondary.btn-usar-placa` con `data-placa`): salta a Tickets, escribe la placa en el campo de placa del formulario de ticket y muestra el toast «Placa {placa} cargada» (`info`).
   - **Estado vacío:** `div.empty-state` con el texto literal «No hay vehículos registrados.»

### Mensajes

| Situación | Texto | Tipo |
|---|---|---|
| Alta correcta | «Vehículo registrado» | toast `ok` |
| Placa vacía | «La placa es obligatoria» | toast `err` |
| Placa con formato inválido | «Placa inválida. Ej: ABC-123» | toast `err` |
| Propietario vacío | «El propietario es obligatorio» | toast `err` |
| Placa duplicada (409 del backend: «La placa ya está registrada») | se muestra ese mismo texto | toast `err` |
| Error de red/servicio | mensaje de `mensajeAmigable` | toast `err` |

### Restricciones de rol (backend, deben replicarse)

- `GET /api/vehiculos`, `GET /api/vehiculos/{id}`, `GET /api/vehiculos/placa/{placa}`, `POST /api/vehiculos`: CLIENTE, OPERADOR, ADMIN.
- `PUT /api/vehiculos/{id}` y `DELETE /api/vehiculos/{id}`: **solo OPERADOR/ADMIN**; el CLIENTE recibe 403 con «El cliente no puede eliminar vehículos; contacta al operador».
- Un CLIENTE solo ve y gestiona vehículos cuyo `propietarioUsername` coincide con su `preferred_username`; al crear, el backend le asigna ese usuario automáticamente.

## 3.4 `#tab-tickets` — «Tickets entrada / salida» / «Mis tickets»

### Estructura

1. `.panel-head`: `<h2 id="tickets-title">Tickets entrada / salida</h2>` + botón `#btn-sync-liberaciones` («Sincronizar plazas», `btn secondary`).
2. `#tickets-scope-hint` (`p.hint`): aviso de rol (textos en §2.2).
3. `#tickets-sync-hint` (`p.hint`, oculto): aparece solo si hay tickets con `liberacionPendiente`. Texto literal: `"{n} pago(s) cobrado(s) con plaza aún por liberar (ms-espacios estaba caído). Pulsa «Sincronizar plazas» cuando el servicio esté UP."`
4. **Resumen** `#tickets-resumen.tickets-resumen` (oculto hasta que hay tickets; `renderTicketsResumen()`): caja oscura con borde izquierdo amarillo de 4 px.
   - Título: para CLIENTE «Tu resumen de consumo»; en otros casos «Resumen de tickets».
   - Subtítulo: CLIENTE «Solo tus tickets: usados, pagados y pendientes.»; otros «Totales según los tickets visibles en esta sesión.»
   - Tres tarjetas `.tr-card` (grid de 3 columnas; 1 columna por debajo de 720 px):

| Tarjeta | Etiqueta | Valor | Nota al pie (small) | Color |
|---|---|---|---|---|
| 1 | `Tickets usados` | total de tickets | `{cerrados} cerrados · {abiertos} abiertos` | borde izquierdo `#6a727a`, valor `#e8ece5` |
| 2 | `He pagado` | `$` + suma de montos de CERRADOS con `toFixed(2)` | «Suma de tickets cerrados» | borde y valor verdes (`#2f9a55` / `#7dffb0`) |
| 3 | `Me falta por pagar` | `$` + suma estimada de ABIERTOS con `toFixed(2)` | «Estimado de tickets abiertos» o, si no hay abiertos, «Sin cargos pendientes» | borde y valor ámbar (`#e8a23a` / `#ffd27a`) |

5. **Formulario de apertura** `#form-ticket.form-inline`:

| Orden | Campo | Etiqueta/placeholder | Tipo | Obligatorio | Notas |
|---|---|---|---|---|---|
| 1 | `placa` (`#input-placa-ticket`) | placeholder «Placa», con `list="lista-placas"` y `autocomplete="off"` | `text` + `<datalist id="lista-placas">` | ✅ `required` | **Se transforma dinámicamente** (ver abajo) |
| 2 | `plaza` (`#select-plaza`) | primera opción «Elige plaza libre…» (valor vacío) | `select` | ✅ `required` | Opciones: `{codigo} · {TIPO} · {zona} · ${tarifaHora}/h` |

   **Transformación del campo de placa según rol (`llenarPlacas`, app.js:392-428) — MUY IMPORTANTE:**
   - Para **CLIENTE**, el `<input>` se **reemplaza por un `<select id="input-placa-ticket" name="placa">` con `required`**, cuya primera opción es «Elige tu placa…» y luego `{placa} · {tipo}` por cada vehículo. Si el cliente no tiene vehículos: única opción «Registra primero un vehículo». El cliente **no puede escribir placas ajenas**.
   - Para OPERADOR/ADMIN, el campo se mantiene (o se revierte) a `<input list="lista-placas" placeholder="Placa" required autocomplete="off">` con la lista completa de placas registradas en el `datalist`.

   Al cambiar de plaza/placa, el `<select id="select-plaza">` se rellena con **solo las plazas libres**: si no hay ninguna, su única opción es «No hay plazas libres».

6. **Ayuda fija:** `p.hint` «También puedes elegir la plaza desde la pestaña **Plazas** → «Usar».»
7. **Filtros** `#filtros-tickets.filtros` — tres `button.chip`, el primero con `class="chip active"`:

| `data-filtro` | Texto | Efecto |
|---|---|---|
| `TODOS` | «Todos» | Sin filtro (por defecto) |
| `ABIERTO` | «Abiertos» | Solo `estado === "ABIERTO"` |
| `CERRADO` | «Cerrados» | Solo `estado === "CERRADO"` |

   El chip pulsado recibe la clase `active` (fondo amarillo, negrita, borde amarillo) y los demás la pierden; se re-renderiza la lista sin volver a pedir datos al servidor.

8. **Lista** `#tickets-list.list`.

### Fila de ticket — contenido exacto (app.js:899-924)

Cabecera de la fila:

```
**{codigo}** · {placa} · plaza {espacioCodigo || espacioId}  {icono tipo (sm)}  {badge estado}  [badge "Plaza pendiente sync"]
```

- `codigo` en negrita (formato `TK-XXXXXXXX`), luego la placa, luego `plaza ` + código o id de la plaza.
- Icono+etiqueta del tipo de vehículo en tamaño `sm`, resuelto por `tipoDeTicket(t)`: primero busca el vehículo por placa en `state.vehiculosCache`; si no, busca la plaza por `espacioCodigo`/`espacioId` en `state.plazasCache` y usa su `tipoVehiculo`; por defecto `AUTO`.
- Badge `.badge-estado` con el texto del estado: en filas abiertas fondo teal, en cerradas fondo gris `--muted`.
- Si `liberacionPendiente` es verdadero se añade otro badge `badge-estado badge-warn` con el texto **«Plaza pendiente sync»** (fondo `#c9a012`, texto `#12171c`).

Detalle debajo (en `<small>`), generado por `detalleTicket(t)` (app.js:782-803):

- **Ticket CERRADO:**
  ```
  Ingreso: {fecha}         (negrita la etiqueta)
  Salida: {fecha}
  Valor cobrado: ${monto}   (2 decimales)  (${tarifaHora}/h)   ← el paréntesis en gris .muted
  ```
  Si `montoTotal` es nulo se recalcula con `calcularMonto(entrada, salida, tarifaHora)`. La tarifa se muestra con 2 decimales (`Number(t.tarifaHora||0).toFixed(2)`).
- **Ticket ABIERTO:**
  ```
  Ingreso: {fecha}
  Valor por cobrar (ahora): ${estimado}  · se actualiza al cerrar · ${tarifaHora}/h
  ```
  El valor se calcula en vivo con `calcularMonto(t.entrada, null, t.tarifaHora)`.

Acciones por fila: solo para ABIERTO, botón `button.btn.ok` con `data-cerrar="{t.id}"` y texto **«Cerrar / cobrar»**. Las filas cerradas no tienen acciones.

**Ordenación de la lista (`renderTickets`, app.js:872-875):** primero todos los ABIERTOS, luego los CERRADOS (comparador por estado); dentro de cada grupo, por `entrada` **descendente** (más reciente primero) usando comparación de cadenas ISO.

**Estados vacíos (texto literal):**

- CLIENTE: «No tienes tickets propios para mostrar. Abre uno con tu placa.»
- Otros roles: `No hay tickets {"" | filtro en minúsculas + "s "}para mostrar.` → con filtro TODOS: «No hay tickets para mostrar.»; con filtro ABIERTO: «No hay tickets abiertos para mostrar.»; con CERRADO: «No hay tickets cerrados para mostrar.»

### Modales de esta pantalla

**Confirmación de cierre y cobro:**

- Título: `Cerrar y cobrar`
- Mensaje: `` `¿Cerrar el ticket ${codigo} de ${placa}? Valor estimado a cobrar: $${estimado}` `` (estimado = `calcularMonto(entrada, null, tarifaHora)` con 2 decimales; si no se encuentra el ticket, `?`).
- Si el usuario cancela: toast «Cobro cancelado» (`info`) y no se llama al backend.
- Si confirma: `POST /api/tickets/{id}/cerrar` (sin cuerpo).

### Mensajes de éxito/error

| Situación | Texto literal | Tipo |
|---|---|---|
| Cierre normal | `Ticket cerrado. Cobrado: ${montoTotal || estimado con 2 decimales}` | toast `ok` |
| Cierre con plaza pendiente | `Cobrado ${monto}. Plaza quedará libre al sincronizar (ms-espacios estaba caído).` | toast `info` |
| Cancelación | «Cobro cancelado» | toast `info` |
| Apertura correcta | `Ticket abierto: {codigo} · plaza {espacioCodigo ‖ plaza elegida}` | toast `ok` |
| Placa inválida al abrir | «Placa inválida. Ej: ABC-123» / «La placa es obligatoria» | toast `err` |
| Sin plaza elegida | «Elige una plaza libre» | toast `err` |
| Plaza ya ocupada en caché | `La plaza {codigo} está ocupada. Elige otra libre.` | toast `err` |
| Ya hay ticket de esa placa en caché | `Ya tienes un ticket abierto para {placa} ({codigo})` | toast `err` |
| Plaza con ticket abierto en caché | `Esa plaza ya tiene ticket abierto ({codigo} · {placa})` | toast `err` |
| Sincronización | `{mensaje}` del backend, tipo `ok` si `liberadas > 0`, si no `info` | toast |
| Error de API | `e.message` | toast `err` |

### Botón «Sincronizar plazas»

`#btn-sync-liberaciones` → `sincronizarLiberacionesPendientes(true)` (app.js:964-977): `POST /api/tickets/sincronizar-liberaciones`, muestra el `mensaje` del backend como toast, y **siempre** recarga tickets y plazas. ⚠️ Nota de fidelidad: el código lee `r.liberadas` para decidir el color del toast; en la respuesta sin pendientes el backend no devuelve ese campo (devuelve `pendientes`), por lo que en ese caso el toast sale en `info` con el mensaje «No hay liberaciones pendientes». En MAUI basta con reproducir el mensaje del backend.

### Formulario de apertura — validaciones previas (antes de llamar al API, app.js:1374-1415)

1. `validarPlacaFront(placa)`; si falla, toast de error y fin.
2. `plaza` no vacío; si no, toast «Elige una plaza libre».
3. Si la plaza elegida figura `ocupado` en la caché → toast de error y fin.
4. Si ya existe en caché un ticket ABIERTO con esa placa → toast y fin.
5. Si ya existe un ticket ABIERTO en esa plaza (por `espacioCodigo` o `espacioId`) → toast y fin.
6. `resolverEspacioId(plaza)`: acepta **id numérico** (`/^\d+$/` → `Number(ref)`) o **código** (`B-02`, normalizado a mayúsculas, buscado en la caché o pidiendo `GET /api/espacios` si la caché está vacía). Errores: «Indica la plaza (ej. B-02)» si está vacío; `No existe la plaza '{REF}'. Revisa el código en Plazas (ej. A-01, B-02).`; `La plaza {REF} está OCUPADA. Elige una LIBRE.`
7. `POST /api/tickets/abrir` con `{ placa, espacioId }`; después `reset()` del formulario, si es CLIENTE se reconstruye el `<select>` de placas con `state.vehiculosCache`, toast de éxito, `loadTickets()` y `loadPlazas()`.

**Orden de las columnas del formulario tal como se ve:** Placa → Plaza ([Abrir ticket]).

## 3.5 `#tab-ia` — «Asignación con Ollama»

### Elementos

1. `.panel-head`: `<h2>Asignación con Ollama</h2>` + botón `#btn-ollama-status` («Estado Ollama», `btn secondary`).
2. `#ollama-status` (`p.hint`): línea de estado. Al pulsar el botón se hace `GET /api/ia/ollama` y se escribe `s.mensaje + " · modelo " + s.modelo + " · " + s.baseUrl` (app.js:1844-1851). Si falla, se escribe `e.message`. El backend devuelve: `disponible` (bool), `baseUrl` (string, por defecto `http://localhost:11434`), `modelo` (string, por defecto `llama3.2:3b`), `mensaje` («Ollama listo para asignar plazas» o «Ollama no responde. Ejecuta: docker exec ollama ollama pull llama3.2:3b»).
3. **Formulario** `#form-ia.form-inline`:

| Orden | Campo (`name`) | Placeholder / Opciones | Tipo | Obligatorio |
|---|---|---|---|---|
| 1 | `placa` | placeholder «Placa (ej. ABC-123)» | `text` | ✅ `required` (y `validarPlacaFront`) |
| 2 | `tipoVehiculo` | opciones `AUTO` (defecto), `MOTO`, `CAMIONETA`, **`DISCAPACIDAD`** | `select` | — (por defecto `AUTO` en backend) |
| 3 | `preferencia` | placeholder «Preferencia (cerca, barato…)» | `text` | ❌ |

4. Dos botones en el formulario:

| Botón | id | Clase | Tipo | Acción |
|---|---|---|---|---|
| «Solo recomendar» | `#btn-recomendar` | `btn secondary` | `button` | `POST /api/ia/recomendar` con `{tipoVehiculo, preferencia}` |
| «Asignar con Ollama» | — | `btn primary` | `submit` | `POST /api/ia/asignar` con `{placa, tipoVehiculo, preferencia}` |

5. **Tarjeta de resultado** `#ia-result.ia-card` (oculta hasta que hay respuesta). Fondo oscuro degradado con borde izquierdo amarillo de 5 px.

### Estados de la tarjeta (textos literales)

| Momento | Contenido |
|---|---|
| Antes de asignar | `<p>Ollama está eligiendo y asignando plaza…</p>` |
| Antes de recomendar | `<p>Consultando Ollama…</p>` |
| Error (`mostrarErrorIa`) | Título «No se pudo asignar», luego `<p>{mensaje}</p>` y «Consejo: en Tickets cierra el ticket abierto de esa placa, o usa otra placa.» en `<small>`. La tarjeta recibe la clase `ia-error` (borde izquierdo rojo, fondo rojizo, título `#ff9a86`) |

### Resultado de **Asignar con Ollama** (app.js:1801-1808)

```
### Asignado: {espacioCodigo} · {zona}
**Placa:** {placa} · **Ticket:** {ticketCodigo} (ID {ticketId})
**Plaza ID:** {espacioId} · ${tarifaHora}/h · Nivel {nivel}
{motivo}
{explicacionIa}            ← en cursiva
Proveedor: {proveedorIa}   ← en <small>
```
Tras una asignación correcta se recargan tickets y plazas.

### Resultado de **Solo recomendar** (app.js:1831-1838)

```
### Sugerencia: {espacioCodigo} · {zonaRecomendada}
**ID plaza:** {espacioId} · ${tarifaHora}/h · Nivel {nivel}
{motivo}
{explicacionIa}            ← en cursiva
Proveedor: {proveedorIa}   ← en <small>
Alternativas: {alternativas unidas por ", " || "ninguna"}   ← en <small>
```

### Mensajes

- Si la placa no pasa la validación: toast `err` con el motivo y **no** se consulta la IA.
- Los errores de negocio del backend llegan al usuario a través de `mostrarErrorIa` con el mensaje de `mensajeAmigable`, por ejemplo: «No se pudo asignar: esa placa ya tiene un ticket abierto, o la plaza está ocupada. Ve a Tickets → Cerrar / cobrar e intenta de nuevo.» (409), «No se pudo completar la asignación con IA. Puede que la placa ya tenga un ticket abierto o que un servicio no responda. Cierra tickets abiertos en la pestaña Tickets e intenta otra vez.» (502/503), «No se encontró lo solicitado (plaza, ticket o vehículo). Revisa los datos e intenta de nuevo.» (404).

### Comportamiento del backend que conviene conocer

`POST /api/ia/asignar` primero pide a ms-ia que seleccione plaza con Ollama y luego **abre el ticket** llamando a ms-tickets con `{placa, espacioId}`, por lo que el efecto es idéntico a abrir un ticket manualmente pero con la plaza elegida por IA. Si Ollama no responde, ms-ia usa un **fallback heurístico local** (menor ocupación de zona → menor tarifa → preferencia) y devuelve `proveedorIa: "heuristica-local"` y un `motivo` que empieza por «Se usó asignación automática local (Ollama no respondió bien): …». Si Ollama responde, `proveedorIa` es `"ollama/llama3.2:3b"` y el `motivo` es «Asignación por Ollama (llama3.2:3b): plaza {codigo}». Si no hay plazas libres del tipo pedido, el error es 404 con «No hay plazas LIBRES para vehículos tipo {TIPO}. Cierra algún ticket abierto o elige otro tipo.». La recomendación devuelve además hasta **3** `alternativas` con el formato `"{codigo} ({zona})"`.

## 3.6 `#tab-reporte` — «Reporte IA» (solo ADMIN)

### Elementos

1. `.panel-head`: `<h2>Reporte IA</h2>` + botón `#btn-reporte-ia` («Generar reporte», `btn primary`).
2. `p.hint` literal: «Elige el rango de fechas (por fecha de ingreso del ticket). Ollama analiza recaudación, ocupación y recomienda acciones.»
3. **Formulario** `#form-reporte.form-inline.reporte-fechas` — **no tiene botón submit** (se genera con el botón del encabezado):

| Orden | Etiqueta | `name` / `id` | Tipo | Obligatorio |
|---|---|---|---|---|
| 1 | «Desde» | `fechaDesde` / `#reporte-desde` | `date` | ✅ `required` |
| 2 | «Hasta» | `fechaHasta` / `#reporte-hasta` | `date` | ✅ `required` |
| 3 | «Hoy» | `#btn-reporte-hoy` (`btn secondary`) | botón | — |
| 4 | «Últimos 7 días» | `#btn-reporte-7d` (`btn secondary`) | botón | — |

   **Valores iniciales:** al cargar la página, `initFechasReporte()` (app.js:1757-1766, ejecutado en la línea 1778) pone **Desde = hoy − 6 días** y **Hasta = hoy** (7 días inclusive), en formato `yyyy-MM-dd` obtenido con `toISOString().slice(0,10)`. «Hoy» pone ambas fechas en el día actual; «Últimos 7 días» vuelve a llamar `initFechasReporte()`.

4. **Caja de reporte** `#reporte-ia-box.ia-card.reporte-card`, contenido inicial: «Selecciona fechas y pulsa **Generar reporte**.»

### Validaciones antes de llamar al API (app.js:1623-1634)

1. Si falta `desde` o `hasta` → toast `err` **«Indica fecha desde y hasta»**.
2. Si `hasta < desde` (comparación de cadenas ISO) → toast `err` **«La fecha hasta no puede ser anterior a desde»**.
3. Estado de carga: la caja muestra `<p>Generando reporte del {desde} al {hasta} con Ollama…</p>`.

### Estructura del reporte renderizado (`renderReporteClaro`, app.js:1650-1751)

Encabezado: «### Reporte del parqueadero», «Periodo de tickets: **{periodo}**» donde `periodo` = `Hoy / día {fecha}` si desde == hasta, o `{desde} → {hasta}`. A la derecha, un sello de estado: **«Estado: en orden»** (verde) si `cuadranConPlazas` es verdadero, o **«Estado: revisar»** (ámbar) si no.

Seis secciones numeradas:

**1. Plazas actuales** — ayuda «Lo que hay ahora mismo en el estacionamiento.» Cuatro métricas (grid de 4; 2 columnas por debajo de 720 px):

| Etiqueta | Valor | Color del valor |
|---|---|---|
| `Total` | `plazasTotal` | amarillo |
| `Ocupadas` | `plazasOcupadas` | rojo (`#ff8f7a`, borde `#e24a3a`) |
| `Libres` | `plazasLibres` | verde (`#7dffb0`, borde `#2f9a55`) |
| `Ocupación` | `plazasPorcentaje` + `%` | amarillo |

Si un valor es nulo se muestra `—`.

**2. Quién está estacionado ahora** — ayuda «Número de plaza ocupada por cada cliente.» Tabla `table.reporte-table` con columnas **en este orden**: `Plaza` | `Placa` | `Cliente` | `Zona` | `Por cobrar`. Filas desde `metricas.plazasOcupadasDetalle`: `plaza` (negrita, Bebas Neue amarilla), `placa` (`-` si vacío), `cliente` (— si vacío o `-`), `zona` (— si vacío), `porCobrar` formateado `$` + 2 decimales. Sin filas: fila única a 5 columnas con «Ninguna plaza ocupada ahora».

**3. Dinero del periodo** — ayuda «Tickets entre las fechas seleccionadas.» Cuatro métricas:

| Etiqueta | Valor |
|---|---|
| `Tickets del periodo` | `totalTickets` |
| `Cerrados (cobrados)` | `cerrados` |
| `Recaudado` | `$` + `recaudado.toFixed(2)` (borde amarillo) |
| `Por cobrar ahora` | `$` + `porCobrarEstimado.toFixed(2)` (borde ámbar, valor `#ffd27a`) |

**4. Resumen IA** — cita `blockquote.reporte-ia-quote` con `resumenIa` (o «Sin resumen.»).

**5. Puntos clave** — lista `<ul>` con `hallazgos` (cada elemento, un `<li>`).

**6. Actividad por zona** — lista `<ul>` con `Object.entries(metricas.porZona)` en formato `**{zona}**: {n} ticket(s)`; si está vacío, un `<li>` con «Sin actividad en el periodo».

Pie: `Generado: {metricas.generadoEn || "—"} · {proveedorIa}`.

### Errores

Si el API falla, la caja recibe `ia-error` y muestra: «### No se pudo generar el reporte» + `<p>{mensaje}</p>`. También son posibles los errores de negocio del backend (400 «La fecha hasta no puede ser anterior a la fecha desde», 503 «No se pudieron leer los tickets para el reporte.»).

### Datos concretos del backend (para cálculos exactos)

- El rango se aplica sobre `entrada` del ticket: desde `desde.atStartOfDay()` hasta `hasta.atTime(LocalTime.MAX)`.
- `recaudado` = suma de `montoTotal` de los tickets CERRADOS del periodo, redondeada a 2 decimales.
- `porCobrarEstimado` = suma del importe estimado **de todos los tickets abiertos ahora** (no solo del periodo), con la misma fórmula de cobro redondeada a 2 decimales.
- `plazasTotal/Ocupadas/Libres/Porcentaje` vienen de la ocupación real de ms-espacios (o del resultado del sync de plazas si ms-espacios no responde).
- `cuadranConPlazas` es verdadero si plazas ocupadas == número de plazas distintas con ticket abierto y coincide con el resultado del sync.
- `hallazgos` es siempre una lista de 5 a 7 frases generadas (ocupación actual, plazas ocupadas con placa, cuadre, tickets del periodo, dinero cobrado, dinero pendiente y, si hay datos, movimiento por zona).
- `proveedorIa` es `"ollama/{modelo}"` o `"heuristica-local"` si el modelo no respondió.

## 3.7 `#tab-resilience` — «Resilience4j» (solo ADMIN)

### Elementos

1. `.panel-head`: `<h2>Resilience4j</h2>` + `<span class="session-pill">Solo ADMIN</span>`.
2. Texto explicativo literal: «Demo de **Circuit Breaker + Retry**: `ms-tickets` consulta ocupación en `ms-espacios` vía Feign. Si el servicio falla, Resilience4j reintenta y activa fallback.»
3. **Fila 1 de acciones** (`.resilience-actions.form-inline`):

| Botón | id | Clase | Acción |
|---|---|---|---|
| «Probar 1 llamada» | `#btn-resilience-once` | `btn primary` | `probarResilience(1)` |
| «Stress ×5» | `#btn-resilience-stress` | `btn secondary` | `probarResilience(5)` |
| «Limpiar» | `#btn-resilience-clear` | `btn ghost dark` | Vacía el historial y los contadores, repinta el panel y muestra el toast «Historial Resilience limpiado» (`info`) |

4. **Fila 2 de acciones** (con `style="margin-top:0.55rem"`):

| Botón | id | Clase | Acción |
|---|---|---|---|
| «Detener ms-espacios» | `#btn-espacios-stop` | `btn ghost dark` | Confirmación → `POST /api/tickets/demo/espacios/stop` |
| «Iniciar ms-espacios» | `#btn-espacios-start` | `btn secondary` | `POST /api/tickets/demo/espacios/start` (sin confirmación) |
| «Estado ms-espacios» | `#btn-espacios-status` | `btn ghost dark` | `GET /api/tickets/demo/espacios/status` y toast «Estado actualizado» (`info`) |
| Píldora de estado | `#espacios-demo-status` | `session-pill` | Valor inicial `—`; pasa a `ms-espacios: UP` o `ms-espacios: DOWN`; si falla la consulta: `estado: ?` |

5. Texto de flujo literal: «Flujo sugerido: **Detener** → Probar 1 llamada (fallback) → **Iniciar** → Probar de nuevo (OK).»
6. **Estadísticas** `#resilience-stats.ocupacion.resilience-stats` — tres `.stat`:

| Etiqueta | Valor |
|---|---|
| `OK` | `state.resilienceOk` |
| `Fallback / error` | `state.resilienceFail` |
| `Total pruebas` | suma de ambos |

7. **Panel de estado** `#resilience-status.ia-card.resilience-status`:
   - Reposo (texto inicial literal): «Pulsa **Probar 1 llamada** para ver Circuit Breaker + Retry en acción.»
   - Al lanzar: «Ejecutando {n} llamada(s) a `/api/tickets/ocupacion`…»
   - Éxito sin fallback: título «Circuit Breaker + Retry OK» + «Ocupación live: total {total} · ocupados {ocupados} · libres {libres}» + `<small>{resilience} · origen: {origen} · {ms} ms</small>`; la tarjeta recibe la clase `resilience-ok` (borde izquierdo verde).
   - Éxito con `fallback: true`: título «Fallback activo» + `<p>{mensaje}</p>` + `<small>Origen: {origen} · {ms} ms</small>`.
   - Error de red/HTTP: título «Error en la prueba» + `<p>{error}</p>` y clase `ia-error` (borde rojo).
8. **Título «Respuesta JSON»** (`.resilience-subtitle`) y bloque `pre#resilience-out.code-box` con `JSON.stringify(data, null, 2)`, o `—` en reposo, o el mensaje de error. Es una caja oscura `#0e1318` con texto `#cfe0bc`, `max-height: 280px` y scroll.
9. **Título «Historial de pruebas»** y lista `#resilience-log.list`. Cada entrada es un `div.row` con:
   ```
   **#{n}** · {label}   {badge: "OK" | "FALLBACK" | "ERROR"}
   {hora} · {ms} ms     ← en <small>
   ```
   donde `n` es correlativo, `label` es «Live ms-espacios» o «Fallback Resilience4j» (o el mensaje de error), `hora` es `new Date().toLocaleTimeString("es-EC")`. Las entradas se insertan **al principio** de la lista (`unshift`), así que lo más reciente aparece arriba; **no hay límite de tamaño del historial**. Vacío: «Sin pruebas aún.»

### Modales

**Confirmación de detención:**

- Título: `Detener ms-espacios`
- Mensaje: «¿Detener el contenedor ms-espacios? Luego prueba Resilience para ver el fallback.»

### Mensajes

| Acción | Éxito | Error |
|---|---|---|
| Probar 1 llamada | «Prueba Resilience completada» (`ok`) | toast `err` con el mensaje |
| Stress ×5 | «Stress ×5 completado» (`ok`) | toast `err` |
| Limpiar | «Historial Resilience limpiado» (`info`) | — |
| Estado | «Estado actualizado» (`info`) | toast `err` |
| Detener | `r.mensaje` del backend («ms-espacios detenido. Prueba ahora Resilience (debería activar fallback).») como `ok` | toast `err` |
| Iniciar | `r.mensaje` («ms-espacios iniciado. Espera unos segundos y prueba de nuevo.») como `ok`; luego «Sincronizando plazas pendientes…» (`info`) | toast `err` |
| Tras sincronizar al iniciar | «Pagos verificados: plazas liberadas y disponibles» (`ok`) si `liberadas > 0`; si falla: «ms-espacios inició, pero aún no sincroniza. Usa «Sincronizar plazas» en Tickets.» (`info`) | — |
| Guardia de rol | Si el rol efectivo no es ADMIN al pulsar: toast «Solo ADMIN puede usar Resilience» (`err`) | — |

### Parámetros exactos de Resilience4j (ms-tickets/application.yml:32-47)

- Circuit Breaker `espaciosClient`: `slidingWindowSize: 10`, `failureRateThreshold: 50`, `waitDurationInOpenState: 10s`, `permittedNumberOfCallsInHalfOpenState: 3`, `automaticTransitionFromOpenToHalfOpenEnabled: true`.
- Retry `espaciosClient`: `maxAttempts: 3`, `waitDuration: 500ms`, `enableExponentialBackoff: true`, `exponentialBackoffMultiplier: 2`.

## 3.8 `#tab-usuarios` — «Usuarios» (solo ADMIN)

### Elementos

1. `.panel-head`: `<h2>Usuarios</h2>` + botón `#btn-refresh-usuarios` («Actualizar», `btn secondary`).
2. `p.hint` literal: «Mantenimiento via Keycloak (ms-auth). Solo ADMIN.»
3. **Formulario de creación** `#form-usuario-crear.form-inline`:

| Orden | Placeholder | `name` | Tipo | Validación | Obligatorio |
|---|---|---|---|---|---|
| 1 | «Usuario» | `username` | `text` | `minlength="4"` | ✅ `required` |
| 2 | «Email» | `email` | `email` | formato email | ✅ `required` |
| 3 | «Nombre» | `firstName` | `text` | — | ✅ `required` |
| 4 | «Apellido» | `lastName` | `text` | — | ✅ `required` |
| 5 | «Contraseña» | `password` | `password` | `minlength="6"` | ✅ `required` |
| 6 | — | `role` | `select` (`CLIENTE` por defecto, `OPERADOR`, `ADMIN`) | — | ✅ `required` |
| 7 | Botón | — | `btn primary` submit | — | — |

   Texto del botón: **«Crear»**. Acción: `POST /api/admin/users` con los 6 campos (se hace `trim` a `username`, `email`, `firstName`, `lastName`; `password` y `role` van crudos) → toast **«Usuario creado»** (`ok`) → `reset()` → `loadUsuarios()`. Los errores son toast `err` con el mensaje amigable.

4. **Tabla** `table.reporte-table#usuarios-table` dentro de `.table-wrap`. Columnas **en este orden (6)**:

| # | Encabezado | Contenido | Formato |
|---|---|---|---|
| 1 | `Usuario` | `u.username` | en **negrita** |
| 2 | `Nombre` | `firstName` + `lastName` unidos por espacio; si ambos vacíos → `—` | texto |
| 3 | `Email` | `u.email` o `—` | texto |
| 4 | `Roles` | `(u.roles \|\| []).join(", ")` o `—` | texto |
| 5 | `Estado` | `u.enabled ? "Activo" : "Inactivo"` | dentro de `<span class="session-pill">` |
| 6 | `Acciones` | tres botones (ver abajo) | `.user-actions` con `flex-wrap` |

   Cada `<tr>` lleva `data-id="{id}"`. Filas especiales: al cargar, `Cargando…` en una celda que abarca 6 columnas (`.empty-row`); si la lista viene vacía, `Sin usuarios`. Todo el contenido pasa por `escHtml()` (escapa `&`, `<`, `>`, `"`).

   **Botones de acción por fila (textos exactos):**

| Botón | Clase | `data-*` | Acción |
|---|---|---|---|
| «Editar» | `btn secondary btn-sm` | `data-edit="{id}"` | Abre el **modal de edición de usuario** |
| «Password» | `btn ghost dark btn-sm` | `data-pwd="{id}"` | Abre el **modal de reseteo de contraseña** |
| «Desactivar» / «Activar» | `btn ghost dark btn-sm` | `data-toggle="{id}"` | Texto dinámico según `u.enabled`. Pide confirmación y luego `PUT /api/admin/users/{id}` con el estado invertido |

### Modales

**A. Modal de edición de usuario** (`#modal-user.modal` con `.modal-card.modal-card-form`)

- Título dinámico: `Editar · {username}` (el título estático del HTML es «Editar usuario», pero se sobrescribe siempre al abrir).
- Formulario `#form-user-edit.stack`:

| Orden | Etiqueta | `id` / `name` | Tipo | Validación | Obligatorio |
|---|---|---|---|---|---|
| 0 | (oculto) | `edit-user-id` / `id` | `hidden` | — | — |
| 1 | «Nombre» | `edit-firstName` / `firstName` | `text` | — | ✅ `required` |
| 2 | «Apellido» | `edit-lastName` / `lastName` | `text` | — | ✅ `required` |
| 3 | «Email» | `edit-email` / `email` | `email` | formato email | ✅ `required` |
| 4 | «Perfil» | `edit-role` / `role` | `select` (`CLIENTE`, `OPERADOR`, `ADMIN`) | — | — |
| 5 | «Activo» | `edit-enabled` / `enabled` | `checkbox` (`.check-row`) | — | — |

   Al abrir se precargan los valores del usuario y el `select` de perfil se sitúa en el **rol principal** del usuario (`ADMIN > OPERADOR > CLIENTE`; si no tiene ninguno, el primer rol de la lista o `—`).
- Botones: «Cancelar» (`btn ghost dark`, id `#modal-user-cancel`) y «Guardar» (`btn primary`, `submit`).
- Acción al guardar (**dos llamadas seguidas, en este orden**): `PUT /api/admin/users/{id}` con `{email, firstName, lastName, enabled}` y después `PUT /api/admin/users/{id}/roles` con `{role}`. Si ambas van bien: toast **«Usuario actualizado»** (`ok`), cierre del modal y recarga de la lista. **Nota de fidelidad:** el botón «Guardar» se muestra siempre, aunque no se haya abierto desde «Editar» (si el `id` está vacío, la URL queda `/api/admin/users/` y el backend responde error).
- Cierre: botón «Cancelar» o clic en el fondo del modal (solo si el clic es exactamente sobre `#modal-user`).

**B. Modal de reseteo de contraseña** (`#modal-password.modal`)

- Título fijo: **«Reset contraseña»**.
- Contenido: `#pwd-user-id` (hidden), párrafo `#pwd-user-label.hint` con «Usuario: {username}» (o el id si no hay username) y el campo:

| Etiqueta | `id` / `name` | Tipo | Validación | Obligatorio |
|---|---|---|---|---|
| «Nueva contraseña» | `pwd-new` / `password` | `password` | `minlength="6"`, `autocomplete="new-password"` | ✅ `required` |

- Botones: «Cancelar» (`#modal-password-cancel`) y «Actualizar» (`btn primary`, submit).
- Acción: `PUT /api/admin/users/{id}/password` con `{password}` → toast **«Contraseña actualizada»** (`ok`) y cierre del modal.

**C. Confirmación de activar/desactivar** (modal genérico):

- Si el usuario está activo: título **«Desactivar usuario»**, mensaje «¿Desactivar este usuario?».
- Si está inactivo: título **«Activar usuario»**, mensaje «¿Activar este usuario?».
- Al confirmar: `PUT /api/admin/users/{id}` con `{email, firstName, lastName, enabled}` (con los valores del usuario y el estado invertido) → toast **«Usuario activado»** o **«Usuario desactivado»** (`ok`) → recarga de la lista.

### Mensajes de esta pantalla

| Acción | Éxito | Error |
|---|---|---|
| Crear | «Usuario creado» (`ok`) | toast `err` |
| Editar + roles | «Usuario actualizado» (`ok`) | toast `err` |
| Reset password | «Contraseña actualizada» (`ok`) | toast `err` |
| Activar | «Usuario activado» (`ok`) | toast `err` |
| Desactivar | «Usuario desactivado» (`ok`) | toast `err` |
| Actualizar lista | — | toast `err` |

### Carga

`irATab("usuarios")` dispara `loadUsuarios()` automáticamente (app.js:361-362), y de nuevo al pulsar «Actualizar». Antes de la petición la tabla se pinta con «Cargando…».

## 3.9 `#tab-perfil` — «Mi perfil» (todos los roles)

### Estructura

`.panel-head` con `<h2>Mi perfil</h2>` + botón `#btn-refresh-perfil` («Recargar», `btn secondary`). Debajo, `.perfil-grid` (`repeat(auto-fit, minmax(260px, 1fr))`) con **dos formularios** en tarjetas oscuras (`.perfil-form`, fondo `rgba(14,19,24,0.65)`, borde blanco al 8 %).

**Formulario A — «Datos personales»** (`#form-perfil.stack.perfil-form`):

| Orden | Etiqueta | `id` / `name` | Tipo | Validación | Obligatorio |
|---|---|---|---|---|---|
| 0 | `#perfil-username` (`p.hint`) | «Usuario: {username} · Roles: {roles unidos por ", " o "—"}» | texto informativo | — | — |
| 1 | «Nombre» | `perfil-firstName` / `firstName` | `text` | — | ✅ `required` |
| 2 | «Apellido» | `perfil-lastName` / `lastName` | `text` | — | ✅ `required` |
| 3 | «Email» | `perfil-email` / `email` | `email` | formato email | ✅ `required` |
| 4 | Botón «Guardar perfil» | — | `btn primary` submit | — | — |

Acción: `PUT /api/perfil/me` con `{firstName, lastName, email}` (con `trim`) → toast **«Perfil actualizado»** (`ok`) → `loadPerfil()` de nuevo. Errores: toast `err`.

**Formulario B — «Cambiar contraseña»** (`#form-perfil-password.stack.perfil-form`):

| Orden | Etiqueta | `name` | Tipo | Validación | Obligatorio |
|---|---|---|---|---|---|
| 1 | «Contraseña actual» | `currentPassword` | `password` | `autocomplete="current-password"` | ✅ `required` |
| 2 | «Nueva contraseña» | `newPassword` | `password` | `minlength="6"`, `autocomplete="new-password"` | ✅ `required` |
| 3 | Botón «Cambiar contraseña» | — | `btn primary` submit | — | — |

Acción: `PUT /api/perfil/me/password` con `{currentPassword, newPassword}` → toast **«Contraseña cambiada»** (`ok`) → `reset()` del formulario. Errores: toast `err` (por ejemplo, si la contraseña actual es incorrecta, el mensaje que devuelva ms-auth).

**Carga:** `irATab("perfil")` llama a `loadPerfil()` (app.js:364-366) y «Recargar» también. `loadPerfil()` hace `GET /api/perfil/me` y rellena el texto de usuario/roles y los tres campos.

**Nota:** en este formulario **no se puede cambiar el nombre de usuario** (el backend tampoco lo permite).

## 3.10 `#tab-token` — «Sesión y JWT» (todos los roles)

### Estructura

`.panel-head` con `<h2>Sesión y JWT</h2>` (sin botones). Debajo:

1. `#token-meta.token-meta` — tarjeta blanca con borde izquierdo teal de 3 px y cuatro líneas, generadas en `renderTokenPanel()` (app.js:489-499):

| Etiqueta | Valor |
|---|---|
| `Usuario:` | `p.preferred_username` o `-` |
| `Roles:` | `rolesFrom(p).join(", ")` o `-` (todos los roles del realm en el token, sin filtrar) |
| `Expira:` | `new Date(p.exp * 1000).toLocaleString()` (formato local del dispositivo) o `-` |
| `Issuer:` | `p.iss` o `-` |

2. `pre#token-raw.code-box` — el **JWT completo en texto plano** (caja oscura con scroll, `max-height: 280px`).

Se vuelve a renderizar al entrar a la app (`showApp`), después de cada refresco de token y al recorrer la app. Si no hay token, se muestra cadena vacía.

---

# 4. CONTRATO HTTP

## 4.0 Configuración de red del cliente

| Constante | Valor | app.js |
|---|---|---|
| `KEYCLOAK_TOKEN` | `/realms/urbanpark/protocol/openid-connect/token` (relativa, pasa por nginx) | 1-2 |
| `KEYCLOAK_TOKEN_FALLBACK` | `http://localhost:8091/realms/urbanpark/protocol/openid-connect/token` | 3-4 |
| `CLIENT_ID` | `urbanpark-web` | 5 |
| `STORAGE_KEY` | `urbanpark.session` | 6 |
| `API_BASE` | `""` (vacío → todas las rutas `/api/...` son relativas al mismo origen) | 7 |

- Todas las llamadas de negocio pasan por **nginx en `http://localhost` (puerto 80)** y se enrutan por prefijo (nginx/nginx.conf:48-98): `/api/auth`, `/api/admin`, `/api/perfil` → **ms-auth:8085**; `/api/espacios` → **ms-espacios:8081**; `/api/vehiculos` → **ms-vehiculos:8082**; `/api/tickets` → **ms-tickets:8083**; `/api/ia` → **ms-ia:8084**; `/realms/` y `/resources/` → **keycloak:8080**. ms-ia tiene timeouts ampliados: `proxy_connect_timeout 10s`, `proxy_send_timeout 180s`, `proxy_read_timeout 180s` (por el tiempo de respuesta de Ollama).
- Cabeceras en todas las llamadas de negocio (`api()`, app.js:253-276): `Content-Type: application/json` y `Authorization: Bearer {accessToken}`.
- El login **no** usa `api()`: va directo a Keycloak con `Content-Type: application/x-www-form-urlencoded`.
- `api()` devuelve `null` si el estado HTTP es **204**; en cualquier otro caso parsea JSON.

## 4.1 Autenticación (Keycloak, OIDC direct grant)

### A. `POST /realms/urbanpark/protocol/openid-connect/token` (login con contraseña)

- **Quién lo dispara:** `login()` desde el submit del formulario de login (app.js:185-223).
- **Content-Type:** `application/x-www-form-urlencoded`.
- **Cuerpo (URLSearchParams):**

| Campo | Tipo | Valor |
|---|---|---|
| `grant_type` | string | `password` |
| `client_id` | string | `urbanpark-web` |
| `username` | string | usuario introducido (`trim`) |
| `password` | string | contraseña introducida (sin trim) |

- **Comportamiento de reintento:** intenta primero la URL relativa y, si la petición lanza excepción, la absoluta a `http://localhost:8091`. Si la petición responde con error HTTP **no** reintenta (el `res` no es nulo y sale del bucle).
- **Respuesta (JSON de Keycloak):** `access_token` (string), `refresh_token` (string), `expires_in` (int, segundos), más campos estándar (`token_type`, `refresh_expires_in`, `scope`, …). Se usan los tres primeros.
- **Efectos en cliente:** `state.accessToken`, `state.refreshToken`, `state.expiresAt = Date.now() + expires_in*1000`, `state.profile = parseJwt(access_token)` y `saveSession()`.
- **Errores:** si no hay respuesta → «No se pudo conectar con Keycloak. Usa http://localhost — {mensaje}»; si la respuesta no es `ok` → «Login fallido ({status}): {cuerpo}».
- **Realm:** `urbanpark`, cliente público `urbanpark-web` con `directAccessGrantsEnabled: true` y `standardFlowEnabled: true`, `fullScopeAllowed: true`, redirect URIs `http://localhost/*`, `http://localhost:80/*`, `*`, atributo `pkce.code.challenge.method: S256` (keycloak/urbanpark-realm.json:18-35). **No hay `accessTokenLifespan` declarado**, por lo que rige el valor por defecto de Keycloak (**300 s = 5 min**) → el refresco ocurre prácticamente en cada llamada (ver §5.1). `sslRequired: "none"`.

### B. `POST /realms/urbanpark/protocol/openid-connect/token` (refresco)

- **Quién lo dispara:** `refreshIfNeeded()` antes de cada llamada de `api()` (app.js:225-251).
- **Cuerpo:** `grant_type=refresh_token`, `client_id=urbanpark-web`, `refresh_token={refreshToken}`.
- **Respuesta:** igual que el login (`access_token`, `refresh_token`, `expires_in`).
- La URL del refresco es **solo la relativa** (sin fallback).
- **Si falla:** `clearSession()`, `showLogin()` y se lanza «Token expirado. Vuelve a iniciar sesión.»

### C. `POST /api/auth/register` (registro público)

- **Quién lo dispara:** submit de `#register-form` (app.js:1026-1061). Se usa `fetch` directo (no `api()`, por lo que **no envía token**) con `Content-Type: application/json`.
- **Cuerpo:**

| Campo | Tipo | Origen | Notas |
|---|---|---|---|
| `username` | string | input, `trim` | 4–50 caracteres |
| `email` | string | input, `trim` | formato email |
| `firstName` | string | input, `trim` | obligatorio |
| `lastName` | string | input, `trim` | obligatorio |
| `password` | string | input | 6–100 caracteres |
| `role` | string | select, por defecto `CLIENTE` | `CLIENTE` \| `OPERADOR` \| `ADMIN` |

- **Respuesta 201:** `{ id: string, username: string, role: string, message: string }` donde `message` = «Usuario registrado como {role}». El frontend solo usa `role`.
- **Efecto colateral:** exige `Content-Type: application/json`. `POST /api/auth/register` es el **único endpoint público** de ms-auth además de `/actuator/**` (SecurityConfig de ms-auth:43).

## 4.2 ms-auth — Perfil

### `GET /api/perfil/me`

- **Dispara:** `loadPerfil()` al entrar en la pestaña «Mi perfil» o al pulsar «Recargar» (app.js:1306-1313).
- **Auth:** cualquier rol autenticado (`/api/perfil/**` → `authenticated()`).
- **Respuesta (`UserResponseDto`):** `id` (string), `username` (string), `email` (string), `firstName` (string), `lastName` (string), `enabled` (bool), `roles` (string[]). El frontend usa `username`, `roles`, `firstName`, `lastName`, `email`.

### `PUT /api/perfil/me`

- **Dispara:** submit de «Datos personales» (app.js:1145-1161).
- **Cuerpo (`UpdatePerfilDto`):** `email` (string, `@Email`, obligatorio), `firstName` (string, obligatorio), `lastName` (string, obligatorio). Todos con `trim`.
- **Respuesta:** `UserResponseDto` completo (el frontend la ignora y vuelve a llamar `GET /api/perfil/me`).
- **Éxito UI:** toast «Perfil actualizado».

### `PUT /api/perfil/me/password`

- **Dispara:** submit de «Cambiar contraseña» (app.js:1163-1179).
- **Cuerpo (`ChangePasswordDto`):** `currentPassword` (string, obligatorio), `newPassword` (string, obligatorio, 6–100).
- **Respuesta:** `{ "message": "Contraseña cambiada" }`.
- **Éxito UI:** toast «Contraseña cambiada»; se limpia el formulario.

## 4.3 ms-auth — Administración de usuarios (solo ADMIN)

`AdminUserController` tiene `@PreAuthorize("hasRole('ADMIN')")` a nivel de clase y además `/api/admin/**` está restringido a `hasRole("ADMIN")` en la configuración de seguridad.

### `GET /api/admin/users`

- **Dispara:** `loadUsuarios()` (al entrar a la pestaña Usuarios o al pulsar «Actualizar») (app.js:1207-1212).
- **Respuesta:** array de `UserResponseDto`: `id` (string), `username` (string), `email` (string), `firstName` (string), `lastName` (string), `enabled` (bool), `roles` (string[]).

### `POST /api/admin/users`

- **Dispara:** submit de `#form-usuario-crear` (app.js:1067-1088).
- **Cuerpo:** idéntico a `RegisterRequestDto`/`CreateUserDto`: `username` (4–50), `email` (`@Email`), `firstName`, `lastName`, `password` (6–100), `role` (`CLIENTE|OPERADOR|ADMIN`). Los cuatro primeros con `trim`.
- **Respuesta 201:** `UserResponseDto` del usuario creado.
- **Éxito UI:** toast «Usuario creado».

### `PUT /api/admin/users/{id}`

- **Dispara:** (a) botón «Guardar» del modal de edición —primera llamada—; (b) confirmación de activar/desactivar.
- **Cuerpo (`UpdateUserDto`):** `email` (`@Email`, obligatorio), `firstName` (obligatorio), `lastName` (obligatorio), `enabled` (bool, opcional).
- **Respuesta:** `UserResponseDto` actualizado.

### `PUT /api/admin/users/{id}/roles`

- **Dispara:** segunda llamada del botón «Guardar» del modal de edición.
- **Cuerpo (`UpdateRolesDto`):** `role` (string, obligatorio, `CLIENTE|OPERADOR|ADMIN`).
- **Respuesta:** `UserResponseDto` actualizado. ⚠️ El backend **reemplaza** los roles de realm por el único rol enviado (`replaceRealmRoles`), por lo que editar un usuario y guardar puede quitarle roles adicionales. Relevante porque `admin1` tiene `["ADMIN","OPERADOR"]`.

### `PUT /api/admin/users/{id}/password`

- **Dispara:** submit del modal «Reset contraseña» (app.js:1126-1139).
- **Cuerpo (`ResetPasswordDto`):** `password` (string, obligatorio, 6–100).
- **Respuesta:** `{ "message": "Contraseña actualizada" }`.
- **Éxito UI:** toast «Contraseña actualizada».

## 4.4 ms-espacios

### `GET /api/espacios`

- **Dispara:** `loadPlazas()` (al entrar a la app, al pulsar «Actualizar» en Plazas, tras abrir/cerrar tickets, tras liberar una plaza y al sincronizar) y `resolverEspacioId()` cuando la caché está vacía (app.js:676).
- **Auth:** autenticado (cualquier rol).
- **Respuesta:** array de `Espacio`:

| Campo | Tipo | Descripción |
|---|---|---|
| `id` | number (Long) | Identificador |
| `codigo` | string | Único, formato `A-01` … `D-05` |
| `zona` | string | «Zona A - Centro», «Zona B - Norte», «Zona C - Sur», «Zona D - Este» |
| `tipoVehiculo` | string | `AUTO`, `MOTO`, `CAMIONETA`, `DISCAPACIDAD` |
| `ocupado` | bool | Estado |
| `tarifaHora` | number (Double) | Tarifa por hora |
| `nivel` | number (Integer) | 1, 2, -1 |
| `descripcion` | string | Texto libre |

### `GET /api/espacios/ocupacion`

- **Dispara:** `loadPlazas()` en paralelo con la anterior.
- **Respuesta (Map):**

| Campo | Tipo | Descripción |
|---|---|---|
| `total` | number | Total de plazas |
| `ocupados` | number | Plazas ocupadas |
| `libres` | number | Plazas libres |
| `porcentajeGlobal` | number | Entero redondeado (`Math.round(ocupados*100/total)`, 0 si total = 0) |
| `zonas` | object | Mapa `zona → { total, ocupados, libres, porcentaje }` |

### `POST /api/espacios/{id}/liberar`

- **Dispara:** botón «Liberar plaza» de una plaza ocupada, solo OPERADOR/ADMIN, tras confirmación (app.js:600).
- **Cuerpo:** ninguno (no se envía cuerpo; sí van las cabeceras con `Content-Type`).
- **Respuesta:** el `Espacio` actualizado con `ocupado: false`.
- **Éxito UI:** toast «Plaza {codigo} liberada» y recarga de plazas.

### Endpoints de ms-espacios **no usados** por el frontend

`GET /api/espacios/disponibles?tipo=&zona=` (lo usa ms-ia), `GET /api/espacios/{id}`, `POST /api/espacios` y `PUT /api/espacios/{id}` (estos dos últimos solo OPERADOR/ADMIN), `POST /api/espacios/{id}/ocupar` (lo usa ms-tickets). `ocupar` devuelve 409 «La plaza ya está ocupada» si ya lo estaba.

## 4.5 ms-vehiculos

### `GET /api/vehiculos`

- **Dispara:** `loadVehiculos()` en `showApp()` (solo si la pestaña Vehículos es visible para el rol) y al registrar un vehículo (app.js:688-718).
- **Auth:** CLIENTE, OPERADOR, ADMIN.
- **Filtrado por rol (backend, `VehiculoService.listar`):** un usuario cuyo rol principal sea **CLIENTE** recibe solo los vehículos con `propietarioUsername == preferred_username`; OPERADOR y ADMIN reciben **todos**.
- **Respuesta:** array de `Vehiculo`:

| Campo | Tipo | Notas |
|---|---|---|
| `id` | number | — |
| `placa` | string | Único, en mayúsculas |
| `tipo` | string | `AUTO`, `MOTO`, `CAMIONETA` |
| `marca` | string \| null | — |
| `color` | string \| null | — |
| `propietario` | string | Obligatorio |
| `propietarioEmail` | string \| null | No lo muestra la interfaz |
| `propietarioUsername` | string \| null | Dueño Keycloak; no lo muestra la interfaz |

### `POST /api/vehiculos`

- **Dispara:** submit de `#form-vehiculo` (app.js:1355-1371).
- **Auth:** CLIENTE, OPERADOR, ADMIN.
- **Cuerpo:** `{ placa, tipo, marca, color, propietario }`. La placa ya va normalizada por el cliente (mayúsculas, sin espacios); `tipo` es el valor del select; `marca` y `color` van tal cual (pueden ser cadena vacía); `propietario` sin `trim` en el envío.
- **Respuesta:** el `Vehiculo` creado.
- **Reglas del backend (deben replicarse en MAUI):** placa obligatoria; normalizada (`trim`, mayúsculas, sin espacios); debe cumplir `^[A-Z0-9]{2,8}(-?[A-Z0-9]{1,4})?$` y tener entre 4 y 12 caracteres, si no → 400 «Placa inválida. Usa formato tipo ABC-123»; `propietario` obligatorio → 400 «El propietario es obligatorio»; placa duplicada → 409 «La placa ya está registrada»; `tipo` se fuerza a mayúsculas con defecto `AUTO`; si el usuario es CLIENTE se le asigna `propietarioUsername`; el `id` se ignora/limpia.

### Endpoints **no usados** por el frontend

`GET /api/vehiculos/{id}`, `GET /api/vehiculos/placa/{placa}`, `PUT /api/vehiculos/{id}` (OPERADOR/ADMIN), `DELETE /api/vehiculos/{id}` (OPERADOR/ADMIN). El CLIENTE no puede editar ni eliminar vehículos («El cliente no puede eliminar vehículos; contacta al operador»).

## 4.6 ms-tickets

### `GET /api/tickets`

- **Dispara:** `loadTickets()` en `showApp()` y tras abrir, cerrar o asignar con IA (app.js:805-810).
- **Auth:** CLIENTE, OPERADOR, ADMIN.
- **Filtrado por rol (backend, `TicketService.listar`):** CLIENTE recibe solo los tickets con `usuario == preferred_username`; OPERADOR y ADMIN reciben **todos**. ⚠️ No hay paginación ni parámetros de consulta: siempre devuelve la lista completa y el filtrado por estado se hace **en el cliente**.
- **Respuesta:** array de `Ticket`:

| Campo | Tipo | Notas |
|---|---|---|
| `id` | number (Long) | — |
| `codigo` | string | Único, generado como `TK-` + 8 caracteres de un UUID en mayúsculas |
| `placa` | string | Normalizada en mayúsculas |
| `espacioId` | number | Id de la plaza |
| `espacioCodigo` | string | Código de la plaza (p. ej. `A-01`) |
| `zona` | string | Zona de la plaza |
| `entrada` | string (LocalDateTime ISO, sin zona) | Fecha/hora de ingreso |
| `salida` | string \| null | Fecha/hora de salida |
| `estado` | string | `ABIERTO` \| `CERRADO` |
| `tarifaHora` | number \| null | Tarifa congelada al abrir |
| `montoTotal` | number \| null | Importe cobrado (solo al cerrar) |
| `usuario` | string \| null | `preferred_username` dueño del ticket |
| `liberacionPendiente` | bool | `true` si se cobró pero la plaza no se pudo liberar |

### `POST /api/tickets/abrir`

- **Dispara:** (a) submit de `#form-ticket`; (b) ms-ia desde `POST /api/ia/asignar`.
- **Auth:** CLIENTE, OPERADOR, ADMIN.
- **Cuerpo (`AbrirRequest`):** `placa` (string), `espacioId` (number/Long). **Importante:** se envía el **id numérico**, nunca el código; el cliente lo resuelve con `resolverEspacioId()`.
- **Respuesta:** el `Ticket` creado con `estado: "ABIERTO"`, `entrada` = ahora, `tarifaHora` copiada de la plaza, `usuario` = usuario autenticado, `espacioCodigo` y `zona` de la plaza.
- **Errores del backend (textos literales):**
  - 400 «Debes indicar una plaza válida» (espacioId nulo o ≤ 0).
  - 400 «La placa es obligatoria» / «Placa inválida. Usa formato tipo ABC-123 o ABC123 (4 a 12 caracteres).»
  - 409 «Ya existe un ticket abierto para la placa {PLACA} (ticket {CODIGO}). Ciérralo antes de abrir otro.»
  - 409 «La plaza ya tiene un ticket abierto ({CODIGO} · {PLACA}). Elige otra plaza libre o cierra ese ticket.»
  - 409 (de ms-espacios) «La plaza ID {id} ya está ocupada. Elige una plaza LIBRE en la pestaña Plazas.»
  - 404 «No existe la plaza ID {id}. Revisa el ID en Plazas.»
  - 503 «ms-espacios no disponible (Resilience/Feign). ¿Está caído el servicio? Detalle: {body}»
- **Efecto en la UI:** toast `Ticket abierto: {codigo} · plaza {espacioCodigo}` (`ok`), recarga de tickets y plazas, y (si el usuario es CLIENTE) reconstrucción del select de placas.

### `POST /api/tickets/{id}/cerrar`

- **Dispara:** botón «Cerrar / cobrar» de una fila ABIERTO, tras confirmación.
- **Auth:** CLIENTE, OPERADOR, ADMIN. Un CLIENTE solo puede cerrar sus propios tickets, si no → 403 «Solo puedes ver o cerrar tus propios tickets» (y 404 «Ticket no encontrado» si el id no existe).
- **Cuerpo:** ninguno.
- **Respuesta:** el `Ticket` actualizado con `estado: "CERRADO"`, `salida`, `montoTotal` y `liberacionPendiente`.
- **Errores:** 409 «El ticket ya está cerrado».
- **Efecto en la UI:** toast «Ticket cerrado. Cobrado: ${monto}» (`ok`) o, si `liberacionPendiente`, «Cobrado ${monto}. Plaza quedará libre al sincronizar (ms-espacios estaba caído).» (`info`); después recarga tickets y plazas.

### `POST /api/tickets/sincronizar-liberaciones`

- **Dispara:** botón «Sincronizar plazas» de la pestaña Tickets y, automáticamente, tras «Iniciar ms-espacios» en Resilience (app.js:966, 1500).
- **Auth:** CLIENTE, OPERADOR, ADMIN (un CLIENTE solo procesa los suyos).
- **Cuerpo:** ninguno.
- **Respuesta (Map):**
  - Si **no hay pendientes**: `{ pendientes: 0, liberadas: 0, fallidas: 0, mensaje: "No hay liberaciones pendientes", detalle: [] }`.
  - Si hay pendientes: `{ pendientesIniciales, liberadas, fallidas, sinCambio, pendientesRestantes, mensaje, detalle: string[] }` con `mensaje` = «Sincronizado: {n} plaza(s) marcada(s) como disponible(s)» o «Aún no se pudo sincronizar (¿ms-espacios sigue caído?)» o «Sin cambios».
- **Efecto en la UI:** toast con el `mensaje` (tipo `ok` si `liberadas > 0`, si no `info`) y recarga de tickets y plazas.

### `POST /api/tickets/reconciliar-plazas` (solo ADMIN)

- **Dispara:** automáticamente al pulsar «Iniciar ms-espacios» en Resilience, **solo si el rol efectivo es ADMIN**, tras esperar 8 s y antes de sincronizar (app.js:1497-1499). No hay botón propio.
- **Cuerpo:** ninguno. **Respuesta (Map):** `ticketsAbiertos`, `plazasConTicketAbierto`, `plazasOcupadasTrasSync`, `plazasLibresTrasSync`, `plazasTotal`, `porcentajeGlobal`, `ocupacion`, `ajustesOcupar`, `ajustesLiberar`, `duplicadosCerrados`, `sinCambio`, `cuadran` (bool), `detalle` (string[]), `mensaje`. El frontend ignora la respuesta.

### `GET /api/tickets/ocupacion` (solo ADMIN) — prueba de Resilience4j

- **Dispara:** `probarResilience(n)` con n = 1 o 5 (app.js:1585).
- **Auth:** `hasRole('ADMIN')`.
- **Respuesta en el camino normal:** el mapa de ocupación de ms-espacios **más** `{ origen: "ms-espacios (live)", resilience: "Circuit Breaker + Retry OK" }` → contiene `total`, `ocupados`, `libres`, `porcentajeGlobal`, `zonas`.
- **Respuesta de fallback** (si ms-espacios falla tras los reintentos): `{ fallback: true, origen: "ms-tickets fallback", mensaje: "Resilience4j activó fallback: {mensaje de error}", total: 0, ocupados: 0, libres: 0 }`. **No** incluye `porcentajeGlobal` ni `zonas`.
- **Interpretación en el cliente:** si `data.fallback` es verdadero se cuenta como fallo (`resilienceFail`) y la etiqueta del historial es «Fallback Resilience4j»; si no, se cuenta como OK y la etiqueta es «Live ms-espacios».
- **Cronometraje:** `performance.now()` alrededor de la llamada, redondeado a milisegundos enteros.

### `GET /api/tickets/demo/espacios/status` (solo ADMIN)

- **Dispara:** `refreshEspaciosDemoStatus()` al entrar a Resilience y al pulsar «Estado ms-espacios» (app.js:1456-1465).
- **Respuesta:** `{ servicio: "ms-espacios", running: bool, containerId: string, mensaje: string }`. El cliente solo usa `running` → píldora `ms-espacios: UP` / `ms-espacios: DOWN`.

### `POST /api/tickets/demo/espacios/stop` (solo ADMIN)

- **Dispara:** botón «Detener ms-espacios» tras confirmación.
- **Respuesta:** `{ action: "stop", servicio: "ms-espacios", containerId, mensaje: "ms-espacios detenido. Prueba ahora Resilience (debería activar fallback).", ok: true }`.
- **Errores:** 502 «No se pudo detener ms-espacios: {salida}»; 404 «No se encontró el contenedor ms-espacios. ¿Está el stack con docker compose?»; 503 con la pista «¿ms-tickets tiene docker.sock y docker-cli?».
- **Efecto en la UI:** toast con `mensaje` (`ok`) y actualización de la píldora de estado.

### `POST /api/tickets/demo/espacios/start` (solo ADMIN)

- **Dispara:** botón «Iniciar ms-espacios» (sin confirmación).
- **Respuesta:** `{ action: "start", servicio: "ms-espacios", containerId, mensaje: "ms-espacios iniciado. Espera unos segundos y prueba de nuevo.", ok: true }`.
- **Efecto en la UI (secuencia completa, app.js:1488-1514):** toast con `mensaje` (`ok`) → actualizar píldora → toast «Sincronizando plazas pendientes…» (`info`) → **espera 8000 ms** → si el rol es ADMIN, `POST /api/tickets/reconciliar-plazas` → `sincronizarLiberacionesPendientes(true)` → si `liberadas > 0`, toast «Pagos verificados: plazas liberadas y disponibles» (`ok`) → si algo de eso falla, toast «ms-espacios inició, pero aún no sincroniza. Usa «Sincronizar plazas» en Tickets.» (`info`) → volver a actualizar la píldora.

### Endpoints de ms-tickets **no usados** por el frontend

`GET /api/tickets/abiertos`, `GET /api/tickets/{id}`.

## 4.7 ms-ia

### `POST /api/ia/recomendar`

- **Dispara:** botón «Solo recomendar» (app.js:1816-1842).
- **Auth:** CLIENTE, ADMIN (un OPERADOR recibe 403).
- **Cuerpo:** `{ tipoVehiculo: string, preferencia: string }` (ambos pueden ir vacíos; el backend convierte `tipoVehiculo` a mayúsculas con defecto `AUTO`).
- **Respuesta (`RecomendacionResponse`):**

| Campo | Tipo |
|---|---|
| `zonaRecomendada` | string |
| `espacioId` | number |
| `espacioCodigo` | string |
| `tarifaHora` | number |
| `nivel` | number |
| `motivo` | string |
| `explicacionIa` | string |
| `proveedorIa` | string (`ollama/{modelo}` o `heuristica-local`) |
| `alternativas` | string[] (hasta 3, formato `"{codigo} ({zona})"`) |

### `POST /api/ia/asignar`

- **Dispara:** submit de `#form-ia` (app.js:1780-1814).
- **Auth:** CLIENTE, ADMIN.
- **Cuerpo:** `{ placa: string, tipoVehiculo: string, preferencia: string }`. El cliente normaliza la placa antes de enviar (`validarPlacaFront`); el backend la pasa a mayúsculas.
- **Respuesta (`AsignacionResponse`):** `espacioId` (number), `espacioCodigo` (string), `zona` (string), `tarifaHora` (number), `nivel` (number), `placa` (string, la del ticket creado), `ticketId` (number), `ticketCodigo` (string), `motivo` (string), `explicacionIa` (string), `proveedorIa` (string), `asignado` (bool, siempre `true` si se devuelve con éxito).
- **Errores:** 400 «Debes indicar la placa del vehículo (ej. ABC-123).»; 404 «No hay plazas LIBRES para vehículos tipo {TIPO}. Cierra algún ticket abierto o elige otro tipo.»; 409 «La placa {PLACA} ya tiene un ticket ABIERTO. Ve a Tickets y pulsa «Cerrar / cobrar» antes de asignar otra plaza.» o «No se pudo asignar la plaza {CODIGO} porque está ocupada o en conflicto. Elige otra preferencia o cierra tickets abiertos.»; 404 «No se encontró la plaza {CODIGO} para crear el ticket.»; 503 «El servicio de tickets no responde. Espera unos segundos e intenta de nuevo.»; 503 «No se pudo consultar plazas libres. Verifica que el servicio de espacios esté activo.»; 502 «Ollama eligió la plaza {CODIGO}, pero no se pudo crear el ticket. Intenta de nuevo o abre el ticket manualmente en Tickets. Detalle: {error}».

### `GET /api/ia/ollama`

- **Dispara:** botón «Estado Ollama» (app.js:1844-1851).
- **Auth:** autenticado (cualquier rol autenticado; `/api/ia/salud` es lo único público).
- **Respuesta:** `{ disponible: bool, baseUrl: string, modelo: string, mensaje: string }`. El cliente compone la línea `mensaje · modelo {modelo} · {baseUrl}`.
- **Configuración:** `ollama.base-url` = `http://localhost:11434`, `ollama.model` = `llama3.2:3b`, `ollama.timeout-seconds` = `90` (ms-ia/application.yml:22-25). El estado consulta `GET {baseUrl}/api/tags` con timeout de 5 s.

### `POST /api/ia/reporte-tickets` (solo ADMIN)

- **Dispara:** botón «Generar reporte» (app.js:1623-1648).
- **Cuerpo:** `{ fechaDesde: "yyyy-MM-dd", fechaHasta: "yyyy-MM-dd" }` (el backend los mapea a `LocalDate`; si faltan, usa la fecha de hoy en ambos).
- **Respuesta (`ReporteTicketsResponse`)** — campos de primer nivel:

| Campo | Tipo | Descripción |
|---|---|---|
| `totalTickets` | number | Tickets con entrada en el periodo |
| `abiertos` | number | De los anteriores, aún ABIERTOS |
| `cerrados` | number | De los anteriores, CERRADOS |
| `recaudado` | number | Suma de `montoTotal` de los cerrados, redondeada a 2 decimales |
| `porCobrarEstimado` | number | Estimado de **todos** los tickets abiertos ahora, 2 decimales |
| `fechaDesde` / `fechaHasta` | string | `yyyy-MM-dd` |
| `plazasTotal` | number | Plazas del parqueadero |
| `plazasOcupadas` | number | Ocupadas ahora |
| `plazasLibres` | number | Libres ahora |
| `plazasPorcentaje` | number | % de ocupación (entero) |
| `ticketsAbiertosAhora` | number | Tickets ABIERTO ahora |
| `cuadranConPlazas` | bool | Coinciden plazas ocupadas y plazas con ticket abierto |
| `resumenIa` | string | Párrafo narrativo (Ollama o heurística local) |
| `proveedorIa` | string | `ollama/{modelo}` o `heuristica-local` |
| `hallazgos` | string[] | Frases de análisis (5–7) |
| `metricas` | object | Ver abajo |

  Y dentro de `metricas`: `fechaDesde`, `fechaHasta`, `periodo`, `plazasTotal`, `plazasOcupadas`, `plazasLibres`, `plazasPorcentaje`, `ticketsAbiertosAhora`, `cuadranConPlazas`, `totalTickets`, `abiertosPeriodo`, `cerrados`, `recaudado`, `porCobrarEstimado`, `porZona` (mapa `zona → nº de tickets`), `plazasOcupadasDetalle` (array de objetos), `ocupacion` (mapa de ocupación), `sync` (resultado de la reconciliación), `generadoEn` (`yyyy-MM-dd HH:mm:ss`).
  Cada elemento de `plazasOcupadasDetalle` tiene: `plaza` (string), `placa` (string), `zona` (string), `cliente` (string, `-` si no hay), `ticket` (string), `porCobrar` (number). El frontend usa `plaza`, `placa`, `cliente`, `zona`, `porCobrar`.

- **Errores:** 400 «La fecha hasta no puede ser anterior a la fecha desde»; 503 «No se pudieron leer los tickets para el reporte.»

## 4.8 Resumen de endpoints consumidos (20 en total)

| # | Método | Ruta | Servicio | Roles | Disparador en la UI |
|---|---|---|---|---|---|
| 1 | POST | `/realms/urbanpark/protocol/openid-connect/token` (password) | Keycloak | público | «Iniciar sesión» |
| 2 | POST | `/realms/urbanpark/protocol/openid-connect/token` (refresh) | Keycloak | sesión | Antes de cada llamada |
| 3 | POST | `/api/auth/register` | ms-auth | público | «Registrarme» |
| 4 | GET | `/api/perfil/me` | ms-auth | autenticado | Pestaña «Mi perfil», «Recargar» |
| 5 | PUT | `/api/perfil/me` | ms-auth | autenticado | «Guardar perfil» |
| 6 | PUT | `/api/perfil/me/password` | ms-auth | autenticado | «Cambiar contraseña» |
| 7 | GET | `/api/admin/users` | ms-auth | ADMIN | Pestaña «Usuarios», «Actualizar» |
| 8 | POST | `/api/admin/users` | ms-auth | ADMIN | «Crear» |
| 9 | PUT | `/api/admin/users/{id}` | ms-auth | ADMIN | «Guardar» (modal), activar/desactivar |
| 10 | PUT | `/api/admin/users/{id}/roles` | ms-auth | ADMIN | «Guardar» (modal, 2.ª llamada) |
| 11 | PUT | `/api/admin/users/{id}/password` | ms-auth | ADMIN | «Actualizar» (modal contraseña) |
| 12 | GET | `/api/espacios` | ms-espacios | autenticado | Plazas, y `resolverEspacioId` |
| 13 | GET | `/api/espacios/ocupacion` | ms-espacios | autenticado | Plazas |
| 14 | POST | `/api/espacios/{id}/liberar` | ms-espacios | OPERADOR/ADMIN | «Liberar plaza» |
| 15 | GET | `/api/vehiculos` | ms-vehiculos | CLIENTE/OPERADOR/ADMIN | Entrada a la app, alta de vehículo |
| 16 | POST | `/api/vehiculos` | ms-vehiculos | CLIENTE/OPERADOR/ADMIN | «Registrar» |
| 17 | GET | `/api/tickets` | ms-tickets | CLIENTE/OPERADOR/ADMIN | Entrada, apertura, cierre, IA |
| 18 | POST | `/api/tickets/abrir` | ms-tickets | CLIENTE/OPERADOR/ADMIN | «Abrir ticket» |
| 19 | POST | `/api/tickets/{id}/cerrar` | ms-tickets | CLIENTE/OPERADOR/ADMIN | «Cerrar / cobrar» |
| 20 | POST | `/api/tickets/sincronizar-liberaciones` | ms-tickets | CLIENTE/OPERADOR/ADMIN | «Sincronizar plazas», tras iniciar ms-espacios |
| 21 | POST | `/api/tickets/reconciliar-plazas` | ms-tickets | ADMIN | Tras «Iniciar ms-espacios» |
| 22 | GET | `/api/tickets/ocupacion` | ms-tickets | ADMIN | «Probar 1 llamada» / «Stress ×5» |
| 23 | GET | `/api/tickets/demo/espacios/status` | ms-tickets | ADMIN | Entrar a Resilience, «Estado ms-espacios» |
| 24 | POST | `/api/tickets/demo/espacios/stop` | ms-tickets | ADMIN | «Detener ms-espacios» |
| 25 | POST | `/api/tickets/demo/espacios/start` | ms-tickets | ADMIN | «Iniciar ms-espacios» |
| 26 | POST | `/api/ia/recomendar` | ms-ia | CLIENTE/ADMIN | «Solo recomendar» |
| 27 | POST | `/api/ia/asignar` | ms-ia | CLIENTE/ADMIN | «Asignar con Ollama» |
| 28 | GET | `/api/ia/ollama` | ms-ia | autenticado | «Estado Ollama» |
| 29 | POST | `/api/ia/reporte-tickets` | ms-ia | ADMIN | «Generar reporte» |

---

# 5. LÓGICA DE CLIENTE

## 5.1 Manejo del token JWT

**Estado en memoria (`state`, app.js:9-23):** `accessToken`, `refreshToken`, `expiresAt` (epoch ms), `profile` (payload decodificado), `plazasCache`, `ticketsCache`, `vehiculosCache`, `filtroTickets` (`"TODOS"` por defecto), `pendingConfirm`, `resilienceLog`, `resilienceOk`, `resilienceFail`, `usuariosCache`.

**Dónde se guarda:** `localStorage` bajo la clave **`urbanpark.session`**, con un JSON de exactamente tres campos (`saveSession()`, app.js:28-37):

```json
{ "accessToken": "...", "refreshToken": "...", "expiresAt": 1730000000000 }
```

El `profile` **no** se persiste: se vuelve a calcular con `parseJwt(accessToken)` al restaurar la sesión.

**Decodificación del JWT (`parseJwt`, app.js:62-71):** parte el token por `.`, toma el segmento 1 (payload), reemplaza `-`→`+` y `_`→`/`, rellena con `=` hasta múltiplo de 4, decodifica base64 y parsea el JSON. **No se valida la firma en el cliente** (solo se lee).

**Roles:** `rolesFrom(profile)` = `profile.realm_access.roles` (array). `hasAnyRole(...)` comprueba pertenencia (se define pero el flujo real usa `rolPrincipal()`).

**Expiración (dos mecanismos):**

1. **Al arrancar** (app.js:1854-1859): `if (loadSession() && Date.now() < state.expiresAt) showApp(); else { clearSession(); showLogin(); }`. Si el token guardado ya expiró, se descarta la sesión silenciosamente.
2. **Antes de cada petición** (`refreshIfNeeded()`, app.js:225-251): si `Date.now() < expiresAt - 15000` (margen de **15 segundos**) no hace nada; si no, exige `refreshToken` (si falta → «Sesión expirada») y hace el `grant_type=refresh_token`. Si el refresco falla: `clearSession()`, `showLogin()` y error «Token expirado. Vuelve a iniciar sesión.». Si va bien, actualiza los tres datos, recalcula el `profile`, guarda la sesión y repinta la pestaña «Sesión y JWT».

**Cómo se envía:** cabecera `Authorization: Bearer {accessToken}` en todas las llamadas que pasan por `api()`. El `POST /api/auth/register` es `fetch` directo y **no** lleva token.

**Cierre de sesión (`#btn-logout`, app.js:1010-1013):** `clearSession()` (borra memoria + `localStorage`) y `showLogin()`. **No** se llama al endpoint de logout de Keycloak ni se revoca el refresh token en el servidor, y **no** se limpian las cachés de datos (`plazasCache`, `ticketsCache`, `vehiculosCache`, `usuariosCache`, historial de Resilience) ni el contenido ya renderizado de los paneles.

**Manejo de 401/403 en `api()`:** ante 401 o 403 se lee el cuerpo, se llama a `clearSession()` + `showLogin()` y se lanza el error con `mensajeAmigable(status, raw, path)`.

**Datos de la pestaña «Sesión y JWT»:** usuario, todos los roles del token, fecha de expiración (`exp` × 1000, formato local) e `iss`, más el token en crudo.

## 5.2 Filtrado

| Ámbito | Mecanismo |
|---|---|
| **Tickets por estado** | `state.filtroTickets` con valores `TODOS` \| `ABIERTO` \| `CERRADO`. Los chips de `#filtros-tickets` cambian el estado y repintan. `TODOS` no filtra. El filtro **no** vuelve a consultar al servidor |
| **Tickets por usuario** | Lo hace el **backend** según el rol (CLIENTE solo los suyos). El cliente no filtra por usuario, pero sí **localiza** «mi ticket abierto» con `miTicketAbierto()` comparando `t.usuario` en minúsculas con `preferred_username` en minúsculas |
| **Plazas libres (select)** | `llenarSelectPlazas()` filtra `!p.ocupado` de la lista completa |
| **Placás (datalist)** | `llenarPlacas()` usa la lista devuelta por `/api/vehiculos` (ya filtrada por el backend para CLIENTE) |
| **Cuadre plaza↔ticket** | `ticketAbiertoDePlaza(p)` busca en la caché de tickets un `estado === "ABIERTO"` cuyo `espacioId` o `espacioCodigo` (mayúsculas) coincida con la plaza |
| **Vehicles por usuario** | Solo backend |
| **Usuarios** | Sin búsqueda ni filtros en la interfaz |

## 5.3 Ordenación

- **Tickets:** ABIERTO antes que CERRADO; dentro de cada grupo, `entrada` **descendente** (comparación lexicográfica de las cadenas ISO). `renderTickets`, app.js:872-875.
- **Plazas:** se muestran en el orden en que llegan del backend (`repository.findAll()`, sin `ORDER BY` explícito → en la práctica por id ascendente). El cliente **no** reordena.
- **Vehículos:** orden de llegada del backend (por id). El cliente no reordena.
- **Usuarios:** orden de llegada de Keycloak. El cliente no reordena.
- **Historial de Resilience:** más reciente primero (`unshift`).
- **Detalle de plazas ocupadas del reporte** (backend): ordenado por `espacioCodigo` como cadena.

## 5.4 Paginación

**No existe paginación en ninguna pantalla.** Ni en `GET /api/tickets`, ni en `GET /api/vehiculos`, ni en `GET /api/admin/users`, ni en el reporte. Tampoco hay scroll infinito, ni parámetros `page`/`size`, ni `limit`. Todas las listas se cargan completas. (Nota para la reimplementación en MAUI: conviene usar `CollectionView` con virtualización, pero sin paginación de servidor.)

## 5.5 Formateo de fechas y de moneda

**Fechas — `formatearFecha(iso)` (app.js:720-732):**

- Si el valor es nulo/vacío → `-`.
- Si `new Date(iso)` no es válido → se devuelve la cadena original tal cual.
- Si es válido → `d.toLocaleString("es-EC", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit", second: "2-digit" })`, es decir formato **dd/mm/aaaa hh:mm:ss** con separadores según la configuración regional `es-EC`. Se usa en el detalle de cada ticket (ingreso y salida).
- **Fechas de la pestaña «Sesión y JWT»:** `new Date(exp*1000).toLocaleString()` (locale del dispositivo, sin opciones).
- **Hora del historial de Resilience:** `new Date().toLocaleTimeString("es-EC")`.
- **Fechas de reporte:** se envían y muestran como cadenas `yyyy-MM-dd` sin transformar; el pie usa `metricas.generadoEn` con formato `yyyy-MM-dd HH:mm:ss`.
- **Fechas de los inputs `date`:** valor `yyyy-MM-dd` calculado con `toISOString().slice(0,10)` (⚠️ usa UTC, no la zona local).

**Moneda — `$` con 2 decimales, siempre a la izquierda del número y sin separador de miles:**

| Contexto | Formato exacto | Ejemplo |
|---|---|---|
| Tarifa de plaza (tarjeta y select) | `${p.tarifaHora}/h` — **sin** forzar decimales | `$3.5/h`, `$4/h` |
| Tarifa en el detalle del ticket | `$${Number(t.tarifaHora \|\| 0).toFixed(2)}/h` | `$3.50/h` |
| Valor cobrado (cerrado) | `$${cobrado.toFixed(2)}` | `$7.00` |
| Valor por cobrar (abierto) | `$${porCobrar.toFixed(2)}` | `$3.50` |
| Resumen: «He pagado» / «Me falta por pagar» | `$${suma.toFixed(2)}` | `$14.00` |
| Toast de cierre | `$${Number(montoTotal \|\| estimado).toFixed(2)}` | `$7.00` |
| Confirmación de cierre | `Valor estimado a cobrar: $${estimado}` (estimado ya con `toFixed(2)`) | `$3.50` |
| Reporte (métricas y tabla) | `$${Number(...).toFixed(2)}` | `$21.00` |
| Resultado IA | `$${r.tarifaHora}/h` (sin decimales forzados) | `$3.5/h` |

Los valores monetarios usan `font-variant-numeric: tabular-nums` en las tarjetas y métricas.

## 5.6 Notificaciones (toasts) y mensajes de error

**Sistema de toasts (`toast(mensaje, tipo)`, app.js:319-334):**

- Contenedor `#toast-root.toast-root`: `position: fixed`, esquina **inferior derecha** (`right: 1rem; bottom: 1rem`), `z-index: 1000`, apilado vertical con separación de 0.5 rem, ancho máximo `min(380px, calc(100vw - 2rem))`, `aria-live="polite"`.
- Cada toast: `div.toast` + tipo, fondo `#11161b`, texto `#eef2ea`, **borde izquierdo de 4 px según el tipo**, sombra fuerte, animación `rise` 0.28 s.
- **Duraciones:** visible **3200 ms**; luego transición de opacidad de **300 ms** y se elimina del DOM a los 3300 ms.
- **Tipos y colores de borde:**

| Tipo | Clase | Borde izquierdo | Uso |
|---|---|---|---|
| `info` | `.toast.info` | `--teal` `#1a8f7c` | Información y avisos |
| `ok` | `.toast.ok` | `--ok` `#2f9a55` | Éxitos |
| `err` | `.toast.err` | `--danger` `#d6452f` | Errores |
| (por defecto) | `.toast` | `--line` `#f2c51a` | No se usa explícitamente |

- Si `#toast-root` no existe, se recurre a `alert(mensaje)`.
- **Los toasts no se pueden cerrar manualmente ni se apilan con límite**; varios a la vez se muestran en columna.

**Catálogo completo de toasts (textos literales):** «Plaza {codigo} lista para abrir ticket», «Plaza {codigo} liberada», «Cobro cancelado», «Ticket cerrado. Cobrado: ${n}», «Cobrado ${n}. Plaza quedará libre al sincronizar (ms-espacios estaba caído).», «Ticket abierto: {codigo} · plaza {X}», «Vehículo registrado», «Placa {placa} cargada», «Usuario creado», «Usuario actualizado», «Contraseña actualizada», «Usuario activado», «Usuario desactivado», «Perfil actualizado», «Contraseña cambiada», «Historial Resilience limpiado», «Estado actualizado», «Prueba Resilience completada», «Stress ×5 completado», «Solo ADMIN puede usar Resilience», «No tienes acceso a esta pantalla con tu perfil», «Sincronizando plazas pendientes…», «Pagos verificados: plazas liberadas y disponibles», «ms-espacios inició, pero aún no sincroniza. Usa «Sincronizar plazas» en Tickets.», «Indica fecha desde y hasta», «La fecha hasta no puede ser anterior a desde», «Estás estacionado en la plaza {X}», «El propietario es obligatorio», «La placa es obligatoria», «Placa inválida. Ej: ABC-123», «Elige una plaza libre», «La plaza {X} está ocupada. Elige otra libre.», «Ya tienes un ticket abierto para {placa} ({codigo})», «Esa plaza ya tiene ticket abierto ({codigo} · {placa})», más el `mensaje` que devuelva el backend al sincronizar.

**Traducción de errores HTTP a texto claro — `mensajeAmigable(status, raw, path)` (app.js:279-317):**

1. Primero intenta extraer un mensaje del cuerpo JSON, en este orden de claves: `message`, `detail`, `error`. Si el cuerpo no es JSON, usa el texto crudo recortado.
2. Si ese mensaje no está vacío, **no empieza por `{`** y tiene **menos de 400 caracteres**, se devuelve tal cual (así se propagan los mensajes de negocio del backend).
3. En caso contrario se aplican mensajes genéricos por código:

| Estado | Condición | Mensaje |
|---|---|---|
| 409 | ruta contiene `/ia/asignar` o `/tickets` | «No se pudo asignar: esa placa ya tiene un ticket abierto, o la plaza está ocupada. Ve a Tickets → Cerrar / cobrar e intenta de nuevo.» |
| 409 | otras rutas | «Conflicto: el recurso ya está en uso. Revisa tickets abiertos o plazas ocupadas.» |
| 404 | — | «No se encontró lo solicitado (plaza, ticket o vehículo). Revisa los datos e intenta de nuevo.» |
| 400 | — | el mensaje extraído o «Datos incompletos o inválidos. Revisa el formulario.» |
| 502 / 503 | ruta contiene `/ia/` | «No se pudo completar la asignación con IA. Puede que la placa ya tenga un ticket abierto o que un servicio no responda. Cierra tickets abiertos en la pestaña Tickets e intenta otra vez.» |
| 502 / 503 | otras rutas | «Un servicio no está disponible temporalmente. Espera unos segundos e intenta de nuevo.» |
| 401 | — | «Tu sesión expiró. Vuelve a iniciar sesión.» |
| 403 | — | «No tienes permiso con tu rol actual. Revisa el perfil: cliente / operador / admin tienen pantallas distintas.» |
| otro | — | el mensaje extraído o «Error {status}. Intenta de nuevo.» |

**Mensajes inline (no toast):** `#login-error` y `#register-error` (`.error`, fondo rojizo con borde rojo) y `#register-ok` (`.hint` gris).

**Estados vacíos (`.empty-state`, tarjeta blanca con borde izquierdo gris):** «No hay vehículos registrados.», «No tienes tickets propios para mostrar. Abre uno con tu placa.», «No hay tickets [abiertos/cerrados] para mostrar.», «Sin usuarios», «Sin pruebas aún.», «Ninguna plaza ocupada ahora» (fila de tabla), «Cargando…» (fila de tabla en mayúsculas/`.empty-row`).

## 5.7 Validaciones del cliente

**`validarPlacaFront(placa)` (app.js:1334-1341)** — se aplica en el alta de vehículo, la apertura de ticket y la asignación por IA:

1. Normaliza: `String(placa).trim().toUpperCase().replace(/\s+/g, "")` (elimina **todos** los espacios).
2. Si queda vacía → `{ok:false, msg:"La placa es obligatoria"}`.
3. Si `length < 4` o `length > 12`, o no cumple `^[A-Z0-9]{2,8}(-?[A-Z0-9]{1,4})?$` → `{ok:false, msg:"Placa inválida. Ej: ABC-123"}`.
4. Si cumple → `{ok:true, placa: <normalizada>}`.

Es **idéntica** a la del backend (`TicketService.validarPlaca` / `VehiculoService.crear`), salvo el texto del mensaje (el backend añade «o ABC123 (4 a 12 caracteres)»).

**Otras validaciones:** `required` del HTML (usuario, contraseña, placa, propietario, campos de registro, campos de perfil, fecha desde/hasta, id de usuario en modales), `minlength="4"` en usuario, `minlength="6"` en contraseñas, `type="email"` en emails, `required` en los selects de rol y plaza, y las comprobaciones en JS descritas en cada pantalla (propietario obligatorio, plaza elegida, plaza no ocupada, sin ticket duplicado por placa ni por plaza, fechas del reporte).

## 5.8 Estados de carga

No hay *spinners* ni *skeletons*. Los indicadores son textuales y de tres tipos:

1. **Placeholder en contenedores:** `#ia-result` → «Ollama está eligiendo y asignando plaza…» o «Consultando Ollama…»; `#resilience-status` → «Ejecutando {n} llamada(s) a `/api/tickets/ocupacion`…»; `#reporte-ia-box` → «Generando reporte del {desde} al {hasta} con Ollama…».
2. **Fila «Cargando…»** en la tabla de usuarios (`<tr><td colspan="6" class="empty-row">Cargando…</td></tr>`) antes de cada `loadUsuarios()`.
3. **Contenido previo sustituido:** las listas y la cuadrícula se reemplazan **completamente** cuando llega la respuesta (`innerHTML = ...`), por lo que durante la carga se mantiene el contenido anterior.

Los botones **no** se deshabilitan durante las operaciones: no hay bloqueo anti doble clic. El `body` no tiene overlay de carga.

## 5.9 Ciclo de arranque y recargas

**Arranque (app.js:1853-1859):** restaura la sesión; si el token es válido muestra la app, si no limpia y muestra el login. `initFechasReporte()` se ejecuta incondicionalmente (app.js:1778), antes del bloque de arranque.

**`showApp()` (app.js:457-487)** hace, en orden: mostrar la vista de app, escribir `#session-user`, `aplicarPermisosUi()`, `renderTokenPanel()` y luego, en una IIFE asíncrona: `await Promise.all([loadTickets(), (loadVehiculos() si puede ver la pestaña)])`, después `await loadPlazas()` y, si el usuario tiene un ticket abierto, un toast informativo. Los errores se registran en consola (`console.error`) **sin** toast, por lo que un fallo de carga inicial es silencioso para el usuario.

**`irATab(id)` (app.js:355-370)** recarga automáticamente: `usuarios` → `loadUsuarios()`, `perfil` → `loadPerfil()`, `resilience` → `refreshEspaciosDemoStatus()`. Las demás pestañas usan datos ya en caché.

**Cadenas de recarga tras operaciones:**

| Operación | Recargas |
|---|---|
| Abrir ticket | `loadTickets()` + `loadPlazas()` |
| Cerrar ticket | `loadTickets()` + `loadPlazas()` (esta última con `.catch(() => {})`) |
| Liberar plaza | `loadPlazas()` |
| Registrar vehículo | `loadVehiculos()` |
| Asignar con IA | `loadTickets()` + `loadPlazas()` |
| Sincronizar liberaciones | `loadTickets()` + `loadPlazas()` (ambas con `.catch(() => {})`) |
| Crear/editar/activar/desactivar usuario | `loadUsuarios()` |
| Guardar perfil | `loadPerfil()` |
| Iniciar ms-espacios | `reconciliar-plazas` (ADMIN) + `sincronizarLiberacionesPendientes` + `refreshEspaciosDemoStatus()` |

**Recargas cruzadas:** `loadPlazas()` termina llamando a `renderTickets()` si hay tickets en caché, y `loadVehiculos()` hace lo mismo; `loadTickets()` termina llamando a `informarPlazaCliente()`.

---

# 6. FLUJOS

## 6.1 Flujo de LOGIN

1. El usuario abre la app. Si en `localStorage["urbanpark.session"]` hay un token con `expiresAt` futuro, entra directamente a la app; si no, se muestra `#view-login` con **`cliente1` / `cliente123` precargados**.
2. Opcionalmente pulsa «Ver perfiles y permisos» y/o un botón «Usar» para autocompletar credenciales.
3. Pulsa «Iniciar sesión» → `login(username.trim(), password)`:
   1. Construye `grant_type=password&client_id=urbanpark-web&username=...&password=...`.
   2. `POST` a la ruta relativa `/realms/urbanpark/protocol/openid-connect/token` con `Content-Type: application/x-www-form-urlencoded`; si la petición **lanza excepción** (no si devuelve un error HTTP), reintenta contra `http://localhost:8091/realms/urbanpark/protocol/openid-connect/token`.
   3. Si no hubo respuesta en ninguno de los dos → error «No se pudo conectar con Keycloak. Usa http://localhost — {detalle}».
   4. Si la respuesta no es 2xx → «Login fallido ({status}): {cuerpo}» (p. ej. 401 por credenciales inválidas).
   5. Si es correcta: guarda `access_token`, `refresh_token`, `expiresAt = ahora + expires_in*1000`, decodifica el payload y persiste la sesión.
   6. `showApp()`: cambia de vista, escribe `usuario · ROL` en la píldora, aplica permisos por rol (ocultando pestañas y ajustando los títulos «Mis tickets»/«Mis vehículos» y sus avisos), pinta la pestaña JWT y **carga tickets (y vehículos) y plazas**.
   7. Si el usuario tiene un ticket abierto, aparece el toast «Estás estacionado en la plaza {X}».
4. En cada acción posterior, `api()` refresca el token si faltan menos de 15 s para expirar.
5. «Salir»: borra la sesión local y vuelve al login (sin revocación en el servidor).

**Cuentas demo (keycloak/urbanpark-realm.json):** `cliente1`/`cliente123` (Ana Cliente, `cliente1@urbanpark.local`), `cliente2`/`cliente123` (Pedro Cliente), `cliente3`/`cliente123` (María Cliente), `operador1`/`operador123` (Luis Operador), `admin1`/`admin123` (Admin Urban, roles `ADMIN` **y** `OPERADOR`).

## 6.2 Flujo de REGISTRO

1. En el login, «Crear cuenta» → se oculta el login y se muestra `#register-form` (todos los avisos de error/éxito se ocultan).
2. El usuario rellena: Usuario (≥4), Email, Nombre, Apellido, Contraseña (≥6) y Perfil (`CLIENTE` / `OPERADOR` / `ADMIN`).
3. «Registrarme» → `POST /api/auth/register` con `{username, email, firstName, lastName, password, role}` (JSON, sin token; los cuatro campos de texto con `trim`, `role` por defecto `CLIENTE`).
4. **Éxito (201):** `#register-ok` muestra «Cuenta creada como {role}. Ya puedes iniciar sesión.»; se copia el usuario al campo del login, se vacía la contraseña y se limpia el formulario de registro. **El usuario permanece en el formulario de registro** (no hay redirección automática al login).
5. **Error:** se muestra `#register-error` con el mensaje amigable (validaciones del backend: usuario de 4 a 50 caracteres, email válido, rol permitido, contraseña de 6 a 100; conflicto si el usuario ya existe en Keycloak).
6. «Volver al login» revierte la vista y limpia los avisos.

## 6.3 Flujo de ALTA / EDICIÓN DE VEHÍCULO

**Alta (único flujo disponible en la interfaz):**

1. Pestaña «Vehículos» («Mis vehículos» para CLIENTE). Al entrar se cargan los vehículos vía `GET /api/vehiculos` (los del usuario si es CLIENTE).
2. El usuario rellena el formulario en línea: **Placa** (obligatoria), **Tipo** (`AUTO` por defecto / `MOTO` / `CAMIONETA`), **Marca**, **Color** y **Propietario** (obligatorio).
3. «Registrar":
   1. `validarPlacaFront` → normaliza (trim, mayúsculas, sin espacios) y valida 4–12 caracteres con el patrón; si falla → toast de error y **fin**.
   2. Comprueba que `propietario` no esté vacío → «El propietario es obligatorio».
   3. `POST /api/vehiculos` con `{placa (normalizada), tipo, marca, color, propietario}` (marca y color pueden ir vacíos).
   4. El backend valida de nuevo, comprueba duplicado de placa (409 «La placa ya está registrada»), normaliza el tipo a mayúsculas y, si el usuario es CLIENTE, le asigna la propiedad.
   5. `reset()` del formulario → toast «Vehículo registrado» → `loadVehiculos()` → la lista se repinta y, si el usuario es CLIENTE, **se reconstruye el select de placas del formulario de tickets** (`llenarPlacas`).
4. El vehículo aparece en la lista con `placa · tipo · marca color` y el propietario debajo, con el botón «Usar placa» que lo lleva a Tickets con la placa cargada.

**Edición / borrado:** **no existen en la interfaz**. El backend expone `PUT /api/vehiculos/{id}` y `DELETE /api/vehiculos/{id}` solo para OPERADOR/ADMIN (el CLIENTE recibe 403 «El cliente no puede eliminar vehículos; contacta al operador»), y `GET /api/vehiculos/{id}` y `GET /api/vehiculos/placa/{placa}`. Si la reimplementación en MAUI quiere mantener paridad funcional, debe **omitir** estas acciones o implementarlas solo como mejora opcional.

## 6.4 Flujo de APERTURA DE TICKET

**Vía A — desde la pestaña Plazas («Usar esta plaza»):**

1. En «Plazas», el usuario pulsa «Usar esta plaza» en una plaza libre.
2. La app cambia a la pestaña Tickets, selecciona ese código en `#select-plaza`, muestra el toast «Plaza {codigo} lista para abrir ticket» y pone el foco en el campo de placa.
3. El usuario elige/escribe la placa (CLIENTE: solo de un select con sus vehículos) y pulsa «Abrir ticket».

**Vía B — directa en la pestaña Tickets:**

1. El usuario elige la placa (datalist de todas las placas para OPERADOR/ADMIN; select propio para CLIENTE) y una plaza libre del select (cada opción muestra `codigo · TIPO · zona · $tarifa/h`).
2. «Abrir ticket»:
   1. `validarPlacaFront` (obligatoria y con formato).
   2. Plaza elegida no vacía → «Elige una plaza libre».
   3. La plaza no debe figurar ocupada en `plazasCache`.
   4. No debe existir ya un ticket ABIERTO con esa placa en `ticketsCache`.
   5. No debe existir ya un ticket ABIERTO en esa plaza.
   6. `resolverEspacioId(plaza)`: si es numérico lo usa tal cual; si es un código, lo busca en la caché (o pide `GET /api/espacios`) y **rechaza** si no existe o si está ocupada.
   7. `POST /api/tickets/abrir` con `{placa, espacioId}`. El backend: valida la plaza, comprueba duplicados (409), manda `POST /api/espacios/{id}/ocupar` a ms-espacios (409 si ya estaba ocupada) y crea el ticket `TK-XXXXXXXX` en estado `ABIERTO` con `entrada = ahora` y `tarifaHora` copiada de la plaza.
   8. La app limpia el formulario, reconstruye el select de placas si es CLIENTE, muestra `Ticket abierto: {codigo} · plaza {espacioCodigo}`, y recarga tickets y plazas.
3. La plaza queda marcada como ocupada en Plazas (badge rojo «OCUPADA»), y si es el ticket del propio cliente, su tarjeta se resalta en amarillo con «TU PLAZA».

**Vía C — con IA:** pestaña IA → «Asignar con Ollama» (ver §6.5), que internamente ejecuta el mismo `POST /api/tickets/abrir`.

**Efectos visibles:** la caja de resumen del cliente («Tickets usados», «Me falta por pagar») se actualiza con el estimado en vivo; el aviso «Estás estacionado en la plaza {X}» aparece en la pestaña Plazas.

## 6.5 Flujo de CIERRE DE TICKET (con cálculo de cobro)

1. En «Tickets», el usuario localiza una fila con estado `ABIERTO` (aparecen primero, ordenadas por ingreso descendente). El detalle muestra «Ingreso: {fecha}» y «Valor por cobrar (ahora): ${estimado} · se actualiza al cerrar · ${tarifa}/h».
2. Pulsa «Cerrar / cobrar» → aparece el modal de confirmación con el título «Cerrar y cobrar» y el mensaje `¿Cerrar el ticket {codigo} de {placa}? Valor estimado a cobrar: ${estimado}`.
3. Si cancela → toast «Cobro cancelado» (nada se envía al servidor).
4. Si confirma → `POST /api/tickets/{id}/cerrar` (sin cuerpo). El backend:
   1. Busca el ticket (404 «Ticket no encontrado») y verifica propiedad si es CLIENTE (403 «Solo puedes ver o cerrar tus propios tickets»).
   2. Si ya estaba CERRADO → 409 «El ticket ya está cerrado».
   3. Intenta liberar la plaza en ms-espacios:
      - Éxito → la plaza queda libre.
      - Error de indisponibilidad (status < 0, 500, 502, 503, 504) → `liberacionPendiente = true`; **el cobro se realiza igual**.
      - 404 → la plaza no existe: nada que liberar, no se marca pendiente.
      - Otros errores → se propaga el error mapeado.
   4. Fija `salida = ahora`, `estado = "CERRADO"` y calcula el importe.
5. La app muestra el toast «Ticket cerrado. Cobrado: ${montoTotal}» (`ok`) o, si `liberacionPendiente`, «Cobrado ${monto}. Plaza quedará libre al sincronizar (ms-espacios estaba caído).» (`info`), y recarga tickets y plazas.
6. Si quedó pendiente, aparece el aviso `#tickets-sync-hint` («{n} pago(s) cobrado(s) con plaza aún por liberar…») y el badge «Plaza pendiente sync» en la fila. El usuario puede pulsar «Sincronizar plazas» cuando ms-espacios vuelva.

### Fórmula EXACTA del cobro

Idéntica en el cliente (`calcularMonto`, app.js:772-780) y en el servidor (`TicketService.cerrar`, ms-tickets:133-136):

```
tarifa    = Number(tarifaHora) || 0
fin       = salida ? salida : ahora          // para el estimado en vivo se usa "ahora"
minutos   = max(1, floor((fin - ini) / 60000))
horas     = ceil(minutos / 60)
montoTotal = round(horas * tarifa * 100) / 100      // 2 decimales
```

Reglas: **mínimo 1 minuto** facturable; las horas se redondean **hacia arriba**; el importe se redondea a **2 decimales**; si `tarifaHora` es nulo o no numérico se usa 0; si las fechas son inválidas el cliente devuelve 0. `tarifaHora` proviene de la plaza (`Espacio.tarifaHora`) y **se congela** en el ticket al abrirlo, por lo que cambios posteriores de tarifa no afectan a tickets ya abiertos.

Ejemplos con la tabla de tarifas real: 30 min con tarifa 3.5 → 1 h × 3.5 = **$3.50**; 61 min con tarifa 1.5 → 2 h × 1.5 = **$3.00**; 3 h 00 min 01 s con tarifa 4.0 → 4 h × 4.0 = **$16.00**.

### Tarifas reales por plaza (semilla de ms-espacios, `EspacioService.seed`)

| Código | Zona | Tipo | Tarifa/hora | Nivel | Descripción |
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

**20 plazas** en 4 zonas, niveles 1, 2 y -1, tarifas de 1.5 a 4.2 por hora. El seed es idempotente: solo crea las plazas cuyo código no exista.

### Vehículos precargados (semilla de ms-vehiculos)

`ABC-123` AUTO Toyota Gris — Ana Cliente (`cliente1`); `XYZ-789` MOTO Yamaha Rojo — Luis Operador (`operador1`); `DEF-456` AUTO Chevrolet Azul — Pedro Cliente (`cliente2`); `GHI-789` CAMIONETA Ford Blanco — María Cliente (`cliente3`).

## 6.6 Flujo de RECOMENDACIÓN / ASIGNACIÓN CON IA

1. Pestaña «IA» (CLIENTE o ADMIN; **el OPERADOR no tiene la pestaña**).
2. (Opcional) «Estado Ollama» → `GET /api/ia/ollama` → línea `{mensaje} · modelo {modelo} · {baseUrl}`.
3. El usuario rellena Placa, Tipo de vehículo (`AUTO` / `MOTO` / `CAMIONETA` / `DISCAPACIDAD`) y Preferencia (texto libre, p. ej. «cerca», «barato»).

**A. Solo recomendar** (no crea ticket):

1. `POST /api/ia/recomendar` con `{tipoVehiculo, preferencia}`; la tarjeta muestra «Consultando Ollama…».
2. El backend pide a ms-espacios las plazas **libres** del tipo indicado (`GET /api/espacios/disponibles?tipo=`), obtiene la ocupación por zona, construye un prompt con las plazas en JSON y pide a Ollama (`llama3.2:3b`, temperatura 0.3, `num_predict` 200, formato JSON) que devuelva `{"codigo":"...","motivo":"..."}`. Reglas del prompt: el código debe existir entre las plazas libres, se prioriza menor ocupación de zona y luego tarifa y preferencia, y no se inventan plazas.
3. Si Ollama falla o devuelve un código inválido → **fallback heurístico local**: ordena las plazas por porcentaje de ocupación de su zona (50 por defecto si no hay dato) → tarifa ascendente → preferencia (si contiene «cerca», «entrada» o «rapido» ordena por `|nivel|`; si contiene «barato» o «econom» ordena por tarifa × 10), y devuelve `proveedorIa: "heuristica-local"` con el motivo «Se usó asignación automática local (Ollama no respondió bien): {detalle}» y la explicación «Se eligió {codigo} en {zona} por menor ocupación / tarifa.».
4. La tarjeta muestra «### Sugerencia: {espacioCodigo} · {zonaRecomendada}», el id de la plaza, `$tarifa/h`, el nivel, el motivo, la explicación en cursiva, el proveedor y hasta 3 alternativas.
5. Si no hay plazas libres del tipo pedido → error 404 «No hay plazas LIBRES para vehículos tipo {TIPO}. Cierra algún ticket abierto o elige otro tipo.» mostrado en la tarjeta con el bloque rojo («No se pudo asignar» + consejo).

**B. Asignar con Ollama** (crea el ticket de verdad):

1. Valida la placa con `validarPlacaFront`; si falla, toast y fin.
2. La tarjeta muestra «Ollama está eligiendo y asignando plaza…» y se envía `POST /api/ia/asignar` con `{placa, tipoVehiculo, preferencia}`.
3. El backend selecciona la plaza con la misma lógica del punto A y luego llama a ms-tickets `POST /api/tickets/abrir` con la placa y el id de la plaza elegida (así que valen los mismos conflictos: placa ya con ticket abierto, plaza ocupada).
4. Éxito: la tarjeta muestra «### Asignado: {espacioCodigo} · {zona}», «Placa: {placa} · Ticket: {ticketCodigo} (ID {ticketId})», «Plaza ID: {espacioId} · ${tarifaHora}/h · Nivel {nivel}», el motivo, la explicación en cursiva y el proveedor; se recargan tickets y plazas.
5. Error: la tarjeta se pone en rojo con «No se pudo asignar», el mensaje (p. ej. «La placa ABC-123 ya tiene un ticket ABIERTO. Ve a Tickets y pulsa «Cerrar / cobrar» antes de asignar otra plaza.») y el consejo «Consejo: en Tickets cierra el ticket abierto de esa placa, o usa otra placa.»

**Tiempos y límites de la IA:** timeout de Ollama **90 s**; timeout de red en nginx para `/api/ia` **180 s** (conexión 10 s); `num_predict` 200 para asignación y 350 para el texto del reporte; temperatura 0.3; modelo por defecto `llama3.2:3b` en `http://localhost:11434`.

## 6.7 Flujo de GESTIÓN DE USUARIOS (ADMIN)

1. El ADMIN entra en la pestaña «Usuarios»; automáticamente `GET /api/admin/users` y la tabla se pinta (columna «Estado» como píldora Activo/Inactivo).
2. **Crear:** rellena el formulario (Usuario, Email, Nombre, Apellido, Contraseña, Perfil) y pulsa «Crear» → `POST /api/admin/users` → toast «Usuario creado» → limpieza del formulario → recarga de la tabla. Errores (usuario duplicado, email inválido, contraseña corta) → toast rojo con el mensaje del backend.
3. **Editar:** «Editar» en una fila → modal «Editar · {username}» con Nombre, Apellido, Email, Perfil y la casilla «Activo» precargados (el perfil se sitúa en el rol principal del usuario). «Guardar» hace **dos llamadas**: `PUT /api/admin/users/{id}` con `{email, firstName, lastName, enabled}` y luego `PUT /api/admin/users/{id}/roles` con `{role}`. Éxito → toast «Usuario actualizado», cierre del modal y recarga. ⚠️ Al guardar se **reemplazan** los roles por el único rol seleccionado, así que un ADMIN+OPERADOR queda solo con el rol elegido.
4. **Resetear contraseña:** «Password» → modal «Reset contraseña» con la etiqueta «Usuario: {username}» y el campo «Nueva contraseña» (≥6) → `PUT /api/admin/users/{id}/password` con `{password}` → toast «Contraseña actualizada» y cierre.
5. **Activar / desactivar:** botón dinámico («Desactivar» si está activo, «Activar» si no) → confirmación («¿Desactivar este usuario?» / «¿Activar este usuario?») → `PUT /api/admin/users/{id}` con los datos actuales y `enabled` invertido → toast «Usuario activado»/«Usuario desactivado» → recarga.
6. Todo el módulo está protegido dos veces: `@PreAuthorize("hasRole('ADMIN')")` en la clase y `/api/admin/**` → `hasRole("ADMIN")` en la seguridad de ms-auth. Un usuario no ADMIN que intente la ruta recibe 403 («No tienes permiso con tu rol actual…» si pasa por `api()`).
7. El filtrado por rol **no** afecta a esta pantalla: el ADMIN ve todos los usuarios del realm.

## 6.8 Flujo de la pestaña «Resilience» (solo ADMIN)

1. El ADMIN entra en «Resilience». La pestaña no está visible para CLIENTE ni OPERADOR. Al entrar se consulta el estado del contenedor (`GET /api/tickets/demo/espacios/status`) y la píldora muestra `ms-espacios: UP` o `ms-espacios: DOWN` (o `estado: ?` si la consulta falla).
2. La píldora de rol «Solo ADMIN» y el texto explicativo describen la demo: ms-tickets consulta la ocupación de ms-espacios vía Feign; si falla, Resilience4j reintenta y activa el fallback.
3. **Historial en reposo:** estadísticas `OK 0`, `Fallback / error 0`, `Total pruebas 0`; panel de estado «Pulsa **Probar 1 llamada** para ver Circuit Breaker + Retry en acción.»; JSON `—`; lista «Sin pruebas aún.».
4. **«Probar 1 llamada»** ejecuta **1** petición a `GET /api/tickets/ocupacion`, cronometrada:
   - Si la respuesta **no** trae `fallback` → contador `OK` +1, etiqueta «Live ms-espacios»; el panel muestra «Circuit Breaker + Retry OK», «Ocupación live: total {} · ocupados {} · libres {}» y `{resilience} · origen: {origen} · {ms} ms` (esperado: `Circuit Breaker + Retry OK · origen: ms-espacios (live)`); el JSON muestra la respuesta completa.
   - Si trae `fallback: true` → contador `Fallback / error` +1, etiqueta «Fallback Resilience4j»; el panel muestra «Fallback activo» y el mensaje («Resilience4j activó fallback: …»), con `Origen: ms-tickets fallback`.
   - Si la petición lanza error (por ejemplo 403 si no fuera ADMIN) → contador de fallos +1, etiqueta de error, panel rojo «Error en la prueba».
   - Al terminar: toast «Prueba Resilience completada» (`ok`).
   - Cada intento añade una entrada al **historial** (arriba del todo) con número correlativo, etiqueta, badge (OK/FALLBACK/ERROR), hora `es-EC` y duración en ms.
5. **«Stress ×5»** repite ese bloque **5 veces secuencialmente** (con `await` en cada iteración) y termina con el toast «Stress ×5 completado» (`ok`). El panel solo refleja el **último** intento, pero el historial y los contadores acumulan los cinco.
6. **«Limpiar»** vacía el historial, pone ambos contadores a 0, repinta el panel a su estado de reposo y muestra el toast «Historial Resilience limpiado» (`info`).
7. **Demostración de caída del servicio:**
   1. «Detener ms-espacios» → confirmación («¿Detener el contenedor ms-espacios? Luego prueba Resilience para ver el fallback.») → `POST /api/tickets/demo/espacios/stop`. El backend ejecuta `docker stop` sobre el contenedor cuyo nombre contiene `ms-espacios` (timeout de 45 s) y devuelve el mensaje «ms-espacios detenido. Prueba ahora Resilience (debería activar fallback).»; la píldora pasa a `ms-espacios: DOWN`.
   2. «Probar 1 llamada» ahora activa el **fallback**: Feign falla, Resilience4j reintenta hasta 3 veces con retroceso exponencial (500 ms, 1 s, 2 s) y el circuit breaker abre tras superar el 50 % de fallos en una ventana de 10 llamadas, permaneciendo abierto 10 s; la respuesta es `{fallback: true, origen: "ms-tickets fallback", mensaje: "Resilience4j activó fallback: …", total: 0, ocupados: 0, libres: 0}`.
   3. «Iniciar ms-espacios» → `POST /api/tickets/demo/espacios/start` → toast con el mensaje «ms-espacios iniciado. Espera unos segundos y prueba de nuevo.» → se actualiza la píldora → toast «Sincronizando plazas pendientes…» → **espera 8 s** → si es ADMIN, `POST /api/tickets/reconciliar-plazas` (alinea plazas con tickets abiertos: ocupa las que tienen ticket, libera las que no, cierra duplicados) → `POST /api/tickets/sincronizar-liberaciones` (libera las plazas de los tickets que se cobraron mientras el servicio estaba caído) → si `liberadas > 0`, toast «Pagos verificados: plazas liberadas y disponibles» → si algo falla, «ms-espacios inició, pero aún no sincroniza. Usa «Sincronizar plazas» en Tickets.» → se vuelve a consultar el estado de la píldora.
   4. «Probar 1 llamada» de nuevo devuelve el camino OK (live), demostrando la recuperación.
8. **«Sincronizar plazas» (en Tickets)** permite hacer el paso de liberación manualmente en cualquier momento, sin detener/iniciar el contenedor.

---

# 7. ANEXOS

## 7.1 Riesgos y consideraciones de fidelidad para la reimplementación en MAUI

1. **Token**: `localStorage` no existe en MAUI. Equivalente recomendado: `SecureStorage` (access + refresh + `expiresAt`) o preferencias protegidas. Mantener el margen de refresco de **15 s** y el descarte de sesión al arrancar si `expiresAt` ya pasó.
2. **El refresco no tiene fallback de URL** (solo el login lo tiene): si se replica, mantener esa asimetría o mejorarla conscientemente.
3. **`expires_in` por defecto 300 s** (5 min) en Keycloak: en MAUI conviene refrescar proactivamente en cada llamada, igual que hace `api()`.
4. **Fechas:** el cliente usa cadenas ISO sin zona (`LocalDateTime`) y `toLocaleString("es-EC")` para mostrarlas; en MAUI usar `CultureInfo("es-EC")` con formato `dd/MM/yyyy HH:mm:ss`. Cuidado con `toISOString().slice(0,10)` (UTC) en los inputs de fecha del reporte.
5. **Moneda:** siempre `$` + 2 decimales con `toFixed(2)` en importes calculados; las tarifas de plaza se muestran **sin** ceros forzados (`$3.5/h`). En MAUI, `ToString("F2", CultureInfo.InvariantCulture)` y `'$' + tarifa` respectivamente, con punto como separador decimal.
6. **Fórmula de cobro:** replicar exactamente `max(1 min)` + `ceil` de horas + redondeo a 2 decimales; cualquier desviación produce diferencias de facturación respecto al backend.
7. **Sin paginación:** usar virtualización de listas; cargar siempre la colección completa.
8. **Cachés y recargas cruzadas:** reproducir que plazas ↔ tickets ↔ vehículos se refrescan entre sí (una plaza liberada debe repintar los tickets y viceversa).
9. **Toasts:** 3200 ms de permanencia, 300 ms de desvanecimiento, tres variantes de color (`info`/`ok`/`err`), esquina inferior derecha, `aria-live="polite"`.
10. **Modal de confirmación genérico:** reutilizable, con título y mensaje, botones «Cancelar» / «Confirmar».
11. **Rol único jerárquico (ADMIN > OPERADOR > CLIENTE):** implementarlo como una función y usarla tanto para el menú como para las guardias de seguridad locales; recordar que `admin1` tiene también el rol OPERADOR.
12. **`PUT /api/admin/users/{id}/roles` reemplaza roles**: advertencia en la interfaz de MAUI si el usuario tiene más de un rol.
13. **Escena decorativa:** el carro animado, el letrero ACCESO→SEGURO y el código de plaza en `.stall-mark` son elementos visuales distintivos; en MAUI se pueden reproducir con animaciones (`Animation`/`TranslateTo`/`RotateY`) o sustituir por una ilustración estática, respetando la paleta y las dos vistas del vehículo.
14. **Errores silenciosos al arrancar:** en la versión web, un fallo de carga inicial solo se registra en consola; en MAUI conviene mostrar un aviso al usuario.
15. **Sin bloqueo anti doble clic** y sin deshabilitar botones durante operaciones: conviene añadir un estado «ocupado» en MAUI para evitar dobles aperturas/cierres.
16. **`data-roles`**: mecanismo genérico de ocultado por rol presente en el código pero sin uso en el HTML; si se traslada, mantenerlo como utilidad.
17. **Endpoints existentes no usados por el frontend** (útiles si se amplía la app): `GET /api/tickets/abiertos`, `GET /api/tickets/{id}`, `GET /api/espacios/{id}`, `GET /api/espacios/disponibles`, `POST /api/espacios`, `PUT /api/espacios/{id}`, `POST /api/espacios/{id}/ocupar`, `GET /api/vehiculos/{id}`, `GET /api/vehiculos/placa/{placa}`, `PUT /api/vehiculos/{id}`, `DELETE /api/vehiculos/{id}`, `GET /api/admin/users/{id}`, `GET /api/ia/salud`.

## 7.2 Inventario de identificadores (`id`) de la interfaz

**Vistas y contenedores:** `view-login`, `view-app`, `tab-plazas`, `tab-vehiculos`, `tab-tickets`, `tab-ia`, `tab-reporte`, `tab-resilience`, `tab-usuarios`, `tab-perfil`, `tab-token`, `toast-root`, `modal-confirm`, `modal-user`, `modal-password`.

**Login:** `login-form`, `username`, `password`, `btn-perfiles`, `btn-mostrar-registro`, `login-error`, `register-form`, `btn-cancelar-registro`, `register-error`, `register-ok`, `panel-perfiles`.

**Modales:** `modal-title`, `modal-msg`, `modal-cancel`, `modal-ok`, `modal-user-title`, `form-user-edit`, `edit-user-id`, `edit-firstName`, `edit-lastName`, `edit-email`, `edit-role`, `edit-enabled`, `modal-user-cancel`, `form-user-password`, `pwd-user-id`, `pwd-user-label`, `pwd-new`, `modal-password-cancel`.

**Cabecera de app:** `session-user`, `btn-logout`.

**Plazas:** `btn-refresh-plazas`, `ocupacion-box`, `plaza-cliente-info`, `plazas-grid`, `plaza-mia` (dinámico).

**Vehículos:** `vehiculos-title`, `vehiculos-scope-hint`, `form-vehiculo`, `vehiculos-list`.

**Tickets:** `tickets-title`, `btn-sync-liberaciones`, `tickets-scope-hint`, `tickets-sync-hint`, `tickets-resumen`, `form-ticket`, `input-placa-ticket`, `lista-placas`, `select-plaza`, `filtros-tickets`, `tickets-list`.

**IA:** `btn-ollama-status`, `ollama-status`, `form-ia`, `btn-recomendar`, `ia-result`.

**Reporte:** `btn-reporte-ia`, `form-reporte`, `reporte-desde`, `reporte-hasta`, `btn-reporte-hoy`, `btn-reporte-7d`, `reporte-ia-box`.

**Resilience:** `btn-resilience-once`, `btn-resilience-stress`, `btn-resilience-clear`, `btn-espacios-stop`, `btn-espacios-start`, `btn-espacios-status`, `espacios-demo-status`, `resilience-stats`, `resilience-status`, `resilience-out`, `resilience-log`.

**Usuarios:** `btn-refresh-usuarios`, `form-usuario-crear`, `usuarios-table`, `usuarios-tbody`.

**Perfil:** `btn-refresh-perfil`, `perfil-username`, `form-perfil`, `perfil-firstName`, `perfil-lastName`, `perfil-email`, `form-perfil-password`.

**JWT:** `token-meta`, `token-raw`.

## 7.3 Clases CSS reutilizables (equivalente a estilos globales en MAUI)

- **Botones:** `.btn` (base), `.btn.primary` (fondo teal, texto blanco, negrita, sombra teal), `.btn.secondary` (fondo `--ink`, texto amarillo), `.btn.ghost` (transparente, texto `--paper`, borde `#4a545e`; hover amarillo), `.btn.ghost.dark` (texto `--ink`, borde `#b7bfb4`, fondo blanco), `.btn.ok` (verde), `.btn.danger` (rojo), `.btn-sm` (padding 0.28/0.55 rem, 0.78 rem). Todos con `cursor: pointer`, `border: none`, `letter-spacing: 0.01em`, hover `translateY(-1px)`, active `translateY(0)`.
- **Tarjetas:** `.panel` (fondo degradado claro, borde superior teal de 4 px, sombra, y un trazo decorativo amarillo de 48×3 px en la esquina superior izquierda con `.panel::after`), `.row` (fila blanca con borde izquierdo teal de 4 px; hover con sombra y `translateX(2px)`; variantes `.ticket-abierto` borde teal y `.ticket-cerrado` borde gris), `.stat` (tarjeta oscura con borde izquierdo amarillo), `.ia-card` (tarjeta oscura con borde izquierdo amarillo de 5 px; `.ia-error` en rojo), `.tr-card` (tarjeta oscura del resumen de tickets), `.empty-state` (blanco con borde gris), `.perfil-form` (oscura con borde blanco translúcido).
- **Etiquetas:** `.badge` (absoluta arriba-derecha, mayúsculas 0.65 rem), `.badge-estado` (en línea, 0.68 rem, mayúsculas) con variantes por fila y `.badge-warn` (ámbar), `.session-pill` (píldora gris con borde), `.chip` (filtro; `.active` amarillo en negrita), `.tipo-icon` con variantes `.tipo-auto`, `.tipo-moto`, `.tipo-camioneta`, `.tipo-discapacidad` y modificador `.sm`.
- **Formularios:** `.stack` (grid con separación 0.95 rem), `.form-inline` (fila con `flex-wrap`), `label` (grid con separación 0.4 rem, 0.88 rem, seminegrita, color `#3a4148`), inputs blancos con borde `#c2cac0`, radio 2 px y foco teal con halo `rgba(26,143,124,0.18)`; variantes oscuras para `.perfil-form`, `.modal-card-form` y `.register-form` (fondo `#0e1318`, texto claro, borde blanco al 12 %), `.check-row` (fila con casilla).
- **Otros:** `.hint` (texto gris 0.8 rem), `.error` (texto rojo sobre fondo rojizo con borde izquierdo), `.code-box` (bloque oscuro monoespaciado con scroll), `.table-wrap` (scroll horizontal con borde), `.reporte-table` (tabla de 0.86 rem con cabecera oscura y mayúsculas), `.perfiles-table` (tabla clara con cabecera oscura amarilla), `.grid-plazas`, `.perfil-grid`, `.filtros`, `.list`, `.modal` y `.modal-card`, `.toast-root` y `.toast`, `.metricas` (grid de métricas con colores por significado: `.m-ocup` rojo, `.m-libre` verde, `.m-money` amarillo, `.m-pending` ámbar), `.resilience-*`, `.reporte-*` (encabezado, sello de estado, secciones, cita, listas, pie).

## 7.4 Locale y textos

- Toda la interfaz está en **español (Ecuador)**, `lang="es"`, con locale `es-EC` en las fechas.
- Se usan signos de interrogación de apertura en las confirmaciones («¿Marcar…?», «¿Cerrar…?», «¿Detener…?», «¿Activar este usuario?») y comillas angulares latinas «» en los textos de ayuda y en los mensajes de error del backend.
- Mayúsculas sostenidas en: `URBAN PARK`, `CLIENTE`/`OPERADOR`/`ADMIN`, estados `ABIERTO`/`CERRADO`, tipos `AUTO`/`MOTO`/`CAMIONETA`/`DISCAPACIDAD`, códigos de plaza (`A-01`) y de ticket (`TK-XXXXXXXX`), códigos de respuesta de los LED (`ACCESO`/`SEGURO`) y el texto de la píldora `Solo ADMIN`.
