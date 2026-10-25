# ============================================================================
#  Copia el proyecto terminado a C:\moviles\Parqueadero
#  Excluye carpetas de compilación (bin, obj, .vs, caché de NuGet local).
#
#  Uso:  powershell -ExecutionPolicy Bypass -File .\copiar_a_moviles.ps1
# ============================================================================

param(
    [string]$Origen  = "C:\Users\pc-casa\Documents\ISRAEL\tareas deepseek\Parqueadero",
    [string]$Destino = "C:\moviles\Parqueadero"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Origen)) {
    throw "No existe el proyecto de origen: $Origen"
}

Write-Host "Origen : $Origen"
Write-Host "Destino: $Destino"
Write-Host ""

New-Item -ItemType Directory -Path $Destino -Force | Out-Null

# /E  subdirectorios (incluidos vacíos)   /XD excluir carpetas   /XF excluir archivos
# /NFL /NDL /NJH /NJS /NP  -> salida limpia
$argumentos = @(
    $Origen, $Destino,
    "/E",
    "/XD", "bin", "obj", ".vs", ".dotnet_home", "node_modules",
    "/XF", "*.user", "*.suo",
    "/NFL", "/NDL", "/NJH", "/NJS", "/NP"
)

$resultado = robocopy @argumentos
$codigo = $LASTEXITCODE

# robocopy devuelve 0-7 cuando todo ha ido bien
if ($codigo -ge 8) {
    throw "robocopy falló con código $codigo"
}

Write-Host ""
Write-Host "Copia completada (código robocopy: $codigo)."
Write-Host ""
Write-Host "Contenido de $Destino :"
Get-ChildItem -LiteralPath $Destino | Select-Object Mode, Name | Format-Table -AutoSize
