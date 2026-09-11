param(
    [string]$ServiceName = "BisApi",
    [switch]$RemoveFiles,
    [string]$InstallDir = "$env:ProgramFiles\BisApi"
)

$ErrorActionPreference = "Stop"
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Serviço $ServiceName removido." -ForegroundColor Green
}
if ($RemoveFiles -and (Test-Path $InstallDir)) {
    Remove-Item $InstallDir -Recurse -Force
    Write-Host "Arquivos removidos: $InstallDir" -ForegroundColor Green
}
