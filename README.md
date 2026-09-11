# bis_api

Serviço Windows standalone para integrar PMS/totem com cartões do BIS Hotel 5.7 usando o codec original Be-Tech/Saga e um **ACS ACR122U via PC/SC**.

## Estado atual

- API HTTP local em `127.0.0.1:8765`.
- Dashboard standalone servido pelo próprio processo.
- Diagnóstico ACR122/PC-SC: lista leitores, ATR e UID.
- Compatibilidade `AcsReader.dll` -> PC/SC para o ACR122U.
- Integração com `btlock57L.dll` para `Write_Guest_Card` e `SerialNo_FromNow`.
- Backend ACR120/RW-41 legado preservado.
- Publicação `win-x86` self-contained.
- Instalação como serviço Windows `BisApi`.
- Emissão de cartão e escrita crua desabilitadas por padrão.

## Arquitetura principal

```text
PMS / dashboard
      |
      v
BisApi.exe (x86)
      |
      v
btlock57L.dll       codec Be-Tech/Saga original
      |
      v
AcsReader.dll       shim PC/SC criado neste repositório
      |
      v
WinSCard -> ACS ACR122U -> MIFARE Classic
```

O projeto **não recria o algoritmo proprietário do cartão**. Ele conserva `btlock57L.dll` como codec e substitui apenas a camada de comunicação que antes terminava no ACR120/RW-41.

## Instalação standalone

Veja o guia completo em [`docs/STANDALONE_ACR122.md`](docs/STANDALONE_ACR122.md).

Resumo:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\build-standalone.ps1 -PmsSagaFolder "C:\caminho\PMS Saga V.1.0.8.7_19 HEX"
cd .\publish\standalone-win-x86
.\BisApi.exe
```

Depois abra:

```text
http://127.0.0.1:8765
```

Após validar o ACR122, execute `install-service.ps1` como Administrador.

## Configuração local

Copie/edite `appsettings.Local.json`. Esse arquivo é ignorado pelo Git porque pode conter a senha HPASS e chaves de compatibilidade.

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

A emissão só funciona quando `EnableHotelCardWrites=true` e a confirmação recebida pela API coincide com `RequireWriteChallenge`.

## DLLs do fabricante

DLLs proprietárias não são versionadas. Para o modo ACR122, use:

```powershell
.\scripts\install-vendor-codec.ps1 -PmsSagaFolder "C:\caminho\PMS Saga V.1.0.8.7_19 HEX"
```

Esse script copia `btlock57L.dll`, mas **não copia o `AcsReader.dll` original**. O nome `AcsReader.dll` na aplicação standalone pertence ao shim PC/SC compilado pelo projeto.

Para o backend ACR120 legado existe `scripts/install-vendor-dlls.ps1`.

## Endpoints principais

- `GET /api/health`
- `GET /api/pcsc/readers`
- `GET /api/pcsc/probe?reader=...`
- `GET /api/vendor/status`
- `GET /api/vendor/serial`
- `POST /api/hotel-card/encode`

Os endpoints ACR120 de laboratório anteriores foram preservados para rollback e comparação.

## Segurança

- bind padrão somente em loopback (`127.0.0.1`);
- segredos fora do repositório;
- escrita de cartão desabilitada por padrão;
- raw writes e sector trailers bloqueados por padrão;
- primeiro teste deve ser feito em cartão/fechadura autorizados de laboratório.
