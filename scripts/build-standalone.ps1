param(
    [string]$OutputDir = "",
    [string]$PmsSagaFolder = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $Root "publish\standalone-win-x86" }
$nativeOut = Join-Path $Root "artifacts\native-win-x86"

& (Join-Path $PSScriptRoot "build-native-shim.ps1") -OutputDir $nativeOut

if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
dotnet restore (Join-Path $Root "src\BisApi.Web\BisApi.Web.csproj")
dotnet publish (Join-Path $Root "src\BisApi.Web\BisApi.Web.csproj") `
    -c Release -r win-x86 --self-contained true -o $OutputDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou." }

Copy-Item (Join-Path $nativeOut "AcsReader.dll") (Join-Path $OutputDir "AcsReader.dll") -Force

if ($PmsSagaFolder) {
    & (Join-Path $PSScriptRoot "install-vendor-codec.ps1") -PmsSagaFolder $PmsSagaFolder -DestinationFolder $OutputDir
}

Copy-Item (Join-Path $PSScriptRoot "install-service.ps1") $OutputDir -Force
Copy-Item (Join-Path $PSScriptRoot "uninstall-service.ps1") $OutputDir -Force
Copy-Item (Join-Path $PSScriptRoot "install-vendor-codec.ps1") $OutputDir -Force

$local = Join-Path $OutputDir "appsettings.Local.json"
if (-not (Test-Path $local)) {
    Copy-Item (Join-Path $Root "src\BisApi.Web\appsettings.Local.example.json") $local
}

Write-Host ""
Write-Host "Standalone pronto: $OutputDir" -ForegroundColor Green
Write-Host "1) Confira appsettings.Local.json" -ForegroundColor Cyan
Write-Host "2) Se btlock57L.dll ainda não estiver no diretório, execute install-vendor-codec.ps1" -ForegroundColor Cyan
Write-Host "3) Teste .\BisApi.exe e abra http://127.0.0.1:8765" -ForegroundColor Cyan
Write-Host "4) Depois execute install-service.ps1 como Administrador" -ForegroundColor Cyan
