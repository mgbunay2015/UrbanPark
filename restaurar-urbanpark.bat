@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
title Urban Park - restaurar base

set "SQL=%~dp0database\urbanpark_actual.sql"

echo.
echo ============================================
echo   Urban Park - borrar y cargar la base
echo ============================================
echo.
echo Este proceso ELIMINA la base urbanpark
echo y la vuelve a crear desde:
echo   %SQL%
echo.

if not exist "%SQL%" (
  echo No se encontro el script database\urbanpark_actual.sql
  pause
  exit /b 1
)

set /p RESP=Escribe S para continuar: 
if /I not "%RESP%"=="S" (
  echo Cancelado.
  pause
  exit /b 0
)

call :DockerYMySql
if errorlevel 1 goto fin_error

echo.
echo [1/2] Eliminando la base urbanpark...
docker exec urbanpark-mysql mysql -uroot -purban123 -e "DROP DATABASE IF EXISTS urbanpark;"
if errorlevel 1 (
  echo No se pudo eliminar la base.
  goto fin_error
)

echo [2/2] Cargando estructura y datos...
docker exec -i urbanpark-mysql mysql -uroot -purban123 --default-character-set=utf8mb4 < "%SQL%"
if errorlevel 1 (
  echo No se pudo cargar el script.
  goto fin_error
)

echo.
echo Base restaurada. Registros actuales:
docker exec urbanpark-mysql mysql -uroot -purban123 -N -e "SELECT 'usuarios', COUNT(*) FROM urbanpark.usuarios UNION ALL SELECT 'espacios', COUNT(*) FROM urbanpark.espacios UNION ALL SELECT 'vehiculos', COUNT(*) FROM urbanpark.vehiculos UNION ALL SELECT 'tickets', COUNT(*) FROM urbanpark.tickets;"
echo.
echo Listo.
pause
exit /b 0

:fin_error
echo.
echo La restauracion no termino.
pause
exit /b 1

:DockerYMySql
docker info >nul 2>&1
if not errorlevel 1 goto mysql_contenedor
echo Docker no responde. Abriendo Docker Desktop...
if not exist "C:\Program Files\Docker\Docker\Docker Desktop.exe" (
  echo No esta instalado Docker Desktop.
  exit /b 1
)
start "" "C:\Program Files\Docker\Docker\Docker Desktop.exe"
set /a N=0
:esperar_docker
set /a N+=1
if !N! GTR 36 (
  echo Docker no quedo listo.
  exit /b 1
)
ping -n 6 127.0.0.1 >nul
docker info >nul 2>&1
if errorlevel 1 goto esperar_docker

:mysql_contenedor
docker container inspect urbanpark-mysql >nul 2>&1
if not errorlevel 1 goto encender_mysql
echo No existe el contenedor urbanpark-mysql.
echo Ejecuta primero iniciar-urbanpark.bat para crearlo.
exit /b 1

:encender_mysql
docker start urbanpark-mysql >nul 2>&1
set /a N=0
:esperar_mysql
set /a N+=1
if !N! GTR 40 (
  echo MySQL no respondio.
  exit /b 1
)
docker exec urbanpark-mysql mysqladmin ping -uroot -purban123 --silent >nul 2>&1
if errorlevel 1 (
  ping -n 4 127.0.0.1 >nul
  goto esperar_mysql
)
exit /b 0
