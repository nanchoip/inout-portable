<#
    publish.ps1 — Genera el artefacto portable "doble click" (un único .exe).

    Produce dist\InoutPortable\InoutPortable.exe : self-contained + single-file,
    sin dependencia de .NET instalado en la máquina destino (solo Windows x64).

    Uso:
        powershell -ExecutionPolicy Bypass -File build\publish.ps1
#>

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\InoutPortable.App\InoutPortable.App.csproj"
$outDir = Join-Path $root "dist\InoutPortable"

# Localiza dotnet (PATH o ruta por defecto de la instalación).
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "No se encontró 'dotnet'. Instala el .NET 8 SDK." }

Write-Host "==> Limpiando salida anterior..." -ForegroundColor Cyan
if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }

Write-Host "==> Publicando ($Configuration / $Runtime, self-contained, single-file)..." -ForegroundColor Cyan
& $dotnet publish $proj `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $outDir

if ($LASTEXITCODE -ne 0) { throw "La publicación falló (código $LASTEXITCODE)." }

# Copia recursos de ayuda junto al ejecutable.
$samples = Join-Path $root "samples"
if (Test-Path $samples) {
    Copy-Item $samples (Join-Path $outDir "ejemplos") -Recurse -Force
}

$exe = Join-Path $outDir "InoutPortable.exe"
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)

Write-Host ""
Write-Host "==> Listo." -ForegroundColor Green
Write-Host "    Ejecutable: $exe ($size MB)"
Write-Host "    Carpeta a distribuir: $outDir"
Write-Host "    (comprime esa carpeta en un .zip y entrégala; se ejecuta con doble click)."
