# ============================================================================
#  PRUEBA HTTP DE EXTREMO A EXTREMO - URBAN PARK
#
#  Recorre la API real por HTTP: login, permisos por rol, plazas, vehiculos,
#  apertura y cierre de ticket con cobro, IA, reporte y administracion.
#
#  Uso:
#    1) Arranca la API:   cd UrbanPark.Api ; dotnet run
#    2) Ejecuta:          powershell -ExecutionPolicy Bypass -File tests\prueba_http.ps1
#
#  Parametro opcional -Base (por defecto http://127.0.0.1:8090)
#
#  NOTA: este archivo se ha escrito SOLO con caracteres ASCII a proposito,
#  para que funcione igual en Windows PowerShell 5.1 y en PowerShell 7 sin
#  depender de la codificacion con la que se lea el fichero.
# ============================================================================

param([string]$Base = "http://127.0.0.1:8090")

$global:pruebas = 0
$global:fallos = 0

function Comprobar($nombre, $condicion, $detalle = "") {
    $global:pruebas++
    if ($condicion) {
        Write-Host "  OK    $nombre" -ForegroundColor Green
    } else {
        $global:fallos++
        Write-Host "  FALLA $nombre  ->  $detalle" -ForegroundColor Red
    }
}

function Req($metodo, $ruta, $token = $null, $cuerpo = $null) {
    $cabeceras = @{}
    if ($token) { $cabeceras["Authorization"] = "Bearer $token" }

    $p = @{ Uri = "$Base$ruta"; Method = $metodo; Headers = $cabeceras; TimeoutSec = 240; UseBasicParsing = $true }
    if ($cuerpo) {
        $p["Body"] = ($cuerpo | ConvertTo-Json -Compress)
        $p["ContentType"] = "application/json"
    }

    try {
        $r = Invoke-WebRequest @p
        $datos = $null
        if ($r.Content) { try { $datos = $r.Content | ConvertFrom-Json } catch { $datos = $r.Content } }
        return @{ ok = $true; status = [int]$r.StatusCode; datos = $datos; crudo = $r.Content }
    }
    catch {
        $resp = $_.Exception.Response
        if ($resp) {
            $sr = New-Object System.IO.StreamReader($resp.GetResponseStream())
            $texto = $sr.ReadToEnd()
            $datos = $null
            if ($texto) { try { $datos = $texto | ConvertFrom-Json } catch { $datos = $texto } }
            return @{ ok = $false; status = [int]$resp.StatusCode; datos = $datos; crudo = $texto }
        }
        return @{ ok = $false; status = 0; datos = $null; crudo = $_.Exception.Message }
    }
}

Write-Host "============================================================="
Write-Host " PRUEBA HTTP DE EXTREMO A EXTREMO - URBAN PARK"
Write-Host " API: $Base"
Write-Host "============================================================="
Write-Host ""

# ---------------------------------------------------------------- 1) publico
Write-Host "[1] Endpoints publicos"
$r = Req GET "/"
Comprobar "GET / responde 200" ($r.status -eq 200) $r.crudo
$r = Req GET "/api/ia/salud"
Comprobar "GET /api/ia/salud responde 200" ($r.status -eq 200) $r.crudo
$r = Req GET "/api/auth/salud"
Comprobar "GET /api/auth/salud responde 200" ($r.status -eq 200) $r.crudo
$r = Req GET "/swagger/v1/swagger.json"
Comprobar "Swagger disponible" ($r.status -eq 200) $r.crudo
$r = Req GET "/api/auth/google/config"
Comprobar "GET /api/auth/google/config responde 200" ($r.status -eq 200) $r.crudo
Comprobar "Google aparece como NO configurado (falta el Client ID)" ($r.datos.configurado -eq $false)

# ---------------------------------------------------------------- 2) login
Write-Host ""
Write-Host "[2] Inicio de sesion"
$r = Req POST "/api/auth/login" $null @{ username = "admin1"; password = "admin123" }
Comprobar "login admin1 / admin123 -> 200" ($r.status -eq 200) $r.crudo
$tokenAdmin = $r.datos.accessToken
Comprobar "devuelve accessToken" (-not [string]::IsNullOrWhiteSpace($tokenAdmin))
Comprobar "devuelve refreshToken" (-not [string]::IsNullOrWhiteSpace($r.datos.refreshToken))
Comprobar "el rol principal de admin1 es ADMIN" ($r.datos.roles -contains "ADMIN") ($r.datos.roles -join ",")
Comprobar "devuelve la fecha de expiracion" ($r.datos.expiresAt -gt 0)

$r = Req POST "/api/auth/login" $null @{ username = "cliente1"; password = "cliente123" }
Comprobar "login cliente1 / cliente123 -> 200" ($r.status -eq 200) $r.crudo
$tokenCliente = $r.datos.accessToken
Comprobar "cliente1 solo tiene rol CLIENTE" (($r.datos.roles -join ",") -eq "CLIENTE") ($r.datos.roles -join ",")

$r = Req POST "/api/auth/login" $null @{ username = "operador1"; password = "operador123" }
Comprobar "login operador1 / operador123 -> 200" ($r.status -eq 200) $r.crudo
$tokenOperador = $r.datos.accessToken

$r = Req POST "/api/auth/login" $null @{ username = "cliente1"; password = "incorrecta" }
Comprobar "contrasena incorrecta -> 401" ($r.status -eq 401) $r.crudo
Comprobar "el mensaje de error es el esperado" ($r.datos.message -match "Usuario o contrase.a incorrectos") $r.datos.message

$r = Req POST "/api/auth/refresh" $null @{ refreshToken = "token.invalido.xxx" }
Comprobar "refresh con token invalido -> 401" ($r.status -eq 401) $r.crudo

# ---------------------------------------------------------------- 3) permisos
Write-Host ""
Write-Host "[3] Permisos por rol"
$r = Req GET "/api/espacios"
Comprobar "sin token -> 401" ($r.status -eq 401) $r.crudo
Comprobar "el 401 trae el formato de error { status, message, path }" `
    (($r.datos.status -eq 401) -and ($r.datos.message) -and ($r.datos.path -eq "/api/espacios")) $r.crudo

$r = Req GET "/api/admin/users" $tokenCliente
Comprobar "cliente1 en /api/admin/users -> 403" ($r.status -eq 403) $r.crudo
$r = Req GET "/api/admin/users" $tokenOperador
Comprobar "operador1 en /api/admin/users -> 403" ($r.status -eq 403) $r.crudo
$r = Req GET "/api/admin/users" $tokenAdmin
Comprobar "admin1 en /api/admin/users -> 200" ($r.status -eq 200) $r.crudo
Comprobar "lista al menos 5 usuarios" ($r.datos.Count -ge 5) $r.datos.Count

$r = Req POST "/api/ia/recomendar" $tokenOperador @{ tipoVehiculo = "AUTO"; preferencia = "" }
Comprobar "operador1 NO puede usar la IA -> 403 (como en el original)" ($r.status -eq 403) $r.crudo

# ---------------------------------------------------------------- 4) plazas
Write-Host ""
Write-Host "[4] Plazas y ocupacion"
$r = Req GET "/api/espacios" $tokenCliente
Comprobar "cliente1 lista las 768 plazas" ($r.datos.Count -eq 768) $r.datos.Count
$plazas = $r.datos
$plazaA01 = $plazas | Where-Object { $_.codigo -eq "CEN-A-07" }
Comprobar "la plaza CEN-A-07 existe con tarifa 3.5" ($plazaA01.tarifaHora -eq 3.5) $plazaA01.tarifaHora

$r = Req GET "/api/espacios/ocupacion" $tokenCliente
Comprobar "ocupacion total = 768" ($r.datos.total -eq 768) $r.datos.total
$nZonas = @($r.datos.zonas.PSObject.Properties).Count
Comprobar "la ocupacion trae el desglose de las 48 zonas" ($nZonas -eq 48) "zonas=$nZonas"

$r = Req GET "/api/espacios/disponibles?tipo=AUTO" $tokenCliente
Comprobar "disponibles tipo AUTO devuelve plazas" ($r.datos.Count -gt 0) $r.datos.Count

# ---------------------------------------------------------------- 5) vehiculos
Write-Host ""
Write-Host "[5] Vehiculos (filtrado por rol)"
$r = Req GET "/api/vehiculos" $tokenCliente
Comprobar "cliente1 solo ve sus vehiculos" ($r.datos.Count -ge 1) $r.datos.Count
Comprobar "entre ellos esta ABC-123" (($r.datos.placa) -contains "ABC-123") (($r.datos.placa) -join ",")
$r = Req GET "/api/vehiculos" $tokenOperador
Comprobar "operador1 ve todos los vehiculos" ($r.datos.Count -ge 4) $r.datos.Count
$r = Req GET "/api/vehiculos/placa/abc-123" $tokenCliente
Comprobar "buscar por placa ignora mayusculas -> 200" ($r.status -eq 200) $r.crudo

# ---------------------------------------------------------------- 6) tickets
Write-Host ""
Write-Host "[6] Apertura y cierre de ticket"
$r = Req GET "/api/tickets/abiertos" $tokenAdmin
foreach ($t in $r.datos) { Req POST "/api/tickets/$($t.id)/cerrar" $tokenAdmin | Out-Null }

$r = Req POST "/api/tickets/abrir" $tokenCliente @{ placa = "ABC-123"; espacioId = $plazaA01.id }
Comprobar "abrir ticket -> 200" ($r.status -eq 200) $r.crudo
$ticket = $r.datos
Comprobar "el ticket queda ABIERTO" ($ticket.estado -eq "ABIERTO") $ticket.estado
Comprobar "el codigo es TK-XXXXXXXX" ($ticket.codigo -match "^TK-[0-9A-F]{8}$") $ticket.codigo
Comprobar "copia la plaza y la zona" (($ticket.espacioCodigo -eq "CEN-A-01") -and ($ticket.zona -eq "Zona A - Centro"))
Comprobar "el monto es null mientras esta abierto" ($null -eq $ticket.montoTotal)

$r = Req POST "/api/tickets/abrir" $tokenCliente @{ placa = "ABC-123"; espacioId = $plazaA01.id }
Comprobar "abrir dos veces la misma placa -> 409" ($r.status -eq 409) $r.crudo
Comprobar "el mensaje de conflicto es el del original" `
    ($r.datos.message -match "^Ya existe un ticket abierto para la placa ABC-123") $r.datos.message

$r = Req POST "/api/tickets/abrir" $tokenCliente @{ placa = "XX"; espacioId = $plazaA01.id }
Comprobar "placa invalida -> 400" ($r.status -eq 400) $r.crudo

$r = Req GET "/api/espacios/$($plazaA01.id)" $tokenCliente
Comprobar "la plaza CEN-A-01 queda OCUPADA" ($r.datos.ocupado -eq $true)

$r = Req POST "/api/tickets/$($ticket.id)/cerrar" $tokenCliente
Comprobar "cerrar ticket -> 200" ($r.status -eq 200) $r.crudo
Comprobar "el ticket queda CERRADO" ($r.datos.estado -eq "CERRADO") $r.datos.estado
Comprobar "cobra 1 hora de tarifa ($3.50)" ($r.datos.montoTotal -eq 3.5) $r.datos.montoTotal
Comprobar "guarda la fecha de salida" ($null -ne $r.datos.salida)
Comprobar "no queda liberacion pendiente" ($r.datos.liberacionPendiente -eq $false)

$r = Req GET "/api/espacios/$($plazaA01.id)" $tokenCliente
Comprobar "la plaza vuelve a estar LIBRE" ($r.datos.ocupado -eq $false)

$r = Req GET "/api/tickets" $tokenCliente
Comprobar "cliente1 ve sus tickets" ($r.datos.Count -ge 1) $r.datos.Count

# ---------------------------------------------------------------- 7) IA
Write-Host ""
Write-Host "[7] Inteligencia artificial (DeepSeek sin configurar -> respaldo local)"
$r = Req GET "/api/ia/estado" $tokenCliente
Comprobar "GET /api/ia/estado responde 200" ($r.status -eq 200) $r.crudo
Comprobar "informa del estado del motor de IA" (-not [string]::IsNullOrWhiteSpace($r.datos.mensaje)) $r.datos.mensaje
$r = Req GET "/api/ia/ollama" $tokenCliente
Comprobar "el alias /api/ia/ollama sigue funcionando" ($r.status -eq 200) $r.crudo

$r = Req POST "/api/ia/recomendar" $tokenCliente @{ tipoVehiculo = "AUTO"; preferencia = "barato" }
Comprobar "recomendar -> 200" ($r.status -eq 200) $r.crudo
Comprobar "devuelve una plaza valida" ($r.datos.espacioCodigo -match "^[A-Z]{2,4}-[A-D]-[0-9]{2}$") $r.datos.espacioCodigo
Comprobar "indica el proveedor de IA" (-not [string]::IsNullOrWhiteSpace($r.datos.proveedorIa)) $r.datos.proveedorIa
Comprobar "devuelve 3 alternativas" ($r.datos.alternativas.Count -eq 3) $r.datos.alternativas.Count
$codigoRecomendado = $r.datos.espacioCodigo
$r = Req GET "/api/espacios" $tokenCliente
$plazaReco = $r.datos | Where-Object { $_.codigo -eq $codigoRecomendado }
Comprobar "recomendar NO ocupa la plaza" ($plazaReco.ocupado -eq $false)

$r = Req POST "/api/ia/asignar" $tokenCliente @{ placa = "XYZ-789"; tipoVehiculo = "MOTO"; preferencia = "cerca" }
Comprobar "asignar con IA -> 200" ($r.status -eq 200) $r.crudo
Comprobar "asignado = true" ($r.datos.asignado -eq $true) $r.crudo
Comprobar "devuelve el ticket creado" ($r.datos.ticketCodigo -match "^TK-") $r.datos.ticketCodigo
$ticketIa = $r.datos.ticketId
$r = Req GET "/api/tickets/$ticketIa" $tokenCliente
Comprobar "el ticket de la IA existe y esta abierto" ($r.datos.estado -eq "ABIERTO") $r.crudo

$r = Req POST "/api/ia/asignar" $tokenCliente @{ placa = ""; tipoVehiculo = "AUTO" }
Comprobar "asignar sin placa -> 400" ($r.status -eq 400) $r.crudo

# ---------------------------------------------------------------- 8) reporte
Write-Host ""
Write-Host "[8] Reporte IA (ADMIN)"
$desde = (Get-Date).AddDays(-7).ToString("yyyy-MM-dd")
$hasta = (Get-Date).ToString("yyyy-MM-dd")
$r = Req POST "/api/ia/reporte-tickets" $tokenAdmin @{ fechaDesde = $desde; fechaHasta = $hasta }
Comprobar "generar reporte -> 200" ($r.status -eq 200) $r.crudo
$rep = $r.datos
Comprobar "informa de las 768 plazas" ($rep.plazasTotal -eq 768) $rep.plazasTotal
Comprobar "cuenta tickets del periodo" ($rep.totalTickets -ge 1) $rep.totalTickets
Comprobar "el recaudado acumula los tickets cerrados del periodo" ($rep.recaudado -ge 3.5) $rep.recaudado
Comprobar "estima lo pendiente por cobrar" ($rep.porCobrarEstimado -ge 0) $rep.porCobrarEstimado
Comprobar "genera al menos 5 hallazgos" ($rep.hallazgos.Count -ge 5) $rep.hallazgos.Count
Comprobar "el resumen narrativo no esta vacio" (-not [string]::IsNullOrWhiteSpace($rep.resumenIa))
Comprobar "el resumen indica su proveedor de IA" (![string]::IsNullOrWhiteSpace($rep.proveedorIa)) $rep.proveedorIa
$nDetalle = @($rep.metricas.plazasOcupadasDetalle).Count
Comprobar "el detalle de plazas ocupadas es coherente con los tickets abiertos" `
    ($nDetalle -eq $rep.ticketsAbiertosAhora) "detalle=$nDetalle abiertos=$($rep.ticketsAbiertosAhora)"

$r = Req POST "/api/ia/reporte-tickets" $tokenAdmin @{ fechaDesde = "2026-12-31"; fechaHasta = "2026-01-01" }
Comprobar "fechas invertidas -> 400" ($r.status -eq 400) $r.crudo
$r = Req POST "/api/ia/reporte-tickets" $tokenCliente @{ fechaDesde = $desde; fechaHasta = $hasta }
Comprobar "cliente1 no puede generar el reporte -> 403" ($r.status -eq 403) $r.crudo

# ---------------------------------------------------------------- 9) admin
Write-Host ""
Write-Host "[9] Administracion de usuarios (ADMIN)"
$usuarioNuevo = "http_$(Get-Random -Minimum 1000 -Maximum 9999)"
$r = Req POST "/api/admin/users" $tokenAdmin @{
    username = $usuarioNuevo; email = "$usuarioNuevo@test.local"
    firstName = "Prueba"; lastName = "Http"; password = "clave123"; role = "CLIENTE"
}
Comprobar "crear usuario -> 201" ($r.status -eq 201) $r.crudo
$idNuevo = $r.datos.id
$r = Req POST "/api/auth/login" $null @{ username = $usuarioNuevo; password = "clave123" }
Comprobar "el usuario creado puede iniciar sesion" ($r.status -eq 200) $r.crudo
$r = Req PUT "/api/admin/users/$idNuevo/roles" $tokenAdmin @{ role = "OPERADOR" }
Comprobar "cambiar el rol a OPERADOR -> 200" ($r.status -eq 200) $r.crudo
Comprobar "el rol se actualizo" ($r.datos.roles -contains "OPERADOR") ($r.datos.roles -join ",")
$r = Req PUT "/api/admin/users/$idNuevo/password" $tokenAdmin @{ password = "otraclave9" }
Comprobar "resetear la contrasena -> 200" ($r.status -eq 200) $r.crudo
$r = Req POST "/api/auth/login" $null @{ username = $usuarioNuevo; password = "otraclave9" }
Comprobar "la contrasena nueva funciona" ($r.status -eq 200) $r.crudo
$r = Req POST "/api/admin/users" $tokenAdmin @{
    username = $usuarioNuevo; email = "otro@test.local"
    firstName = "X"; lastName = "Y"; password = "clave123"; role = "CLIENTE"
}
Comprobar "usuario duplicado -> 409" ($r.status -eq 409) $r.crudo
$r = Req POST "/api/admin/users" $tokenAdmin @{
    username = "rolmalo$(Get-Random)"; email = "rol@test.local"
    firstName = "X"; lastName = "Y"; password = "clave123"; role = "SUPERADMIN"
}
Comprobar "rol no permitido -> 400" ($r.status -eq 400) $r.crudo

# ---------------------------------------------------------------- 10) perfil
Write-Host ""
Write-Host "[10] Perfil propio"
$r = Req GET "/api/perfil/me" $tokenCliente
Comprobar "cliente1 lee su perfil -> 200" ($r.status -eq 200) $r.crudo
Comprobar "el perfil es de cliente1" ($r.datos.username -eq "cliente1") $r.datos.username
$r = Req PUT "/api/perfil/me/password" $tokenCliente @{ currentPassword = "mala"; newPassword = "nueva12345" }
Comprobar "cambiar contrasena con la actual incorrecta -> 400" ($r.status -eq 400) $r.crudo
Comprobar "el mensaje avisa de que la actual es incorrecta" `
    ($r.datos.message -match "actual incorrecta") $r.datos.message

# ---------------------------------------------------------------- 11) limpieza
Write-Host ""
Write-Host "[11] Limpieza"
$r = Req GET "/api/tickets/abiertos" $tokenAdmin
$abiertos = @($r.datos).Count
foreach ($t in $r.datos) { Req POST "/api/tickets/$($t.id)/cerrar" $tokenAdmin | Out-Null }
$r = Req GET "/api/tickets/abiertos" $tokenAdmin
Comprobar "no quedan tickets abiertos" (@($r.datos).Count -eq 0) @($r.datos).Count
Write-Host "      (se cerraron $abiertos tickets de la prueba)"

Write-Host ""
Write-Host "============================================================="
if ($global:fallos -eq 0) {
    Write-Host " RESULTADO: $($global:pruebas)/$($global:pruebas) comprobaciones correctas  -  TODO OK" -ForegroundColor Green
} else {
    Write-Host " RESULTADO: $($global:pruebas - $global:fallos)/$($global:pruebas) correctas  ($($global:fallos) FALLOS)" -ForegroundColor Red
}
Write-Host "============================================================="
exit $global:fallos
