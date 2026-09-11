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
  - ConfPmsSaga.ini

HPASS / SENHA DO HOTEL
----------------------
- O HPASS NAO fica gravado no codigo-fonte nem no ZIP publico do GitHub.
- Se o BisApi ja estiver configurado neste Windows, o instalador PRESERVA o HPASS local
  e nunca pede novamente.
- Em uma instalacao nova, o instalador tenta localizar ConfPmsSaga.ini e importar
  automaticamente a chave HPASS da secao [DADOS].
- O valor nunca e impresso no terminal nem gravado em logs.
- O valor fica somente em:
    C:\Program Files\BisApi\appsettings.Local.json
  Esse arquivo e configuracao LOCAL e nao deve ser enviado ao GitHub.

OUTRO HOTEL:
O HPASS pertence ao sistema/instalacao daquele hotel. Para usar o BisApi em outro hotel,
NAO reutilize o HPASS de outra unidade. Use uma destas opcoes:
  1. deixe o ConfPmsSaga.ini daquele hotel acessivel ao instalador; ou
  2. altere localmente BeTech57.HotelPassword em:
       C:\Program Files\BisApi\appsettings.Local.json
  3. opcionalmente execute:
       BisApi-Install.bat -PmsConfigFile "C:\caminho\ConfPmsSaga.ini"

Se btlock57L.dll/Data.dll nao forem encontrados, o BisApi ainda e instalado em
MODO DIAGNOSTICO. Para indicar a pasta manualmente:
  BisApi-Install.bat -VendorFolder "C:\caminho\para\pasta\PMS Saga"

SEGURANCA
---------
- EnableHotelCardWrites permanece FALSE apos instalacao/atualizacao.
- A porta 8765 fica somente em 127.0.0.1.
- O HPASS nunca e impresso pelo instalador.
- HPASS e configuracao local; nao versionar nem publicar.
- NAO instale o driver ACR120/RW-41 para este projeto.
- O ACR122 usa PC/SC / CCID / WinSCard.

Dashboard:
  http://127.0.0.1:8765

Servico Windows:
  BisApi

Pasta instalada:
  C:\Program Files\BisApi
