@echo off
setlocal
set "SCRIPT=%~dp0BisApi-Install.ps1"

if not exist "%SCRIPT%" (
  echo [ERRO] BisApi-Install.ps1 nao encontrado ao lado deste BAT.
  pause
  exit /b 1
)

echo =============================================
echo  BIS API - Instalador Windows
echo =============================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" (
  echo.
  echo [ERRO] A instalacao terminou com codigo %RC%.
  pause
  exit /b %RC%
)

echo.
echo Instalacao concluida.
pause
exit /b 0
