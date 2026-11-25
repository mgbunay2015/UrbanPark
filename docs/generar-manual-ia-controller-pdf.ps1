# Genera docs/MANUAL_IA_CONTROLLER.pdf desde docs/MANUAL_IA_CONTROLLER.md
$ErrorActionPreference = "Stop"
$raiz = Split-Path $PSScriptRoot -Parent
$pdf = Join-Path $raiz "docs\MANUAL_IA_CONTROLLER.pdf"
Set-Location $raiz

$argsExport = @(
    "MANUAL_IA_CONTROLLER.md",
    "MANUAL_IA_CONTROLLER.pdf",
    "Manual IaController.cs",
    "Línea a línea · api/ia · Urban Park"
)

dotnet run --project (Join-Path $raiz "tools\ExportarManual\ExportarManual.csproj") -- @argsExport
if ($LASTEXITCODE -eq 0 -and (Test-Path $pdf)) {
    Write-Host "Listo: $pdf" -ForegroundColor Green
    exit 0
}

$htmlDir = Get-ChildItem (Join-Path $raiz "tools\ExportarManual\salida") -Directory |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($null -eq $htmlDir) { Write-Error "No se generó HTML." }
$html = Join-Path $htmlDir.FullName "MANUAL_IA_CONTROLLER.html"
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
if (-not (Test-Path $edge)) { $edge = "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe" }
if (-not (Test-Path $edge)) {
    Write-Host "Abre $html y usa Imprimir -> Guardar como PDF." -ForegroundColor Yellow
    exit 2
}

$uri = [Uri]::new((Resolve-Path -LiteralPath $html).Path).AbsoluteUri
& $edge --headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf="$pdf" $uri
Start-Sleep -Seconds 4
if (Test-Path $pdf) {
    Write-Host "Listo: $pdf" -ForegroundColor Green
    exit 0
}
Write-Host "Abre $html y usa Imprimir -> Guardar como PDF." -ForegroundColor Yellow
exit 2