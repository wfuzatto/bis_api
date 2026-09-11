# BIS API standalone com ACR122U

## Arquitetura

```text
Browser local / PMS
        |
        | HTTP 127.0.0.1:8765
        v
     BisApi.exe (x86)
        |
        +------ WinSCard ----------> ACS ACR122U (diagnóstico)
        |
        v
   btlock57L.dll                 codec original Be-Tech/Saga
        |
        v
   AcsReader.dll                shim do bis_api, não a DLL original
        |
        v
     WinSCard / PC-SC ----------> ACS ACR122U
        |
        v
   MIFARE Classic 1K
```

A camada proprietária de formação do cartão permanece em `btlock57L.dll`. O projeto implementa apenas a compatibilidade do transporte ACR120 com PC/SC.

## Pré-requisitos no Windows

- Windows 10/11 ou Windows Server.
- ACS ACR122U instalado e visível em **Gerenciador de Dispositivos > Leitores de cartão inteligente**.
- Serviço Windows **Smart Card (SCardSvr)** disponível.
- Para compilar: .NET 8 SDK e Visual Studio Build Tools 2022 com **Desktop development with C++ / MSVC x86**.
- Para executar o pacote self-contained depois de compilado, o .NET SDK não é necessário.

## 1. Gerar o pacote standalone

Abra PowerShell na raiz do repositório:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\build-standalone.ps1 `
  -PmsSagaFolder "C:\caminho\PMS Saga V.1.0.8.7_19 HEX"
```

O diretório final será:

```text
publish\standalone-win-x86\
```

O script compila o `AcsReader.dll` PC/SC, publica o `BisApi.exe` x86 self-contained e copia apenas o `btlock57L.dll` autorizado da instalação do PMS Saga.

## 2. Configurar sem colocar segredos no Git

Edite:

```text
publish\standalone-win-x86\appsettings.Local.json
```

Exemplo:

```json
{
  "BisApi": {
    "EnableHotelCardWrites": false,
    "RequireWriteChallenge": "GRAVAR"
  },
  "BeTech57": {
    "PcscReader": "ACS ACR122 0",
    "HotelPassword": "000000"
  }
}
```

Troque `000000` pelos seis dígitos **HPASS** da instalação local. Não publique esse arquivo.

Comece com `EnableHotelCardWrites=false`.

## 3. Testar antes de instalar o serviço

No PowerShell:

```powershell
cd .\publish\standalone-win-x86
.\BisApi.exe
```

Abra no mesmo computador:

```text
http://127.0.0.1:8765
```

No dashboard:

1. clique em **Listar leitores**;
2. selecione `ACS ACR122 0`;
3. coloque um cartão de laboratório;
4. clique em **Ler ATR + UID**;
5. confirme que o UID e o ATR aparecem;
6. confira **Codec Be-Tech 5.7L**.

Somente depois disso altere `EnableHotelCardWrites` para `true`, reinicie o processo e teste uma emissão em cartão autorizado/de laboratório.

## 4. Instalar como serviço do Windows

Abra PowerShell **como Administrador** dentro da pasta publicada:

```powershell
.\install-service.ps1
```

O serviço será instalado como:

```text
BisApi
```

em:

```text
C:\Program Files\BisApi
```

O dashboard continuará disponível somente localmente em:

```text
http://127.0.0.1:8765
```

Comandos úteis:

```powershell
Get-Service BisApi
Restart-Service BisApi
Stop-Service BisApi
Start-Service BisApi
```

Para remover o serviço, preservando arquivos/configuração:

```powershell
.\uninstall-service.ps1
```

Para remover também os arquivos:

```powershell
.\uninstall-service.ps1 -RemoveFiles
```

## Segurança operacional

- O serviço escuta em `127.0.0.1` por padrão, não na LAN.
- A senha do hotel fica somente em `appsettings.Local.json`.
- Emissão de cartão vem desabilitada por padrão.
- A escrita crua MIFARE e escrita em sector trailers continuam bloqueadas por padrão.
- Valide primeiro em cartão e fechadura de laboratório.
- O `AcsReader.dll` original da Be-Tech **não deve substituir** o shim PC/SC na pasta publicada.

## Campos usados na emissão

O endpoint `POST /api/hotel-card/encode` usa:

- `RoomOrDoorId`: até 6 caracteres; números são preenchidos à esquerda (`1200` -> `001200`);
- `ValidFrom` / `ValidUntil`: convertidos para `yyyyMMddHHmmss`;
- `SuitDoor`: 12 caracteres hex, padrão `000000000000`;
- `PublicDoor`: 8 caracteres hex, padrão `00000000`;
- `GuestSerial`: automático via `SerialNo_FromNow` quando omitido;
- `HolderSerial`: padrão `0` no laboratório;
- `GuestIndex`: padrão `2`.

Esses defaults reproduzem o fluxo de teste observado no `PMSSaga.exe`; os valores avançados podem ser informados pelo dashboard quando necessário.
