param(
    [string]$Room = '302',
    [string]$DoorId = '000302',
    [string]$Reader = '',
    [int]$Attempts = 15,
    [int]$DelaySeconds = 1
)

$ErrorActionPreference = 'Stop'
$package = 'com.grupovaledamantiqueira.nfckey'
$logPath = Join-Path $PSScriptRoot ('android-hce-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
Start-Transcript -Path $logPath | Out-Null
try {
    if (-not (Get-Command adb -ErrorAction SilentlyContinue)) { throw 'adb não está no PATH.' }
    $devices = @(adb devices | Select-String '\tdevice$')
    if ($devices.Count -eq 0) { throw 'Nenhum aparelho Android autorizado/conectado.' }
    if (-not ($Room -match '^[0-9]{3,4}$')) { throw 'Room inválido.' }
    if (-not ($DoorId -match '^[0-9]{6}$')) { throw 'DoorId inválido.' }

    adb shell pm path $package | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Pacote $package não está instalado." }
    adb shell svc power stayon usb
    adb shell input keyevent KEYCODE_WAKEUP
    adb logcat -c
    adb shell am force-stop $package
    adb shell am start -n "$package/.MainActivity" --es room $Room --es door_id $DoorId --ez arm true
    Invoke-RestMethod 'http://127.0.0.1:8765/api/health' | ConvertTo-Json -Depth 8
    $readers = Invoke-RestMethod 'http://127.0.0.1:8765/api/pcsc/readers'
    $readers | ConvertTo-Json -Depth 8
    Write-Host "Encoste o telefone ao leitor agora. Tentando até $Attempts vezes..."
    for ($i = 1; $i -le $Attempts; $i++) {
        Write-Host "Tentativa $i/$Attempts"
        $uri = 'http://127.0.0.1:8765/api/pcsc/hce-probe'
        if ($Reader) { $uri += '?reader=' + [Uri]::EscapeDataString($Reader) }
        try {
            $result = Invoke-RestMethod $uri
            $result | ConvertTo-Json -Depth 12
            if ($result.hceRoundTripOk -eq $true) { Write-Host 'ACR -> Android HCE: OK'; break }
        } catch { Write-Warning $_.Exception.Message }
        if ($i -lt $Attempts) { Start-Sleep -Seconds $DelaySeconds }
    }
    Write-Host "Log salvo em $logPath"
} finally { Stop-Transcript | Out-Null }
