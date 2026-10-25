@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
title Urban Park - levantar servicios

echo.
echo ============================================
echo   Urban Park - verificar y levantar
echo ============================================
echo.

call :DockerListo
if errorlevel 1 goto fin_error

call :MySqlListo
if errorlevel 1 goto fin_error

call :OllamaListo
if errorlevel 1 goto fin_error

call :ApiLista
if errorlevel 1 goto fin_error

call :ValidarTodo
if errorlevel 1 goto fin_error

call :AppWindows

echo.
echo --------------------------------------------
echo   Servicios listos y validados
echo   API:      http://127.0.0.1:8090
echo   MySQL:    localhost:3306   root / urban123
echo   Ollama:   http://127.0.0.1:11434
echo   Login:    cliente1 / cliente123
echo   Emulador: http://10.0.2.2:8090
echo --------------------------------------------
echo Si la aplicacion ya estaba abierta, cierra el aviso
echo de error y vuelve a entrar.
echo.
pause
exit /b 0

:fin_error
echo.
echo No se pudieron levantar todos los servicios. Revisa los mensajes de arriba.
echo.
pause
exit /b 1

REM ------------------------------------------------------------------ Docker
:DockerListo
where docker >nul 2>&1
if errorlevel 1 (
  echo [Docker] No esta instalado el comando docker.
  exit /b 1
)
docker info >nul 2>&1
if not errorlevel 1 (
  echo [Docker] Ya esta en marcha.
  exit /b 0
)

echo [Docker] No responde. Abriendo Docker Desktop...
if exist "C:\Program Files\Docker\Docker\Docker Desktop.exe" (
  start "" "C:\Program Files\Docker\Docker\Docker Desktop.exe"
) else (
  echo [Docker] No se encontro Docker Desktop.
  exit /b 1
)

set /a N=0
:esperar_docker
set /a N+=1
if !N! GTR 40 (
  echo [Docker] No quedo listo. Abre Docker Desktop y vuelve a ejecutar este archivo.
  exit /b 1
)
ping -n 6 127.0.0.1 >nul
docker info >nul 2>&1
if errorlevel 1 goto esperar_docker
echo [Docker] Listo.
exit /b 0

REM ------------------------------------------------------------------ MySQL
:MySqlListo
docker container inspect urbanpark-mysql >nul 2>&1
if errorlevel 1 goto crear_mysql
echo [MySQL] El contenedor urbanpark-mysql ya existe.
docker start urbanpark-mysql >nul 2>&1
goto esperar_mysql

:crear_mysql
echo [MySQL] No existe. Creando urbanpark-mysql...
docker run -d --name urbanpark-mysql --restart unless-stopped -e MYSQL_ROOT_PASSWORD=urban123 -e MYSQL_DATABASE=urbanpark -p 3306:3306 mysql:8.4
if errorlevel 1 (
  echo [MySQL] No se pudo crear. El puerto 3306 puede estar ocupado por otro programa.
  exit /b 1
)

:esperar_mysql
set /a N=0
:bucle_mysql
set /a N+=1
if !N! GTR 40 (
  echo [MySQL] No respondio a tiempo.
  docker logs urbanpark-mysql --tail 15
  exit /b 1
)
docker exec urbanpark-mysql mysqladmin ping -uroot -purban123 --silent >nul 2>&1
if errorlevel 1 (
  ping -n 4 127.0.0.1 >nul
  goto bucle_mysql
)
docker exec urbanpark-mysql mysql -uroot -purban123 -N -e "SELECT COUNT(*) FROM urbanpark.usuarios;" >nul 2>&1
if errorlevel 1 (
  echo [MySQL] El servidor responde, pero la base urbanpark aun no esta lista. Reintento...
  ping -n 4 127.0.0.1 >nul
  if !N! GTR 35 exit /b 1
  goto bucle_mysql
)
echo [MySQL] Base urbanpark lista.
exit /b 0

REM ------------------------------------------------------------------ Ollama
:OllamaListo
call :HttpOk "http://127.0.0.1:11434/api/tags"
if not errorlevel 1 (
  echo [Ollama] Ya responde en http://127.0.0.1:11434
  exit /b 0
)

if exist "%LOCALAPPDATA%\Programs\Ollama\ollama.exe" (
  echo [Ollama] Iniciando la instalacion local...
  start "Ollama" /MIN "%LOCALAPPDATA%\Programs\Ollama\ollama.exe" serve
  goto esperar_ollama
)

docker container inspect ollama >nul 2>&1
if errorlevel 1 goto crear_ollama
echo [Ollama] El contenedor ollama ya existe. Encendiendo...
docker start ollama >nul 2>&1
goto esperar_ollama

:crear_ollama
echo [Ollama] No hay instalacion ni contenedor. Creando el contenedor...
docker run -d --name ollama --restart unless-stopped -p 11434:11434 ollama/ollama
if errorlevel 1 (
  echo [Ollama] No se pudo crear. Urban Park usara la asignacion automatica local.
  exit /b 0
)
echo [Ollama] Descargando el modelo llama3.2:1b. Puede tardar...
docker exec ollama ollama pull llama3.2:1b

:esperar_ollama
set /a N=0
:bucle_ollama
set /a N+=1
if !N! GTR 30 (
  echo [Ollama] No respondio. La API igual arranca y asigna plazas en local.
  exit /b 0
)
call :HttpOk "http://127.0.0.1:11434/api/tags"
if errorlevel 1 (
  ping -n 3 127.0.0.1 >nul
  goto bucle_ollama
)
echo [Ollama] Responde en el puerto 11434.
exit /b 0

REM ------------------------------------------------------------------ API
:ApiLista
call :HttpOk "http://127.0.0.1:8090/"
if not errorlevel 1 (
  echo [API] Ya responde en http://127.0.0.1:8090
  exit /b 0
)

echo [API] No responde. Revisando si otro contenedor tiene el puerto 8090...
call :LiberarPuerto8090

where dotnet >nul 2>&1
if errorlevel 1 (
  echo [API] No se encontro dotnet. Instala el SDK de .NET 10.
  exit /b 1
)

echo [API] Compilando e iniciando...
start "Urban Park API" /D "%~dp0UrbanPark.Api" cmd /k dotnet run --launch-profile http

set /a N=0
:bucle_api
set /a N+=1
if !N! GTR 45 (
  echo [API] No respondio en http://127.0.0.1:8090. Mira la ventana "Urban Park API".
  exit /b 1
)
call :HttpOk "http://127.0.0.1:8090/"
if errorlevel 1 (
  ping -n 4 127.0.0.1 >nul
  goto bucle_api
)
echo [API] Responde en http://127.0.0.1:8090
exit /b 0

REM ------------------------------------------------------------------ comprobacion final
:ValidarTodo
echo.
echo Comprobando que cada servicio responda...
call :ApiUrbanPark
if errorlevel 1 (
  echo [FALLO] http://127.0.0.1:8090 no responde como Urban Park.
  exit /b 1
)
call :HttpOk "http://localhost:8090/"
if errorlevel 1 (
  echo [FALLO] localhost:8090 no responde. La aplicacion de Windows no va a poder entrar.
  exit /b 1
)
docker exec urbanpark-mysql mysql -uroot -purban123 -N -e "SELECT 1 FROM urbanpark.usuarios LIMIT 1;" >nul 2>&1
if errorlevel 1 (
  echo [FALLO] MySQL no tiene la base urbanpark.
  exit /b 1
)
echo [OK] API, localhost y MySQL responden.
exit /b 0

REM ------------------------------------------------------------------ App Windows
:AppWindows
tasklist /FI "IMAGENAME eq UrbanPark.App.exe" | find /I "UrbanPark.App.exe" >nul
if not errorlevel 1 (
  echo [App] Ya esta abierta. Cierra el error y vuelve a entrar.
  exit /b 0
)
set "EXE=%~dp0UrbanPark.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\UrbanPark.App.exe"
if exist "%EXE%" (
  echo [App] Abriendo Urban Park...
  start "" "%EXE%"
) else (
  echo [App] Aun no esta compilada. En Visual Studio pulsa F5 sobre UrbanPark.App.
)
exit /b 0

REM Si otro contenedor publico el 8090, el API de Urban Park no puede enlazar.
:LiberarPuerto8090
for /f "delims=" %%L in ('docker ps --format "{{.Names}}|{{.Ports}}"') do (
  echo %%L | findstr /C:":8090->" >nul
  if not errorlevel 1 (
    for /f "tokens=1 delims=|" %%N in ("%%L") do (
      echo [API] Deteniendo %%N porque ocupa el puerto 8090.
      docker stop %%N >nul
    )
  )
)
exit /b 0

REM ApiUrbanPark -> 0 solo si el cuerpo es el de esta API.
:ApiUrbanPark
powershell -NoProfile -Command "try { $r = Invoke-WebRequest -Uri 'http://127.0.0.1:8090/' -UseBasicParsing -TimeoutSec 4; if ($r.Content -match 'Urban Park') { exit 0 } else { exit 1 } } catch { exit 1 }"
exit /b %errorlevel%

REM HttpOk URL  -> errorlevel 0 si contesta, 1 si no.
:HttpOk
powershell -NoProfile -Command "try { $r = Invoke-WebRequest -Uri '%~1' -UseBasicParsing -TimeoutSec 4; if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 500) { exit 0 } else { exit 1 } } catch { exit 1 }"
exit /b %errorlevel%
