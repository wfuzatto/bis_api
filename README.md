# bis_api

Camada de integração direta entre o totem e o gravador de cartões utilizado pelo BIS Hotel 5.7, sem abrir ou automatizar o executável do BIS.

> Estado atual: laboratório de hardware. A comunicação de baixo nível com o ACR120U está implementada; a codificação completa de cartão de hóspede ainda será adicionada depois de mapearmos o layout/segredos usados pelas fechaduras Saga/Be-Tech.

## Objetivo

```text
Totem / PMS
    |
    | HTTP localhost
    v
bis_api (Windows x86)
    |
    | P/Invoke
    v
acr120u.dll
    |
    v
ACR120 / RW-41 USB
    |
    v
MIFARE 1K
```

O runtime final não depende do `btlock57.exe` nem do banco do BIS.

## O que já existe

- API HTTP local em `127.0.0.1:8765`.
- Processo forçado para `x86`, compatível com as DLLs legadas do pacote BIS 5.7.
- Detecção da presença da `acr120u.dll`.
- Consulta da versão da DLL.
- Abrir/fechar o ACR120U (USB1 a USB8).
- Selecionar cartão e ler UID/tipo.
- Autenticar setor MIFARE 1K/4K.
- Ler bloco de 16 bytes.
- Dump de um setor para análise.
- Escrita crua de bloco protegida por configuração + palavra de confirmação.
- Dashboard de laboratório para testar tudo pelo navegador.
- Endpoint reservado para a futura codificação de cartão de quarto.

## Segurança do laboratório

A escrita crua vem **desabilitada por padrão**. Para habilitar, altere `BisApi:EnableRawWrites` em `appsettings.json` para `true`. Mesmo habilitada, a API exige a palavra configurada em `RequireWriteChallenge`.

Não grave blocos trailer (`3, 7, 11...`) sem conhecer exatamente as chaves e access bits; uma escrita incorreta pode tornar o setor inacessível.

## DLLs do fabricante

As DLLs proprietárias **não são versionadas neste repositório**. Copie a partir da instalação autorizada do BIS:

```powershell
.\scripts\install-vendor-dlls.ps1 -BisFolder "C:\caminho\BIS Hotel v5.7"
```

O script procura e copia, quando disponíveis:

- `acr120u.dll`
- `AcsReader.dll`
- `Win32dll.dll`

O projeto usa diretamente `acr120u.dll`; as demais ficam disponíveis para os próximos testes.

## Requisitos

- Windows 10/11 ou Windows Server.
- Driver do ACR120U/RW-41 instalado.
- .NET 8 SDK para desenvolvimento, ou publicação self-contained.
- Arquitetura x86.

## Executar em desenvolvimento

```powershell
dotnet restore .\src\BisApi.Web\BisApi.Web.csproj
dotnet run --project .\src\BisApi.Web\BisApi.Web.csproj
```

Abra:

```text
http://127.0.0.1:8765
```

## Publicar para o computador do totem

```powershell
dotnet publish .\src\BisApi.Web\BisApi.Web.csproj `
  -c Release `
  -r win-x86 `
  --self-contained true `
  -o .\publish\win-x86
```

Confirme que `acr120u.dll` e suas dependências estão junto do executável publicado.

## Próxima etapa: cartão de hotel

A API do ACR120U resolve a comunicação física, mas o cartão da fechadura contém um formato de aplicação específico do sistema Saga/Be-Tech: quarto, validade, permissões, sequência, possíveis checksums e chaves de setores.

A próxima etapa é mapear esse formato e implementar `IHotelCardCodec`/`SagaBis57CardCodec`. O plano de laboratório está em `docs/CARD_CODEC_PLAN.md`.

## Referência técnica

O ACR120U expõe as funções `ACR120_Open`, `ACR120_Select`, `ACR120_Login`, `ACR120_Read` e `ACR120_Write`. A assinatura utilizada neste projeto segue a documentação oficial da Advanced Card Systems para o ACR120U API v3.00.
