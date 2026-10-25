@echo off
chcp 65001 >nul
title Urban Park - Subir el proyecto a GitHub

rem ============================================================
rem  Sube este proyecto a GitHub (crea el repositorio si no existe).
rem
rem  Antes de ejecutarlo, revisa estas tres lineas:
rem    GITHUB_USUARIO : tu usuario de GitHub
rem    REPO           : nombre del repositorio
rem    VISIBILIDAD    : --private (privado) o --public (publico)
rem ============================================================

set GITHUB_USUARIO=mgbunay2015
set REPO=UrbanPark
set VISIBILIDAD=--public
set RAMA=main
set MENSAJE=Urban Park - app MAUI + API .NET + MySQL

cd /d "%~dp0"

echo ============================================================
echo   URBAN PARK - Subir a GitHub
echo   Repositorio: %GITHUB_USUARIO%/%REPO%   ^(%VISIBILIDAD%^)
echo ============================================================
echo.

rem ---------- 1) comprobar git ----------
where git >nul 2>nul
if errorlevel 1 goto :sin_git

git config --global core.longpaths true

rem ---------- 2) salir si es la carpeta equivocada ----------
if not exist "UrbanPark.App" goto :carpeta_incorrecta

rem ---------- 3) iniciar repositorio si hace falta ----------
if exist ".git" goto :repo_listo

echo [1/5] Iniciando repositorio local...
git init -b %RAMA% >nul
if errorlevel 1 git init >nul

:repo_listo

rem ---------- 4) identidad ----------
for /f "delims=" %%i in ('git config user.name') do set NOMBRE=%%i
if "%NOMBRE%"=="" (
    echo [2/5] Configurando nombre y correo de git...
    for /f "delims=" %%u in ('gh api user --jq .login 2^>nul') do set GITHUB_USUARIO=%%u
    for /f "delims=" %%e in ('gh api user --jq .email 2^>nul') do set CORREO=%%e
    if "%CORREO%"=="" set CORREO=%GITHUB_USUARIO%@users.noreply.github.com
    git config user.name "%GITHUB_USUARIO%"
    git config user.email "%CORREO%"
) else (
    echo [2/5] Identidad de git: %NOMBRE%
)

rem ---------- 5) que no se suba basura ----------
if not exist ".gitignore" goto :sin_gitignore

echo [3/5] Preparando archivos (sin bin, obj ni .vs)...
git add -A
if errorlevel 1 goto :error_add

rem ---------- 6) commit ----------
git diff --cached --quiet
if not errorlevel 1 goto :sin_cambios

echo [4/5] Creando el commit...
git commit -m "%MENSAJE%" >nul
if errorlevel 1 goto :error_commit
goto :remoto

:sin_cambios
echo [4/5] No hay cambios nuevos que confirmar.

rem ---------- 7) remoto y push ----------
:remoto
git remote get-url origin >nul 2>nul
if errorlevel 1 (
    echo [5/5] Conectando con GitHub...
    where gh >nul 2>nul
    if errorlevel 1 goto :push_simple

    gh auth status >nul 2>nul
    if errorlevel 1 goto :push_simple

    gh repo view %GITHUB_USUARIO%/%REPO% >nul 2>nul
    if errorlevel 1 (
        echo     El repositorio no existe: se crea ahora ^(%VISIBILIDAD%^).
        gh repo create %GITHUB_USUARIO%/%REPO% %VISIBILIDAD% --source=. --remote=origin --push
        if errorlevel 1 goto :error_push
        goto :listo
    )

    git remote add origin https://github.com/%GITHUB_USUARIO%/%REPO%.git
    goto :push
) else (
    echo [5/5] Subiendo cambios a GitHub...
    goto :push
)

:push_simple
echo     No se encontro GitHub CLI autenticado. Se usara git normal.
git remote get-url origin >nul 2>nul
if errorlevel 1 git remote add origin https://github.com/%GITHUB_USUARIO%/%REPO%.git

:push
git push -u origin %RAMA%
if errorlevel 1 goto :error_push

:listo
echo.
echo ============================================================
echo   LISTO. Proyecto subido a:
echo   https://github.com/%GITHUB_USUARIO%/%REPO%
echo ============================================================
echo.
echo Si el repositorio es nuevo, entra a GitHub para comprobar
echo que no se subieron las carpetas bin, obj ni .vs.
goto :fin

rem ---------- errores ----------
:sin_git
echo [ERROR] No se encontro git en el PATH.
echo         Instala Git para Windows: https://git-scm.com/download/win
goto :fin

:carpeta_incorrecta
echo [ERROR] Este archivo debe estar en la carpeta del proyecto
echo         (donde estan UrbanPark.App y UrbanPark.Api).
goto :fin

:sin_gitignore
echo [ERROR] Falta el archivo .gitignore en esta carpeta.
echo         Sin el se subirian bin, obj y .vs (cientos de MB).
goto :fin

:error_add
echo [ERROR] No se pudieron preparar los archivos (git add).
goto :fin

:error_commit
echo [ERROR] No se pudo crear el commit.
echo         Revisa tu nombre y correo con:  git config --global user.name
goto :fin

:error_push
echo.
echo [ERROR] No se pudo subir el proyecto.
echo.
echo   Si el repositorio no existe, crealo en https://github.com/new
echo   con el nombre "%REPO%" y vuelve a ejecutar este archivo.
echo.
echo   Si pide usuario y contrasena, usa un token personal (PAT):
echo   https://github.com/settings/tokens  -^>  "Generate new token (classic)"
echo   con el permiso "repo", pegalo como contrasena.
echo.
echo   O inicia sesion en GitHub CLI y vuelve a intentarlo:
echo       gh auth login
goto :fin

:fin
echo.
pause
