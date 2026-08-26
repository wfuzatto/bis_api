param(
    [Parameter(Mandatory = $true)]
    [string]$BisFolder
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $projectRoot 'src\BisApi.Web\vendor\x86'
New-Item -ItemType Directory -Force -Path $destination | Out-Null

$files = @('acr120u.dll', 'AcsReader.dll', 'Win32dll.dll')
$copied = 0

foreach ($file in $files) {
    $source = Join-Path $BisFolder $file
    if (Test-Path $source) {
        Copy-Item $source -Destination (Join-Path $destination $file) -Force
        Write-Host "OK  $file"
        $copied++
    }
    else {
        Write-Warning "Não encontrado: $source"
    }
}

if ($copied -eq 0) {
    throw 'Nenhuma DLL foi encontrada. Confira o caminho da pasta do BIS.'
}

Write-Host "\nDLLs copiadas para: $destination"
Write-Host 'Agora execute: dotnet run --project .\src\BisApi.Web\BisApi.Web.csproj'
