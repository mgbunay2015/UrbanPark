# Guía de ejecución — Urban Park móvil

Esta guía lleva el proyecto desde cero hasta verlo funcionando en el celular (o en Windows).

---

## 0. Requisitos

| Herramienta | Versión | Notas |
|---|---|---|
| .NET SDK | 10.0 o superior | `dotnet --version` |
| Workload MAUI | instalado | `dotnet workload install maui` |
| MySQL Server | 8.0 o superior | XAMPP, WAMP, MySQL Installer o Docker |
| JDK 17 | para Android | requerido por MAUI Android |
| Android SDK | API 35/36 | se instala con Visual Studio o Android Studio |

> La app también compila y se ejecuta en **Windows** (`net10.0-windows10.0.19041.0`), que es la forma
> más rápida de probarla sin emulador.

---

## 1. Base de datos MySQL

**Opción A — automática (recomendada).** Al arrancar, la API crea la base `urbanpark`, las tablas
(`usuarios`, `sedes`, `espacios`, `vehiculos`, `tickets`) y los datos semilla (12 parqueaderos con 768 plazas, 4 vehículos, 5 usuarios demo).
Solo necesitas que MySQL esté encendido y que la cadena de conexión sea correcta.

**Opción B — manual.**

```bash
mysql -u root -p < database/urbanpark_mysql.sql
```

Crea la base, el esquema y los datos semilla. Es idempotente: puedes ejecutarlo varias veces.

**Opción C — MySQL en Docker (así está configurado este proyecto ahora mismo).**

```bash
docker run -d --name urbanpark-mysql \
  -e MYSQL_ROOT_PASSWORD=urban123 \
  -e MYSQL_DATABASE=urbanpark \
  -p 3306:3306 \
  mysql:8.4

# aplicar el esquema y los datos semilla
docker cp database/urbanpark_mysql.sql urbanpark-mysql:/tmp/esquema.sql
docker exec urbanpark-mysql mysql -uroot -purban123 -e "source /tmp/esquema.sql"
```

Con esta opción la cadena de conexión ya viene lista en `appsettings.json`
(usuario `root`, contraseña `urban123`, puerto `3306`).

> **Ojo con los puertos:** la API usa el **8090** porque el 8080 suele estar ocupado por Keycloak
> (por ejemplo, el del proyecto original). Si necesitas otro puerto, cámbialo en
> `UrbanPark.Api/appsettings.json` (clave `Urls`), en `Properties/launchSettings.json` y en
> `UrbanPark.App/Servicios/ApiCliente.cs`.

### Usar un MySQL que ya tengas

Si tu MySQL está en otro puerto (por ejemplo el 3307) o tiene otra contraseña, solo cambia
`ConnectionStrings:UrbanPark` en `appsettings.json`:

```json
"UrbanPark": "Server=localhost;Port=3307;Database=urbanpark;User Id=root;Password=TU_PASSWORD;CharSet=utf8mb4"
```

---

## 2. Configurar y arrancar la API

Edita `UrbanPark.Api/appsettings.json`:

```json
"ConnectionStrings": {
  "UrbanPark": "Server=localhost;Port=3306;Database=urbanpark;User Id=root;Password=TU_PASSWORD;CharSet=utf8mb4"
},
"Jwt": {
  "Key": "UNA_CLAVE_LARGA_Y_SECRETA_DE_MINIMO_32_CARACTERES"
},
"DeepSeek": {
  "ApiKey": "sk-...tu-api-key..."
},
"Google": {
  "ClientId": "1234567890-xxxxxxxx.apps.googleusercontent.com"
}
```

Arranca:

```bash
cd UrbanPark.Api
dotnet run
```

Comprueba en el navegador del PC:

- `http://localhost:8090/` → mensaje de bienvenida.
- `http://localhost:8090/swagger` → **Swagger UI** para probar todos los endpoints.
- `http://localhost:8090/api/ia/salud` → `{"status":"ms-ia OK"}`.

Prueba rápida del login con Swagger:

1. `POST /api/auth/login` con `{"username":"admin1","password":"admin123"}`.
2. Copia el `accessToken`.
3. Pulsa **Authorize**, pega el token y ya puedes probar los endpoints protegidos.

---

## 3. Configurar la IA de DeepSeek

1. Crea una cuenta en <https://platform.deepseek.com> y genera una **API key**.
2. Pégala en `UrbanPark.Api/appsettings.json` → `DeepSeek:ApiKey`.
3. Listo. Se usa en tres lugares:
   - **Asignación con DeepSeek**: elige la plaza y redacta el motivo.
   - **Reporte IA**: redacta el resumen del administrador.
   - **Asistente DeepSeek**: el chat libre con la IA.

**Sin API key la app funciona igual**: si DeepSeek no responde o no está configurado, el backend usa el
**respaldo heurístico local** (menor ocupación de zona → menor tarifa → preferencia del conductor) y lo
indica en el campo `proveedorIa` (`heuristica-local`), igual que hacía el sistema original con Ollama.

Ajustes opcionales en la sección `DeepSeek`: `BaseUrl` (`https://api.deepseek.com`), `Model`
(`deepseek-chat`), `Temperature` (0.3), `MaxTokens` y `TimeoutSeconds` (90).

---

## 4. Configurar Google Sign-In (opcional)

La app funciona sin Google usando usuario/contraseña, pero si quieres habilitarlo:

1. Entra a <https://console.cloud.google.com/apis/credentials> y crea un **ID de cliente de OAuth**.
2. Crea un cliente por plataforma:
   - **Android**: tipo *Android*, nombre de paquete `com.urbanpark.app` y la huella **SHA-1** de tu firma
     (obtenla con `keytool -list -v -keystore %USERPROFILE%\.android\debug.keystore -alias androiddebugkey -storepass android -keypass android`).
   - **Windows/iOS**: tipo *iOS* o *Web*, según el caso.
3. En *URIs de redirección autorizados* agrega: `com.urbanpark.app:/oauth2redirect`
4. Copia el **Client ID** en dos lugares:
   - `UrbanPark.Api/appsettings.json` → `Google:ClientId` (y `AndroidClientId` si aplica).
   - `UrbanPark.App/Servicios/GoogleAuthServicio.cs` → constante `ClientId`.
5. En **iOS**, añade el esquema en `Platforms/iOS/Info.plist`:

```xml
<key>CFBundleURLTypes</key>
<array>
  <dict>
    <key>CFBundleURLSchemes</key>
    <array><string>com.urbanpark.app</string></array>
  </dict>
</array>
```

En **Android** ya está declarado en `Platforms/Android/CallbackActivity.cs`.

> El botón muestra «Entrar con Google (sin configurar)» mientras el Client ID siga siendo el marcador
> de posición, y el API responde 503 con un mensaje claro. Nada se rompe.

---

## 5. Ejecutar la app móvil

### 5.1 En Windows (lo más rápido para probar)

```bash
cd UrbanPark.App
dotnet build -f net10.0-windows10.0.19041.0 -t:Run
```

La URL del API por defecto en Windows es `http://localhost:8090`.
Puedes cambiarla desde la propia pantalla de login (campo «URL del API» + botón «Aplicar»).

### 5.2 En Android (emulador)

1. Enciende un emulador desde Android Studio o Visual Studio.
2. Desde la carpeta `UrbanPark.App`:

```bash
dotnet build -f net10.0-android -t:Run
```

En el emulador, `10.0.2.2` es el `localhost` de tu PC: la app ya viene configurada con
`http://10.0.2.2:8090`, así que no hay que cambiar nada.

### 5.3 En un celular físico

1. Averigua la IP de tu PC en la red WiFi (`ipconfig`).
2. Asegúrate de que la API escuche en toda la red: en `appsettings.json` está `"Urls": "http://0.0.0.0:8090"`.
3. Permite el puerto 8090 en el firewall de Windows.
4. En la app, entra al login y escribe `http://TU_IP:8090` en «URL del API» → **Aplicar**.

También puedes cambiar la URL por defecto en `Servicios/ApiCliente.cs`.

---

## 6. Probar el flujo completo (5 minutos)

1. Entra como **cliente1 / cliente123**.
2. **Mis vehículos** → verás `ABC-123`. Registra otro si quieres.
3. **Plazas** → pulsa «Usar esta plaza» en una libre (te avisa del código).
4. **Mis tickets** → elige tu placa y esa plaza → **Abrir ticket**.
5. **Asignación con DeepSeek** → botón «Estado DeepSeek» para ver si la IA responde; luego
   «Solo recomendar» y «Asignar con DeepSeek» con una placa distinta. Prueba también el chat.
6. **Mis tickets** → «Cerrar / cobrar» y comprueba el importe (mínimo 1 hora, redondeo hacia arriba).
7. Sal y entra como **admin1 / admin123**:
   - **Reporte IA** → «Generar reporte» con los últimos 7 días.
   - **Usuarios** → crea un usuario, edítalo y cámbiale la contraseña.
   - **Resilience** → «Detener espacios» → «Probar 1 llamada» (verás **Fallback activo**) → «Iniciar espacios»
     → «Probar 1 llamada» (verás **Circuit Breaker + Retry OK**). Mira también la respuesta JSON y el historial.

---

## 7. Problemas frecuentes

| Síntoma | Causa y solución |
|---|---|
| «No se pudo conectar con el API» | La API no está encendida, o la URL es incorrecta. En emulador usa `10.0.2.2`, en celular la IP de tu PC. |
| «No se pudo inicializar MySQL» en la consola | Usuario/contraseña o puerto mal en `ConnectionStrings:UrbanPark`, o MySQL apagado. |
| El reporte tarda mucho | Es normal: DeepSeek puede tardar. El cliente espera hasta 180 segundos. |
| `proveedorIa: heuristica-local` | DeepSeek no está configurado o no respondió. Revisa la API key; la app sigue funcionando. |
| Google no abre el navegador | Falta configurar el Client ID o la huella SHA-1 no coincide con la de tu firma. |
| Error de compilación Android por el SDK | Instala el Android SDK (API 35/36) y el JDK 17, y ejecuta `dotnet workload install maui`. |
| Quiero empezar de cero en la base | `DROP DATABASE urbanpark;` y vuelve a arrancar la API. |

---

## 7b. Desvincular usuarios (autenticador de Google + equipos)

```bat
cd database
desvincular_usuarios.bat            :: todos los usuarios (pide confirmación)
desvincular_usuarios.bat cliente1   :: un solo usuario
desvincular_usuarios.bat /y         :: todos, sin preguntar
```

Deja `totp_secret = NULL`, `totp_activo = 0` y borra la tabla `dispositivos`: las cuentas vuelven a entrar con
usuario y contraseña y la app deja de mostrar la pantalla de huella/código.

## 8. Comandos útiles

```bash
# API (compilar y ejecutar)
dotnet build UrbanPark.Api
dotnet run --project UrbanPark.Api

# App: verificar que el código y el XAML compilan sin empaquetar (rápido)
dotnet build UrbanPark.App -f net10.0-android -t:Compile

# App: compilar y ejecutar en Android (necesita emulador encendido)
dotnet build UrbanPark.App -f net10.0-android -t:Run

# App: compilar y ejecutar en Windows (lo más rápido para probar)
dotnet build UrbanPark.App -f net10.0-windows10.0.19041.0 -t:Run

# Pruebas de las reglas de negocio (no necesitan MySQL)
dotnet run --project tests/UrbanPark.Pruebas
```

> **Importante:** no uses `dotnet build UrbanPark.slnx` a secas en Windows. La app declara varios
> *target frameworks* (Android, iOS, MacCatalyst y Windows) y los de Apple necesitan un Mac con Xcode.
> Compila siempre indicando el framework con `-f`, como en los ejemplos de arriba.
> Si desarrollas en Mac, `-f net10.0-ios` y `-f net10.0-maccatalyst` sí funcionarán.

---

## 9. Verificación realizada sobre este proyecto

Todo lo de abajo **se ha ejecutado de verdad** sobre este código:

| Comprobación | Resultado |
|---|---|
| API compila | **0 errores, 0 advertencias** |
| App compila para Android (XAML + C# de las 11 pantallas) | **0 errores, 0 advertencias** |
| App compila y empaqueta para Windows | **0 errores, 0 advertencias** |
| **Reglas de negocio** — `dotnet run --project tests/UrbanPark.Pruebas` | **75/75 correctas** |
| **Integración con MySQL** — `dotnet run --project tests/UrbanPark.Integracion` | **131/131 correctas** |
| **Recorrido HTTP completo** — `tests/prueba_http.ps1` | **86/86 correctas** |
| Arranque sin MySQL | La API **no se cae**: informa del problema y sigue en marcha |
| Arranque sin `Jwt:Key` | Avisa con un mensaje claro y no levanta el servidor (mejor que fallar en cada petición) |

**Qué cubre cada suite**

- `tests/UrbanPark.Pruebas` (75) — no necesita MySQL: fórmula de cobro (1 min→1 h, 61 min→2 h, 121 min→3 h), vectores RFC 6238 del autenticador de Google,
  validación de placas, jerarquía de roles, hashes PBKDF2 de los 5 usuarios demo del script SQL,
  Circuit Breaker (abre tras 10 fallos, 3 intentos, fallback) y parseo de la respuesta de la IA.
- `tests/UrbanPark.Integracion` (131) — contra MySQL real: creación del esquema y semillas (idempotente), los 12 parqueaderos (direcciones, distancias y recomendación), el pase del puesto con su QR, las reservas con fecha y hora, el autenticador de Google,
  login con los usuarios de la base, plazas y ocupación, vehículos por propietario, apertura y cierre de
  ticket con cobro, conflictos (placa duplicada, plaza ocupada, plazas inexistentes), cobro con el servicio
  de espacios caído (`liberacionPendiente`), sincronización, reconciliación, Circuit Breaker real y el
  motor de IA con su respaldo heurístico.
- `tests/prueba_http.ps1` (86) — la API por HTTP: endpoints públicos, Swagger, login de los 3 roles,
  permisos (401 sin token, 403 por rol, el OPERADOR no puede usar la IA como en el original), plazas,
  vehículos filtrados por rol, apertura/cierre de ticket con cobro, IA (recomendar y asignar),
  reporte del administrador, gestión de usuarios y perfil propio.

Para repetir las pruebas:

```bash
# 1) MySQL encendido y la API en marcha
dotnet run --project UrbanPark.Api

# 2) En otra terminal
dotnet run --project tests/UrbanPark.Pruebas         # 75 comprobaciones
dotnet run --project tests/UrbanPark.Integracion     # 131 comprobaciones
powershell -ExecutionPolicy Bypass -File tests\prueba_http.ps1   # 86 comprobaciones
```
