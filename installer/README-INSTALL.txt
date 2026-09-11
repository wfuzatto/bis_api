BIS API - INSTALADOR WINDOWS
===========================

1. Extraia TODO o ZIP baixado do GitHub Actions.
2. Conecte o ACS ACR122U ao Windows.
3. Execute BisApi-Install.bat.
4. Aceite a solicitacao de Administrador (UAC).
5. O instalador valida os hashes, limpa a pasta temporaria, inicia o servico Smart Card,
   instala/atualiza o BisApi e testa o dashboard/API.

O pacote NAO instala .NET, Visual Studio ou Docker. O BisApi e self-contained.

DLLs proprietarias Be-Tech/Saga nao sao publicadas no GitHub.
O instalador tenta preservar uma instalacao existente e localizar automaticamente:
  - btlock57L.dll
  - Data.dll

Se elas nao forem encontradas, o BisApi ainda e instalado em MODO DIAGNOSTICO.
Voce pode executar novamente usando:
  BisApi-Install.bat -VendorFolder "C:\caminho\para\pasta\PMS Saga"

SEGURANCA
---------
- EnableHotelCardWrites permanece FALSE.
- A porta 8765 fica somente em 127.0.0.1.
- O HPASS nunca e impresso pelo instalador.
- NAO instale o driver ACR120/RW-41 para este projeto.
- O ACR122 usa PC/SC / CCID / WinSCard.

Dashboard:
  http://127.0.0.1:8765

Servico Windows:
  BisApi

Pasta instalada:
  C:\Program Files\BisApi
