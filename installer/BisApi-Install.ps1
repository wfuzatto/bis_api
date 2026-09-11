[CmdletBinding()]
param(
    [string]$PackagePath,
    [string]$VendorFolder,
    [switch]$NoOpenDashboard
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Test-Administrator {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-SelfElevated {
    if (Test-Administrator) { return }

    Write-Host 'Solicitando permissao de Administrador...' -ForegroundColor Yellow
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    if ($PackagePath) { $arguments += " -PackagePath `"$PackagePath`"" }
    if ($VendorFolder) { $arguments += " -VendorFolder `"$VendorFolder`"" }
    if ($NoOpenDashboard) { $arguments += ' -NoOpenDashboard' }

    $process = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $process.ExitCode
}

function Wait-Api {
    param([int]$Seconds = 30)

    $until = (Get-Date).AddSeconds($Seconds)
    do {
        try {
            return Invoke-RestMethod 'http://127.0.0.1:8765/api/health' -TimeoutSec 2
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    } while ((Get-Date) -lt $until)

    throw 'A API nao respondeu em http://127.0.0.1:8765.'
}

function Ensure-JsonProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        $Value
    )

    if ($Object.PSObject.Properties.Name -contains $Name) {
        $Object.$Name = $Value
    }
    else {
        $Object | Add-Member -MemberType NoteProperty -Name $Name -Value $Value
    }
}

function Set-SafeConfig {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (Test-Path $Path) {
        $cfg = Get-Content -Raw $Path | ConvertFrom-Json
    }
    else {
        $cfg = [pscustomobject]@{}
    }

    if (-not ($cfg.PSObject.Properties.Name -contains 'BisApi') -or $null -eq $cfg.BisApi) {
        Ensure-JsonProperty $cfg 'BisApi' ([pscustomobject]@{})
    }
    if (-not ($cfg.PSObject.Properties.Name -contains 'BeTech57') -or $null -eq $cfg.BeTech57) {
        Ensure-JsonProperty $cfg 'BeTech57' ([pscustomobject]@{})
    }

    Ensure-JsonProperty $cfg.BisApi 'Url' 'http://127.0.0.1:8765'
    Ensure-JsonProperty $cfg.BisApi 'EnableHotelCardWrites' $false
    Ensure-JsonProperty $cfg.BisApi 'RequireWriteChallenge' 'GRAVAR'

    if (-not ($cfg.BeTech57.PSObject.Properties.Name -contains 'PcscReader')) {
        Ensure-JsonProperty $cfg.BeTech57 'PcscReader' ''
    }
    if (-not ($cfg.BeTech57.PSObject.Properties.Name -contains 'HotelPassword')) {
        Ensure-JsonProperty $cfg.BeTech57 'HotelPassword' ''
    }
    elseif ([string]$cfg.BeTech57.HotelPassword -eq 'COLOQUE_AQUI_OS_6_DIGITOS') {
        $cfg.BeTech57.HotelPassword = ''
    }

    $cfg | ConvertTo-Json -Depth 10 | Set-Content -Path $Path -Encoding UTF8
}

function Ensure-SmartCardService {
    $service = Get-Service -Name 'SCardSvr' -ErrorAction Stop
    $serviceInfo = Get-CimInstance Win32_Service -Filter "Name='SCardSvr'" -ErrorAction SilentlyContinue
    if ($serviceInfo -and $serviceInfo.StartMode -eq 'Disabled') {
        Set-Service -Name 'SCardSvr' -StartupType Manual
    }
    if ($service.Status -ne 'Running') {
        Write-Host 'Iniciando servico Smart Card (SCardSvr)...' -ForegroundColor Cyan
        Start-Service -Name 'SCardSvr'
        $service = Get-Service -Name 'SCardSvr'
    }
    return $service
}

function Get-Acr122PnpDevices {
    if (-not (Get-Command Get-PnpDevice -ErrorAction SilentlyContinue)) { return @() }
    return @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
        $_.FriendlyName -match 'ACR122|ACS ACR122'
    })
}

function Find-VendorCodecFolder {
    param([string]$PreferredFolder)

    $installedDir = Join-Path $env:ProgramFiles 'BisApi'
    if ((Test-Path (Join-Path $installedDir 'btlock57L.dll')) -and (Test-Path (Join-Path $installedDir 'Data.dll'))) {
        return $installedDir
    }

    if ($PreferredFolder -and (Test-Path $PreferredFolder)) {
        if ((Test-Path (Join-Path $PreferredFolder 'btlock57L.dll')) -and (Test-Path (Join-Path $PreferredFolder 'Data.dll'))) {
            return (Resolve-Path $PreferredFolder).Path
        }
    }

    $directCandidates = @(
        (Join-Path $PSScriptRoot 'vendor'),
        'C:\BIS',
        'C:\Saga',
        'C:\Be-Tech',
        'C:\BTLock',
        (Join-Path $env:USERPROFILE 'Desktop'),
        (Join-Path $env:USERPROFILE 'Downloads')
    ) | Where-Object { $_ -and (Test-Path $_) }

    foreach ($root in $directCandidates) {
        try {
            $codec = Get-ChildItem -Path $root -Filter 'btlock57L.dll' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($codec -and (Test-Path (Join-Path $codec.DirectoryName 'Data.dll'))) {
                return $codec.DirectoryName
            }
        }
        catch { }
    }

    $programRoots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { $_ -and (Test-Path $_) }
    foreach ($root in $programRoots) {
        $candidateDirs = Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue | Where-Object {
            $_.Name -match 'BIS|Saga|Be-Tech|BTLock|PMS'
        }
        foreach ($dir in $candidateDirs) {
            try {
                $codec = Get-ChildItem -Path $dir.FullName -Filter 'btlock57L.dll' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($codec -and (Test-Path (Join-Path $codec.DirectoryName 'Data.dll'))) {
                    return $codec.DirectoryName
                }
            }
            catch { }
        }
    }

    return $null
}

function Copy-VendorCodec {
    param(
        [string]$VendorSource,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not $VendorSource) { return $false }
    Copy-Item (Join-Path $VendorSource 'btlock57L.dll') (Join-Path $Destination 'btlock57L.dll') -Force
    Copy-Item (Join-Path $VendorSource 'Data.dll') (Join-Path $Destination 'Data.dll') -Force
    return $true
}

Invoke-SelfElevated

Write-Host '=============================================' -ForegroundColor DarkCyan
Write-Host ' BIS API - Instalacao / Atualizacao Windows ' -ForegroundColor Cyan
Write-Host '=============================================' -ForegroundColor DarkCyan
Write-Host ''

$manifestPath = Join-Path $PSScriptRoot 'manifest.json'
if (-not (Test-Path $manifestPath)) {
    throw 'manifest.json nao encontrado. Baixe e extraia o pacote completo do GitHub Actions.'
}
$manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
if (-not $manifest.package.file -or -not $manifest.package.sha256 -or -not $manifest.shim.sha256) {
    throw 'manifest.json invalido ou incompleto.'
}

if ($PackagePath) {
    $package = (Resolve-Path $PackagePath).Path
}
else {
    $package = Join-Path $PSScriptRoot ([string]$manifest.package.file)
}
if (-not (Test-Path $package)) {
    throw "Pacote standalone nao encontrado: $package"
}

$actualPackageHash = (Get-FileHash $package -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedPackageHash = ([string]$manifest.package.sha256).ToLowerInvariant()
if ($actualPackageHash -ne $expectedPackageHash) {
    throw 'SHA-256 do pacote standalone nao corresponde ao manifest.json. Instalacao interrompida.'
}
Write-Host 'Pacote standalone: hash OK' -ForegroundColor Green

$smartCardService = Ensure-SmartCardService
$pnpDevices = Get-Acr122PnpDevices
if ($pnpDevices.Count -eq 0) {
    Write-Warning 'ACR122 nao foi encontrado no Plug and Play. O BisApi sera instalado em modo diagnostico.'
    Write-Warning 'NAO instale driver ACR120/RW-41. Se necessario, use somente driver PC/SC/CCID oficial do ACS ACR122U.'
}
else {
    Write-Host "ACR122 detectado pelo Windows: $($pnpDevices[0].FriendlyName)" -ForegroundColor Green
}

$source = Join-Path $env:TEMP 'BisApiStandalone'
if (Test-Path $source) {
    Remove-Item $source -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $source | Out-Null
Expand-Archive -LiteralPath $package -DestinationPath $source -Force

$required = @(
    'BisApi.exe',
    'AcsReader.dll',
    'appsettings.json',
    'appsettings.Local.json',
    'install-service.ps1',
    'uninstall-service.ps1',
    'wwwroot'
)
foreach ($name in $required) {
    if (-not (Test-Path (Join-Path $source $name))) {
        throw "Arquivo obrigatorio ausente no standalone: $name"
    }
}

$actualShimHash = (Get-FileHash (Join-Path $source 'AcsReader.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedShimHash = ([string]$manifest.shim.sha256).ToLowerInvariant()
if ($actualShimHash -ne $expectedShimHash) {
    throw 'SHA-256 invalido do AcsReader.dll PC/SC; instalacao interrompida.'
}
Write-Host 'AcsReader.dll PC/SC: hash OK' -ForegroundColor Green

$vendorSource = Find-VendorCodecFolder -PreferredFolder $VendorFolder
$vendorCopied = Copy-VendorCodec -VendorSource $vendorSource -Destination $source
if ($vendorCopied) {
    Write-Host 'Codec Be-Tech localizado e preparado para instalacao.' -ForegroundColor Green
}
else {
    Write-Warning 'btlock57L.dll + Data.dll nao foram localizados. O servico sera instalado, mas emissao Be-Tech ficara indisponivel ate instalar o codec localmente.'
}

Set-SafeConfig (Join-Path $source 'appsettings.Local.json')
& (Join-Path $source 'install-service.ps1') -SourceFolder $source

$installDir = Join-Path $env:ProgramFiles 'BisApi'
$installedConfig = Join-Path $installDir 'appsettings.Local.json'
Set-SafeConfig $installedConfig
Restart-Service -Name 'BisApi' -Force
$health = Wait-Api

$reader = $null
try {
    $readerResponse = Invoke-RestMethod 'http://127.0.0.1:8765/api/pcsc/readers' -TimeoutSec 10
    $reader = $readerResponse.readers | Where-Object { $_ -match 'ACR122' } | Select-Object -First 1
}
catch {
    Write-Warning "Nao foi possivel consultar leitores PC/SC: $($_.Exception.Message)"
}

if ($reader) {
    $cfg = Get-Content -Raw $installedConfig | ConvertFrom-Json
    Ensure-JsonProperty $cfg.BeTech57 'PcscReader' ([string]$reader)
    Ensure-JsonProperty $cfg.BisApi 'EnableHotelCardWrites' $false
    Ensure-JsonProperty $cfg.BisApi 'RequireWriteChallenge' 'GRAVAR'
    $cfg | ConvertTo-Json -Depth 10 | Set-Content -Path $installedConfig -Encoding UTF8
    Restart-Service -Name 'BisApi' -Force
    $health = Wait-Api
}

$vendorStatus = Invoke-RestMethod 'http://127.0.0.1:8765/api/vendor/status' -TimeoutSec 10
$finalReaders = Invoke-RestMethod 'http://127.0.0.0.1:8765/api/pcsc/readers' -TimeoutSec 10
$connections = @(Get-NetTCPConnection -LocalPort 8765 -State Listen -ErrorAction SilentlyContinue)
$invalidBindings = @($connections | Where-Object { $_.LocalAddress -notin @('127.0.0.1', '::1') })
if ($invalidBindings.Count -gt 0) {
    throw 'A porta 8765 esta exposta fora do loopback. Instalacao interrompida por seguranca.'
}
if ($vendorStatus.hotelCardWritesEnabled -ne $false) {
    throw 'EnableHotelCardWrites deveria estar FALSE apos a instalacao.'
}

$service = Get-Service -Name 'BisApi'
$hotelPasswordConfigured = [bool]$vendorStatus.hotelPasswordConfigured
$codecPresent = [bool]$vendorStatus.codecPresent
$shimPresent = [bool]$vendorStatus.pcscShimPresent
$readyForEnable = $codecPresent -and $shimPresent -and $hotelPasswordConfigured -and [bool]$reader

Write-Host ''
Write-Host '==============================================' -ForegroundColor DarkCyan
Write-Host ' BIS API - RESULTADO DA INSTALACAO ' -ForegroundColor Cyan
Write-Host '=============================================' -ForegroundColor DarkCyan
Write-Host ("Windows Smart Card ......... {0}" -f $smartCardService.Status)
Write-Host ("Servico BisApi .............. {0}" -f $service.Status)
Write-Host ("Dashboard ................... http://127.0.0.1:8765")
Write-Host ("API health .................. OK")
Write-Host ("Leitor ACR122 ............... {0}" -f $(if ($reader) { $reader } else { 'NAO DETECTADO' }))
Write-Host ("Codec btlock57L.dll ......... {0}" -f $(if ($codecPresent) { 'OK' } else { 'NAO INSTALADO' }))
Write-Host ("AcsReader.dll PC/SC ......... {0}" -f $(if ($shimPresent) { 'OK' } else { 'ERRO' }))
Write-Host ("HPASS ....................... {0}" -f $(if ($hotelPasswordConfigured) { 'CONFIGURADO' } else { 'NAO CONFIGURADO' }))
Write-Host 'Emissao de cartoes .......... DESABILITADA'
Write-Host ("Porta 8765 .................. LOOPBACK SOMENTE")
Write-Host ("Pronto para habilitar ....... {0}" -f $(if ($readyForEnable) { 'SIM' } else { 'NAO' }))
Write-Host 'Modo atual ................... DIAGNOSTICO'
Write-Host '=============================================' -ForegroundColor DarkCyan

if (-not $reader) {
    Write-Warning 'Conecte/regularize o ACR122U e execute novamente o instalador para configurar automaticamente o nome PC/SC.'
}
if (-not $codecPresent) {
    Write-Warning 'Instale/copie localmente btlock57L.dll e Data.dll da instalacao licenciada do BIS/PMS Saga.'
}
if (-not $hotelPasswordConfigured) {
    Write-Warning 'HPASS ainda nao esta configurado. Nenhum valor de senha foi exibido ou gravado em log.'
}

if (-not $NoOpenDashboard) {
    Start-Process 'http://127.0.0.1:8765'
}

exit 0
