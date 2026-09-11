param(
    [string]$SourceFolder = $PSScriptRoot,
    [string]$InstallDir = "$env:ProgramFiles\BisApi",
    [string]$ServiceName = "BisApi"
)

$ErrorActionPreference = "Stop"

function Assert-Administrator {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Abra o PowerShell como Administrador para instalar o serviço."
    }
}

Assert-Administrator
$SourceFolder = (Resolve-Path $SourceFolder).Path
$sourceExe = Join-Path $SourceFolder "BisApi.exe"
if (-not (Test-Path $sourceExe)) { throw "BisApi.exe não encontrado em $SourceFolder" }
if (-not (Test-Path (Join-Path $SourceFolder "AcsReader.dll"))) { throw "AcsReader.dll PC/SC não encontrado no pacote." }

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Service -Name $ServiceName).Status -ne 'Stopped' -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
}

$backupLocal = $null
$installedLocal = Join-Path $InstallDir "appsettings.Local.json"
if (Test-Path $installedLocal) {
    $backupLocal = Join-Path $env:TEMP "bis_api_appsettings.Local.$([guid]::NewGuid().ToString('N')).json"
    Copy-Item $installedLocal $backupLocal -Force
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Get-ChildItem $SourceFolder -Force | ForEach-Object {
    if ($_.Name -ne "appsettings.Local.json") {
        Copy-Item $_.FullName $InstallDir -Recurse -Force
    }
}

if ($backupLocal) {
    Copy-Item $backupLocal $installedLocal -Force
    Remove-Item $backupLocal -Force
} elseif (Test-Path (Join-Path $SourceFolder "appsettings.Local.json")) {
    Copy-Item (Join-Path $SourceFolder "appsettings.Local.json") $installedLocal -Force
}

$exe = Join-Path $InstallDir "BisApi.exe"
if (-not $existing) {
    New-Service -Name $ServiceName `
        -BinaryPathName ('"{0}"' -f $exe) `
        -DisplayName "BIS API - Be-Tech / ACR122" `
        -Description "Serviço local standalone para emissão e diagnóstico de cartões Be-Tech/Saga via ACR122 PC/SC." `
        -StartupType Automatic | Out-Null
} else {
    sc.exe config $ServiceName binPath= ('"{0}"' -f $exe) start= auto | Out-Null
}

sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
Start-Service -Name $ServiceName
Start-Sleep -Seconds 2
$svc = Get-Service -Name $ServiceName

Write-Host "Serviço instalado: $($svc.Name) / $($svc.Status)" -ForegroundColor Green
Write-Host "Dashboard: http://127.0.0.1:8765" -ForegroundColor Cyan
Write-Host "Config local: $installedLocal" -ForegroundColor Cyan
