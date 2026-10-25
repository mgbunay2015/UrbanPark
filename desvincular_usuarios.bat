@echo off
chcp 65001 >nul
title Urban Park - Desvincular a TODOS los usuarios

rem ============================================================
rem  Doble clic aqui para desvincular a TODOS los usuarios:
rem  quita su autenticador de Google y borra sus equipos.
rem
rem  Llama al script completo de la carpeta database, que muestra
rem  la tabla de antes/despues y controla los errores.
rem ============================================================

cd /d "%~dp0"

if not exist "database\desvincular_usuarios.bat" goto :sin_script

echo.
echo  Se van a desvincular TODOS los usuarios en 3 segundos...
echo  (cierra esta ventana si no es lo que quieres)
echo.
timeout /t 3 /nobreak >nul

call "database\desvincular_usuarios.bat" /y
goto :fin

:sin_script
echo [ERROR] No se encontro database\desvincular_usuarios.bat
echo         Ejecuta este archivo desde la carpeta del proyecto.
echo.
pause

:fin
