# Manual línea a línea — `IaController.cs`

**Archivo:** `UrbanPark.Api/Controladores/IaController.cs` (~898 líneas)  
**Namespace:** `UrbanPark.Api.Controladores`  
**Ruta HTTP:** prefijo `api/ia` → ej. `http://localhost:8090/api/ia/recomendar`

Este documento describe **qué hace cada bloque de código**, con **números de línea** alineados con el archivo en el repositorio (pueden variar ±1 si editas el fuente).

---

## 0. Cómo leer este manual

| Símbolo | Significado |
|---------|-------------|
| **L** | Número de línea en `IaController.cs` |
| **→** | Efecto o resultado |
| **JWT** | Token Bearer del login (`/api/auth/login`) |

La clase mezcla **reglas de negocio en C#** (obligatorias) con **texto del LLM** (opcional, con respaldo).

---

## 1. Encabezado, ruta y dependencias (L1–L38)

| Líneas | Código / idea | Explicación detallada |
|--------|---------------|------------------------|
| **L1** | `using System.Globalization` | Formateo invariante de dinero y fechas en hallazgos y prompts (`0.00`, fechas en reporte). |
| **L2–L6** | `Microsoft.AspNetCore.*`, `Modelos`, `Repositorios`, `Servicios` | Autorización MVC, DTOs, acceso MySQL y servicios de dominio. |
| **L8** | `namespace UrbanPark.Api.Controladores` | Mismo namespace que el resto de controladores REST. |
| **L10–L14** | Comentario XML | Documenta equivalencia con el antiguo **ms-ia** (puerto 8084) y uso de Ollama/DeepSeek con **fallback heurístico**. |
| **L15** | `[Route("api/ia")]` | Todas las acciones públicas cuelgan de `/api/ia/...`. |
| **L16** | `[Authorize]` | Por defecto hace falta JWT; excepción explícita en `salud` (L445). |
| **L17** | `sealed class IaController : ControladorBase` | No se hereda; `ControladorBase` aporta `Ok`, `Conflicto`, `NoEncontrado`, `Username`, `UsuarioId`, etc. |
| **L19–L25** | Campos `readonly` | Inyección de dependencias: espacios, tickets, repos, LLM, balance, log. |
| **L27–L38** | Constructor | Recibe e asigna cada servicio; no hay lógica extra en el ctor. |

**Quién registra esto:** `UrbanPark.Api/Program.cs` (ASP.NET descubre el controlador por convención).

---

## 2. `Recomendar` — sugerir plaza sin ticket (L40–L79)

| Líneas | Qué ocurre |
|--------|------------|
| **L40** | Comentario separador de sección. |
| **L41–L42** | `[HttpPost("recomendar")]` + `[Authorize(Roles = "CLIENTE,ADMIN")]`. OPERADOR → **403**. |
| **L43** | Firma: body `RecomendarRequest` (`tipoVehiculo`, `preferencia`, `placa` opcional). |
| **L45** | Si `tipoVehiculo` vacío → **AUTO**; si no, trim + mayúsculas. |
| **L46** | Si `preferencia` vacía → texto fijo `"sin preferencia"` (sigue pasando por `PrioridadPerfil`). |
| **L48–L50** | `TicketAbiertoAsync(placa)`: si devuelve `IActionResult`, se retorna de inmediato **sin** llamar al LLM. |
| **L52–L58** | Pipeline de filtrado de plazas (ver §8). |
| **L60–L63** | Si el servicio de espacios falla → HTTP de no disponible con mensaje claro. |
| **L65–L66** | Cero candidatas → 404 con texto que sugiere cerrar tickets o cambiar tipo. |
| **L68–L70** | `OcupacionAsync()` opcional: si falla, `ocupacion` queda `null` (el prompt de asignación sigue). |
| **L72** | **`ElegirPlazaAsync`**: corazón IA + heurística (§9). |
| **L73–L74** | Hasta 3 **alternativas** como strings `"CODIGO (Zona)"`, excluyendo la elegida. |
| **L76–L78** | `Ok(RecomendacionResponse)`: zona, id, código, tarifa, nivel, motivo, explicación IA, proveedor, alternativas. |
| **L79** | Cierre del método; **no** abre ticket. |

---

## 3. `Asignar` — misma elección + abrir ticket (L81–L162)

| Líneas | Qué ocurre |
|--------|------------|
| **L81–L83** | POST `asignar`, mismos roles que recomendar. |
| **L86–L87** | `placa` obligatoria; si falta → petición inválida. |
| **L89–L91** | Normaliza placa (upper), tipo (AUTO default), preferencia. |
| **L93–L95** | Misma comprobación de ticket abierto que recomendar. |
| **L97–L111** | **Mismo** pipeline de plazas libres que L52–L66. |
| **L113–L115** | Ocupación opcional. |
| **L117** | `ElegirPlazaAsync` otra vez. |
| **L118–L119** | Lista `orden`: primero la plaza elegida, luego el resto de `libres` (plan B si hay 409). |
| **L121–L124** | Variables para resultado de `AbrirAsync`, último error HTTP. |
| **L125–L141** | **Bucle** por cada candidata: `_tickets.AbrirAsync(placa, espacioId, Username, UsuarioId)`. Si éxito → break. Si mensaje indica conflicto de **esta placa** → return Conflicto inmediato. Si código ≠ 409 → deja de intentar otras. |
| **L143–L154** | Sin éxito → switch por `ultimoCodigo`: 404, 5xx→503, 409, otro. |
| **L156–L157** | Si al final se usó otra plaza que la de la IA, reconstruye `Eleccion` con `MotivoDePerfil` (texto coherente con la plaza real). |
| **L159–L161** | `Ok(AsignacionResponse)`: datos de plaza + ticket + textos IA + `Asignado: true`. |

**Diferencia clave con recomendar:** L125–L154 es la única parte que **persiste** un ticket en MySQL.

---

## 4. Constantes de tiempo de espera IA (L164–L171)

| Líneas | Constante | Valor | Uso |
|--------|-----------|-------|-----|
| **L164–L165** | `SegundosMaximosIa` | **25** | Cancelación al redactar **resumen** del reporte (L372). |
| **L167–L171** | `SegundosMaximosAsignacion` | **12** | Cancelación al pedir JSON de plaza (L505). |

Si se cancela, el usuario **sigue** recibiendo respuesta con texto heurístico o primera plaza ordenada.

---

## 5. `ReporteTickets` — administrador (L173–L394)

### 5.1 Fechas y reconciliación (L173–L199)

| Líneas | Qué ocurre |
|--------|------------|
| **L174–L176** | POST `reporte-tickets`, solo **ADMIN**. |
| **L178–L180** | `desde`/`hasta` desde body o **hoy** si vienen vacíos (`ParsearFecha`, L892–L896). |
| **L182–L183** | `hasta < desde` → error de validación. |
| **L185–L187** | `_tickets.ReconciliarAsync()`: alinea plazas ocupadas con tickets; excepción solo se registra en log. |
| **L189–L199** | `ListarAsync()` todos los tickets; fallo grave → no disponible. |

### 5.2 Filtro por sedes (L201–L230)

| Líneas | Qué ocurre |
|--------|------------|
| **L202** | `sedesPedidas`: IDs > 0, sin duplicados. |
| **L203–L205** | Plazas de esas sedes; si espacios caídos → lista vacía. |
| **L207–L215** | Diccionarios `espacioId → sedeId` y `sedeId → nombre`. |
| **L217–L225** | Con filtro: tickets restringidos a plazas conocidas; nombres de sucursales en respuesta. |
| **L227–L230** | Sin filtro: lista de nombres de todas las sedes conocidas en mapa. |

### 5.3 Periodo, dinero, agregados (L232–L297)

| Líneas | Qué ocurre |
|--------|------------|
| **L232–L233** | Rango **inclusive** de entradas: desde medianoche de `desde` hasta último tick de `hasta`. |
| **L234–L236** | `delPeriodo` por `Entrada`; `abiertosAhora` estado ABIERTO (global); `cerradosPeriodo` dentro del periodo y CERRADO. |
| **L238–L240** | Suma recaudado/descuento cerrados; **por cobrar** en abiertos con `Reglas.CalcularMonto`. |
| **L242–L245** | Conteo tickets por zona en el periodo. |
| **L247–L253** | Conteo por **nombre de sede** resolviendo sede vía `EspacioId`. |
| **L255–L263** | Ocupación actual (total/ocupados/libres/%) vía `_espacios.OcupacionAsync(sedesPedidas)`. |
| **L265–L266** | `cuadran`: ocupados en mapa == plazas distintas con ticket abierto == cantidad tickets abiertos. |
| **L268–L270** | Texto legible del periodo para hallazgos y prompt. |
| **L272–L280** | Detalle de quien está adentro: código plaza, placa, usuario, zona, monto estimado ahora. |
| **L282–L290** | Cobro agregado por **día de salida** (pagado + descuento). |
| **L292–L297** | `porPersona`: agrupa tickets del periodo por etiqueta (`PerfilPersona`, §12). |

### 5.4 Hallazgos en español fijo (L299–L338)

| Líneas | Qué ocurre |
|--------|------------|
| **L300–L303** | Arranca lista `hallazgos` con ocupación actual %. |
| **L305–L308** | Lista de códigos de plaza + placa o mensaje “nadie estacionado”. |
| **L310–L312** | Mensaje si cuadran o no plazas vs tickets. |
| **L314–L318** | Conteos del periodo y dinero cobrado / descuento / pendiente. |
| **L320–L322** | Desglose por zona si hubo movimiento. |
| **L324–L337** | **Hora pico**: cuenta entradas por hora 0–23; distingue una hora vs empate; lista horas con entradas > 0. Importante para el prompt (no inventar rangos). |

### 5.5 Guardar balance y narrativa IA (L340–L394)

| Líneas | Qué ocurre |
|--------|------------|
| **L340–L342** | `usosPlaza`: cuántas veces se usó cada código en el periodo. |
| **L345–L350** | Solo sin filtro de sedes: **`_balance.GuardarAsync`** en MySQL `reporte_balance` + hallazgo informativo. |
| **L352–L355** | Error al guardar → warning log, reporte sigue. |
| **L357–L360** | `ConstruirDatosReporte` (§11): bloque texto enorme con números para el LLM. |
| **L362–L364** | Resumen **respaldo** corto y `proveedor = heuristica-local`. |
| **L368–L385** | Si LLM configurado y `sinIa != true`: prompt reporte, max 700 tokens, **25 s**; `LimpiarMarkdown`; si timeout o error → conserva respaldo. |
| **L387–L393** | Empaqueta `ReporteTicketsResponse` con métricas anidadas, hallazgos, resumen, descuento, sucursales. |

---

## 6. `Chat` y estado (L396–L446)

| Líneas | Qué ocurre |
|--------|------------|
| **L397–L401** | POST `chat`; mensaje vacío → invalida. |
| **L403–L407** | System prompt: asistente Urban Park, español, temas parking/app. |
| **L409–L415** | Historial: roles `assistant`/`system`/`user`; ignora contenido vacío. |
| **L415** | Añade mensaje actual del usuario. |
| **L417–L420** | `ConversarAsync`, hasta 700 tokens, sin JSON → `ChatResponse` con `UsaIa: true`. |
| **L422–L429** | Catch: igual devuelve 200 con mensaje de error amigable y `UsaIa: false`. |
| **L433–L437** | GET `estado` → `DeepSeekService.EstadoAsync()` + URL, modelo, proveedor. |
| **L441–L442** | GET `ollama` → redirige a `EstadoIa` (compatibilidad contrato viejo). |
| **L444–L446** | GET `salud` **AllowAnonymous**: JSON `{ status, servicio }` para healthcheck. |

---

## 7. Tipos y utilidades de tickets (L448–L490)

| Líneas | Método | Explicación |
|--------|--------|-------------|
| **L449** | `record Eleccion` | Tupla interna: plaza, motivo, explicación, proveedor. |
| **L452–L474** | `TicketAbiertoAsync` | Sin placa → null. Busca ticket ABIERTO por placa. Si la plaza en mapa **no** está `Ocupado`, asume desfase y **no** bloquea. Si no, `Conflicto` con código de plaza. |
| **L477–L485** | `SinTicketAbiertoAsync` | Quita de la lista cualquier `EspacioId` que tenga ticket ABIERTO (evita doble asignación). |
| **L487–L490** | `ConflictoDeEstaPlaca` | Detecta mensajes de `AbrirAsync` que indican placa duplicada o vehículo de baja. |

---

## 8. Pipeline de plazas candidatas (usado en L55–L58 y L100–L103)

| Método | Líneas | Paso a paso |
|--------|--------|-------------|
| `DisponiblesAsync(tipo, null)` | L55, L100 | Plazas libres para tipo vehículo (AUTO, MOTO, …). |
| `IncluirPlazasDePerfilAsync` | L637–L655 | Si preferencia habla de discapacidad/embarazo, **añade** plazas DISCAPACIDAD/EMBARAZADA disponibles y une sin duplicar por `Id`. |
| `CandidatasDePerfil` | L605–L611 | Calcula mínimo `PrioridadPerfil`; deja solo plazas con ese mínimo (mejor encaje legal). |
| `SinTicketAbiertoAsync` | L477–L485 | Excluye plazas con ticket abierto en BD. |

---

## 9. `ElegirPlazaAsync` (L492–L545) — estudio obligatorio

| Líneas | Paso |
|--------|------|
| **L495** | Lee último `ReporteBalance` de MySQL (puede ser null). |
| **L496** | `OrdenarParaEquilibrar` (§10): lista de mejor a peor candidata. |
| **L498** | Si `DeepSeek.Configurado` (Ollama local u API con clave válida). |
| **L502** | `ConstruirPromptAsignacion` (L671–L695). |
| **L505–L506** | Timeout **12 s**, `json: true`, max 200 tokens. |
| **L507–L508** | Extrae JSON del texto (tolerante a markdown alrededor). |
| **L510–L512** | Campo obligatorio `"codigo"`. |
| **L514–L515** | Código debe existir en `ordenadas`. |
| **L517–L519** | La IA no puede elegir plaza **peor** en perfil que `ordenadas[0]`. |
| **L521–L522** | Si con balance la plaza elegida tiene **más usos** que la heurística top → fuerza la top. |
| **L524–L526** | Éxito: motivo con `MotivoDePerfil`, explicación con usos del reporte, proveedor real. |
| **L528–L532** | Cualquier excepción → log y cae a heurística. |
| **L534–L537** | LLM no configurado. |
| **L539–L544** | Toma `ordenadas[0]`; motivo indica fallback local; proveedor `heuristica-local`. |

---

## 10. Ordenación y perfil (L547–L668)

### `OrdenarParaEquilibrar` (L547–L556)

Ordena por, en orden:

1. `PrioridadPerfil` (menor es mejor).
2. Usos de **código de plaza** en balance.
3. Usos de **zona** en balance.
4. `%` ocupación de la zona ahora (`OcupacionDeZona`, default 50 si sin dato).
5. `TarifaHora` ascendente.
6. `PreferenciaNivel` (cerca/barato según palabras en preferencia).

### `PrioridadPerfil` (L579–L598)

Devuelve **0** si la plaza encaja con el texto de preferencia; **8** o **10** si no. Discapacidad exige `tipo == DISCAPACIDAD`; embarazo `EMBARAZADA`; tercera edad Zona A nivel 1; estudiante Zona C; mujer Zona D; hombre Zona B o C.

### `MotivoDePerfil` (L613–L635)

Genera párrafo en español para el usuario: quién conduce, regla de descuento/zona, código y zona elegidos.

### `ExplicacionEquilibrada` (L561–L570)

Texto que menciona fechas del reporte guardado y contadores de usos plaza/zona.

### `PreferenciaNivel` (L660–L668)

Si preferencia contiene “cerca/entrada/rapido” → prioriza nivel bajo; “barato/econom” → prioriza tarifa baja.

---

## 11. Prompts y datos para el LLM (L670–L854)

| Líneas | Función | Rol |
|--------|---------|-----|
| **L671–L695** | `ConstruirPromptAsignacion` | Instrucciones + `ReglaDe` + ocupación por zona + historial de usos + JSON de plazas + pide respuesta `{"codigo","motivo"}`. |
| **L697–L713** | `ReglaDe` | Una regla imperativa según palabras clave (discapacidad, embarazo, mujer, etc.). |
| **L715–L726** | `LimpiarMarkdown` | Quita `**` y viñetas al resumen del reporte. |
| **L728–L739** | `ConstruirPromptReporte` | Pide 5 bloques en español sin inventar números; hora pico exacta. |
| **L741–L748** | `LineaPlaza` | Serializa una plaza a JSON inline para el prompt. |
| **L751–L757** | `DescripcionZonas` | Formato estilo Java `Map.toString` con % por zona. |
| **L759–L854** | `ConstruirDatosReporte` | Construye bloque **DATOS:** multilínea con TODO lo auditable (plazas ahora, dinero, hora pico línea a línea, permanencia, top plazas, perfiles, cobro por día). |

---

## 12. Perfiles en estadísticas del reporte (L856–L896)

| Líneas | Función | Explicación |
|--------|---------|-------------|
| **L856–L862** | `PerfilesPorPlacaAsync` | Mapa placa → `Propietario` desde tabla vehículos. |
| **L864–L878** | `PerfilPersona` | Usa propietario si coincide con `NombrePerfil`; si no, **estima** por % descuento y paridad hash de placa (heurística estadística, no regla de asignación). |
| **L880–L890** | `NombrePerfil` | Normaliza etiquetas: Capacidades especiales, Tercera edad, Embarazada, etc. |
| **L892–L896** | `ParsearFecha` | Parsea fechas del body del reporte en cultura invariante. |

---

## 13. Tabla resumen de endpoints (referencia rápida)

| Líneas método | HTTP | Path completo | Rol |
|---------------|------|---------------|-----|
| L41–79 | POST | `/api/ia/recomendar` | CLIENTE, ADMIN |
| L82–162 | POST | `/api/ia/asignar` | CLIENTE, ADMIN |
| L174–394 | POST | `/api/ia/reporte-tickets` | ADMIN |
| L397–430 | POST | `/api/ia/chat` | Autenticado |
| L433–437 | GET | `/api/ia/estado` | Autenticado |
| L441–442 | GET | `/api/ia/ollama` | Autenticado |
| L444–446 | GET | `/api/ia/salud` | Anónimo |

---

## 14. Archivos que debes abrir junto con este controlador

| Archivo | Relación con `IaController` |
|---------|-------------------------------|
| `Servicios/DeepSeekService.cs` | HTTP `chat/completions`, `Configurado`, `Proveedor`. |
| `Servicios/ServicioEspacios.cs` | Disponibles, ocupación, listar por sedes. |
| `Servicios/ServicioTickets.cs` | Abrir y reconciliar tickets. |
| `Repositorios/ReporteBalanceRepositorio.cs` | Tabla `reporte_balance`, fila id=1. |
| `Modelos/Dtos.cs` | Requests/responses de IA. |
| `App/Servicios/ApiCliente.cs` | Cliente MAUI que llama estas rutas. |

---

## 15. Regenerar este PDF

```powershell
cd c:\moviles\Parqueadero
.\docs\generar-manual-ia-controller-pdf.ps1
```

Salida: **`c:\moviles\Parqueadero\docs\MANUAL_IA_CONTROLLER.pdf`**

---

*Documento generado para estudio del fuente `IaController.cs` — Urban Park.*