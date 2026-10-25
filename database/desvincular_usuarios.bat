@echo off
chcp 65001 >nul
title Urban Park - Desvincular usuarios

rem ============================================================
rem  Urban Park - desvinculacion de usuarios
rem  Quita el autenticador de Google (TOTP) y borra los equipos
rem  vinculados, para que todos vuelvan a entrar solo con
rem  usuario y contrasena.
rem
rem  Uso:
rem    desvincular_usuarios.bat              - todos los usuarios
rem    desvincular_usuarios.bat cliente1     - solo ese usuario
rem    desvincular_usuarios.bat cliente1 /y  - sin preguntar
rem    desvincular_usuarios.bat /y           - todos, sin preguntar
rem ============================================================

set CONTENEDOR=urbanpark-mysql
set MYSQL_USER=root
set MYSQL_PASS=urban123
set BASE=urbanpark

set USUARIO=%~1
set CONFIRMAR=%~2
if /i "%USUARIO%"=="/y" set CONFIRMAR=/y
if /i "%USUARIO%"=="/y" set USUARIO=

set DETALLE=todos los usuarios
if not "%USUARIO%"=="" set DETALLE=el usuario %USUARIO%

echo ============================================================
echo   URBAN PARK - Desvinculacion de autenticadores
echo ============================================================
echo.

rem ---------- 1) comprobar Docker ----------
where docker >nul 2>nul
if errorlevel 1 goto :sin_docker

set ESTADO_FILE=%TEMP%\urbanpark_estado.txt
if exist "%ESTADO_FILE%" del "%ESTADO_FILE%" >nul 2>nul
docker inspect --format "{{.State.Running}}" %CONTENEDOR% > "%ESTADO_FILE%" 2>nul
set ESTADO=
if exist "%ESTADO_FILE%" set /p ESTADO=<"%ESTADO_FILE%"
if exist "%ESTADO_FILE%" del "%ESTADO_FILE%" >nul 2>nul
if /i not "%ESTADO%"=="true" goto :sin_contenedor

rem ---------- 2) confirmar ----------
if /i "%CONFIRMAR%"=="/y" goto :ejecutar

echo Se van a desvincular: %DETALLE%
echo   - Se borra su autenticador de Google (TOTP)
echo   - Se borran los equipos vinculados
echo.
set RESPUESTA=
set /p RESPUESTA=Escribe S para continuar:
if /i not "%RESPUESTA%"=="S" goto :cancelado

:ejecutar

rem ---------- 3) preparar el SQL ----------
set ARCHIVO_SQL=%TEMP%\urbanpark_desvincular.sql
if exist "%ARCHIVO_SQL%" del "%ARCHIVO_SQL%" >nul 2>nul

if "%USUARIO%"=="" echo SET @usuario = NULL;> "%ARCHIVO_SQL%"
if not "%USUARIO%"=="" echo SET @usuario = '%USUARIO%';> "%ARCHIVO_SQL%"

echo SELECT '=== ANTES ===' AS paso;>> "%ARCHIVO_SQL%"
echo SELECT username, enabled, totp_activo FROM usuarios WHERE (@usuario IS NULL OR username = @usuario) ORDER BY username;>> "%ARCHIVO_SQL%"
echo UPDATE usuarios SET totp_secret = NULL, totp_activo = 0 WHERE (@usuario IS NULL OR username = @usuario) AND (totp_secret IS NOT NULL OR totp_activo ^<^> 0);>> "%ARCHIVO_SQL%"
echo SELECT CONCAT('Autenticadores desactivados: ', ROW_COUNT()) AS resultado;>> "%ARCHIVO_SQL%"
echo DELETE FROM dispositivos WHERE (@usuario IS NULL OR usuario = @usuario);>> "%ARCHIVO_SQL%"
echo SELECT CONCAT('Equipos desvinculados: ', ROW_COUNT()) AS resultado;>> "%ARCHIVO_SQL%"
echo SELECT '=== DESPUES ===' AS paso;>> "%ARCHIVO_SQL%"
echo SELECT username, enabled, totp_activo FROM usuarios WHERE (@usuario IS NULL OR username = @usuario) ORDER BY username;>> "%ARCHIVO_SQL%"
echo SELECT CONCAT('Usuarios con autenticador activo: ', (SELECT COUNT(*) FROM usuarios WHERE totp_activo ^<^> 0)) AS resumen;>> "%ARCHIVO_SQL%"

rem ---------- 4) ejecutar ----------
echo.
echo Ejecutando en el contenedor %CONTENEDOR% ...
echo.

docker exec -e MYSQL_PWD=%MYSQL_PASS% -i %CONTENEDOR% mysql -u%MYSQL_USER% --table %BASE% < "%ARCHIVO_SQL%"
if errorlevel 1 goto :error_sql

del "%ARCHIVO_SQL%" >nul 2>nul

echo.
echo ============================================================
echo   LISTO. Los usuarios desvinculados ya entran solo con
echo   usuario y contrasena: su autenticador de Google quedo
echo   desactivado y sus equipos borrados.
echo ============================================================
echo.
echo Si la app sigue mostrando "Hola, usuario" con las dos
echo opciones, cierrala y vuelve a abrirla: al preguntar al
echo API vera que el autenticador ya no esta activo.
goto :fin

:sin_docker
echo [ERROR] No se encontro Docker en el PATH.
echo         Abre Docker Desktop y vuelve a intentarlo.
goto :fin

:sin_contenedor
echo [ERROR] El contenedor "%CONTENEDOR%" no esta corriendo.
echo         Levantalo con:  docker start %CONTENEDOR%
goto :fin

:error_sql
del "%ARCHIVO_SQL%" >nul 2>nul
echo.
echo [ERROR] No se pudo ejecutar el SQL. Revisa que el contenedor
echo         tenga la base "%BASE%" y que la contrasena sea correcta.
goto :fin

:cancelado
echo.
echo Cancelado. No se cambio nada.
goto :fin

:fin
echo.
pause
