param(
    [string]$Configuration = "Release",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Native = Join-Path $Root "native\AcsReaderShim"
if (-not $OutputDir) { $OutputDir = Join-Path $Root "artifacts\native-win-x86" }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    throw "Visual Studio Build Tools não encontrado. Instale 'Desktop development with C++' / MSVC x86/x64."
}

$vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsPath) { throw "MSVC C++ x86/x64 não encontrado." }
$vcvars = Join-Path $vsPath "VC\Auxiliary\Build\vcvars32.bat"
if (-not (Test-Path $vcvars)) { throw "vcvars32.bat não encontrado em $vcvars" }

$cpp = Join-Path $Native "AcsReaderShim.cpp"
$def = Join-Path $Native "AcsReaderShim.def"
$out = Join-Path $OutputDir "AcsReader.dll"
$obj = Join-Path $OutputDir "AcsReaderShim.obj"
$opt = if ($Configuration -eq "Debug") { "/Od /Zi" } else { "/O2" }

$command = 'call "{0}" >nul && cl.exe /nologo /c /EHsc /MT {1} /DWIN32 /D_WINDOWS /Fo:"{2}" "{3}" && link.exe /nologo /DLL /MACHINE:X86 /DEF:"{4}" /OUT:"{5}" "{2}" winscard.lib' -f $vcvars, $opt, $obj, $cpp, $def, $out
cmd.exe /d /s /c $command
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $out)) { throw "Falha ao compilar AcsReader.dll PC/SC." }
Write-Host "Shim PC/SC gerado: $out" -ForegroundColor Green
