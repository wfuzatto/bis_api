param(
    [Parameter(Mandatory = $true)]
    [string]$PmsSagaFolder,
    [string]$DestinationFolder = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not $DestinationFolder) {
    $DestinationFolder = Join-Path $Root "src\BisApi.Web\vendor\x86"
}

$codec = Join-Path $PmsSagaFolder "btlock57L.dll"
if (-not (Test-Path $codec)) { throw "btlock57L.dll não encontrada em: $PmsSagaFolder" }
New-Item -ItemType Directory -Force -Path $DestinationFolder | Out-Null
Copy-Item $codec (Join-Path $DestinationFolder "btlock57L.dll") -Force

$dataDll = Join-Path $PmsSagaFolder "Data.dll"
if (Test-Path $dataDll) { Copy-Item $dataDll (Join-Path $DestinationFolder "Data.dll") -Force }

Write-Host "Codec Be-Tech instalado em $DestinationFolder" -ForegroundColor Green
Write-Host "IMPORTANTE: o AcsReader.dll original do PMS NÃO foi copiado. O destino deve usar o shim PC/SC do bis_api." -ForegroundColor Yellow
