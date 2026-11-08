@echo off
chcp 65001 >nul
setlocal EnableExtensions EnableDelayedExpansion
title Urban Park - Compilar y reiniciar la API

rem ============================================================
rem  Detiene la API, compila y la vuelve a arrancar.
rem  Evita el error "el archivo esta bloqueado por UrbanPark.Api".
rem
rem  Uso:
rem    reiniciar-api.bat              compila y arranca la API
rem    reiniciar-api.bat /solo        solo compila (no arranca)
rem    reiniciar-api.bat /detener     solo detiene la API
rem    agregar /nopause              para no esperar una tecla al final
rem ============================================================

cd /d "%~dp0"

set PROYECTO=UrbanPark.Api\UrbanPark.Api.csproj
set PUERTO=8090
set SINPAUSA=0
if /I "%~1"=="/nopause" set SINPAUSA=1
if /I "%~2"=="/nopause" set SINPAUSA=1

echo ============================================================
echo   URBAN PARK - Reiniciar API
echo ============================================================
echo.

rem ---------- 1) validar que estamos en la carpeta correcta ----------
if not exist "%PROYECTO%" (
  echo [ERROR] No se encontro %PROYECTO%
  echo         Ejecuta este archivo desde la carpeta del proyecto.
  goto fin
)

rem ---------- 2) detener la API (proceso y puerto) ----------
echo [1/3] Deteniendo la API...

for /f "tokens=5" %%P in ('netstat -ano ^| findstr /R /C:"LISTENING" ^| findstr ":%PUERTO%"') do (
  call :MatarProceso %%P
)

taskkill /IM UrbanPark.Api.exe /F >nul 2>&1
if not errorlevel 1 echo       Proceso UrbanPark.Api.exe detenido.

ping -n 2 127.0.0.1 >nul

if /I "%~1"=="/detener" goto detenido

rem ---------- 3) compilar ----------
echo [2/3] Compilando la API...
dotnet build "%PROYECTO%" -v q --nologo
if errorlevel 1 (
  echo.
  echo [ERROR] La compilacion fallo. Revisa los mensajes de arriba.
  goto fin
)
echo       Compilacion correcta.

if /I "%~1"=="/solo" goto compilado

rem ---------- 4) arrancar ----------
echo [3/3] Arrancando la API en el puerto %PUERTO%...
set LOG=%TEMP%\urbanpark-api.log
start "UrbanPark API" /min cmd /c "dotnet run --project ""%PROYECTO%"" --no-build > ""%LOG%"" 2>&1"

rem Espera a que el puerto responda (maximo 30 s)
set INTENTOS=0
:esperar
ping -n 2 127.0.0.1 >nul
set /a INTENTOS+=1
netstat -ano | findstr /R /C:"LISTENING" | findstr ":%PUERTO%" >nul
if not errorlevel 1 goto arriba
if %INTENTOS% GEQ 15 (
  echo.
  echo [AVISO] La API no respondio en el puerto %PUERTO% despues de 30 s.
  echo         Ultimas lineas del registro:
  if exist "%LOG%" (
    powershell -NoProfile -Command "Get-Content '%LOG%' -Tail 12"
  ) else (
    echo         No se creo el registro %LOG%
  )
  echo.
  echo         Tambien puedes ejecutarla a mano:
  echo             dotnet run --project %PROYECTO%
  goto fin
)
goto esperar

:arriba
echo.
echo ============================================================
echo   LISTO. API escuchando en http://127.0.0.1:%PUERTO%
echo   Swagger: http://127.0.0.1:%PUERTO%/swagger
echo   Registro: %LOG%
echo ============================================================
goto fin

:detenido
echo.
echo ============================================================
echo   API detenida. El puerto %PUERTO% quedo libre.
echo ============================================================
goto fin

:compilado
echo.
echo ============================================================
echo   Compilacion lista. La API NO se arranco (/solo).
echo ============================================================
goto fin

rem ---------- rutinas ----------
:MatarProceso
set PID=%~1
if "%PID%"=="" goto :eof
if "%PID%"=="0" goto :eof
taskkill /PID %PID% /F >nul 2>&1
if not errorlevel 1 echo       Puerto %PUERTO% liberado (PID %PID%).
goto :eof

:fin
echo.
if "%SINPAUSA%"=="1" goto :eof
pause
endlocal
