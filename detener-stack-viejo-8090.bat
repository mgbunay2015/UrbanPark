@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
title Urban Park - liberar puerto 8090

echo.
echo ============================================
echo   Liberar puerto 8090 (stack web antiguo)
echo ============================================
echo   La app MAUI necesita la API .NET en 8090.
echo   El proyecto viejo (nginx + microservicios)
echo   usa el mismo puerto y provoca error 401.
echo ============================================
echo.

call :Estado8090
echo.

where docker >nul 2>&1
if errorlevel 1 (
  echo [Docker] No esta instalado. Cierra manualmente lo que use el puerto 8090.
  goto fin
)

docker info >nul 2>&1
if errorlevel 1 (
  echo [Docker] No responde. Abre Docker Desktop, espera a que cargue y ejecuta este .bat otra vez.
  goto fin
)

echo [1/4] Deteniendo nginx conocidos...
call :StopConocidos

echo [2/4] Deteniendo contenedores que publican 8090...
call :StopPuerto8090

echo [3/4] Deteniendo servicios del proyecto urban-park-proyecto-main (opcional, solo nginx si /solo)...
if /I "%~1"=="/solo" (
  echo       Modo /solo: no se detiene todo el stack antiguo.
) else (
  call :StopProyectoMain
)

ping -n 2 127.0.0.1 >nul

echo [4/4] Comprobando...
call :Estado8090
call :ApiPuertoAjeno
if not errorlevel 1 (
  echo.
  echo [AVISO] En 8090 sigue habiendo web/HTML, no la API .NET.
  echo         Reinicia Docker Desktop: clic derecho en el icono -^> Restart
  echo         Luego ejecuta de nuevo este archivo.
  goto fin
)

call :ApiUrbanPark
if not errorlevel 1 (
  echo.
  echo [OK] El puerto 8090 ya tiene la API .NET Urban Park.
  goto ofrecer_inicio
)

netstat -ano | findstr /R /C:":8090 .*LISTENING" >nul 2>&1
if errorlevel 1 (
  echo.
  echo [OK] El puerto 8090 esta libre. Ejecuta iniciar-urbanpark.bat para levantar la API.
  goto ofrecer_inicio
)

echo.
echo [AVISO] Algo sigue escuchando en 8090 pero no es la API .NET.
echo         Revisa: netstat -ano ^| findstr :8090
goto fin

:ofrecer_inicio
if /I "%~1"=="/auto" (
  echo.
  echo Arrancando iniciar-urbanpark.bat...
  call "%~dp0iniciar-urbanpark.bat" /nopause
  exit /b %errorlevel%
)
echo.
echo Siguiente paso: iniciar-urbanpark.bat
goto fin

:fin
echo.
if /I not "%~2"=="/nopause" if /I not "%~1"=="/nopause" if /I not "%~1"=="/auto" pause
exit /b 0

:StopConocidos
for %%C in (
  urban-park-proyecto-main-nginx-1
  estacionamiento-nginx-1
) do (
  docker container inspect %%C >nul 2>&1
  if not errorlevel 1 (
    docker ps -q -f name=%%C 2>nul | findstr /R "." >nul
    if not errorlevel 1 call :DockerStop "%%C"
  )
)
exit /b 0

:StopPuerto8090
for /f "delims=" %%L in ('docker ps --format "{{.Names}}|{{.Ports}}" 2^>nul') do (
  echo %%L | findstr /C:":8090->" >nul
  if not errorlevel 1 (
    for /f "tokens=1 delims=|" %%N in ("%%L") do call :DockerStop "%%N"
  )
)
for /f "delims=" %%I in ('docker ps -q --filter "publish=8090" 2^>nul') do (
  if not "%%I"=="" call :DockerStop "%%I"
)
exit /b 0

:StopProyectoMain
for /f "delims=" %%I in ('docker ps -q -f "name=urban-park-proyecto-main" 2^>nul') do (
  if not "%%I"=="" call :DockerStop "%%I"
)
exit /b 0

:DockerStop
set "OBJ=%~1"
echo       docker stop %OBJ% ...
docker stop %OBJ% >nul 2>&1
if errorlevel 1 (
  docker kill %OBJ% >nul 2>&1
  if errorlevel 1 (
    echo       [FALLO] No se pudo detener %OBJ%. Reinicia Docker Desktop.
  ) else (
    echo       [OK] kill %OBJ%
  )
) else (
  echo       [OK] stop %OBJ%
)
exit /b 0

:Estado8090
powershell -NoProfile -Command ^
  "try { $r = Invoke-WebRequest -Uri 'http://127.0.0.1:8090/' -UseBasicParsing -TimeoutSec 3; if ($r.Content -match 'Urban Park API') { Write-Host '[8090] API .NET Urban Park (correcto)'; exit 0 }; if ($r.Content -match '<(html|!DOCTYPE)' -or ($r.Headers['Content-Type'] -match 'text/html')) { Write-Host '[8090] Pagina web / nginx (INCORRECTO para la app MAUI)'; exit 0 }; Write-Host '[8090] Responde algo distinto:' $r.StatusCode; exit 0 } catch { Write-Host '[8090] Sin respuesta HTTP o puerto libre.'; exit 0 }"
exit /b 0

:ApiPuertoAjeno
powershell -NoProfile -Command "try { $r = Invoke-WebRequest -Uri 'http://127.0.0.1:8090/' -UseBasicParsing -TimeoutSec 4; if ($r.Content -match 'Urban Park API') { exit 1 }; if ($r.Content -match '<(html|!DOCTYPE)') { exit 0 }; if ($r.Headers['Content-Type'] -match 'text/html') { exit 0 }; if ($r.Content -match 'Urban Park' -and $r.Content -notmatch 'Urban Park API') { exit 0 }; exit 1 } catch { exit 1 }"
exit /b %errorlevel%

:ApiUrbanPark
powershell -NoProfile -Command "try { $r = Invoke-WebRequest -Uri 'http://127.0.0.1:8090/' -UseBasicParsing -TimeoutSec 4; if ($r.Content -match 'Urban Park API') { exit 0 }; if ($r.Content -match 'servicio' -and $r.Headers['Content-Type'] -match 'application/json') { exit 0 }; exit 1 } catch { exit 1 }"
exit /b %errorlevel%
