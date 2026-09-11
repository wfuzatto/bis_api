# Codec Be-Tech 5.7L e ACS PC/SC

## Contrato verificado no binário local

Análise de interoperabilidade do `btlock57L.dll` x86, SHA-256
`5a275944c73148ce89bf2e887e6a19029630960221b74f5e17247fdf00b1f433`.
Os endereços abaixo usam a base preferida `0x400000`; o Windows pode relocá-la.
Nenhum binário proprietário deve ser adicionado ao Git.

* `Read_Snr`, VA `0x4BC908`: stdcall, três slots de quatro bytes, `ret 0x0C`
  em `0x4BCAB0`. Primeiro argumento: porta lógica; segundo: seletor do codec;
  terceiro: ponteiro de saída. O seletor é lido como byte em `[ebp+0x0C]`.
* O salto do seletor 3 alcança `0x4BC9A7`, atribuindo o modelo interno 11;
  4 alcança `0x4BC9CC`, modelo 12; 5 alcança `0x4BC9DD`, modelo 13.
  O backend ACS tem o literal `AcsReader.dll` em `0x4B6248` e resolve os
  exports `acr_120*` por nome.
* `Write_Guest_Card`, VA `0x4BA7E8`: stdcall, 12 argumentos, `ret 0x30`
  em `0x4BAB53`. O setor é lido de `[ebp+0x10]` e convertido para
  `sector * 4 + 1`; o seletor vem de `[ebp+0x0C]`; a porta é o primeiro
  argumento. Manter **port, readerModel, sectorNo** no P/Invoke e na chamada.
* `Read_Snr` copia exatamente oito caracteres ASCII, não oito bytes de UID
  binário. Para ACS, a rotina em `0x4B60D0` formata os bytes do UID em ordem
  inversa. Exemplo: UID PC/SC `01020304` produz serial `04030201`.
  O endpoint retorna `serial` do codec e `uidHex` do probe PC/SC separadamente.
* A rotina ACS de abertura em `0x4B617C` decrementa a porta lógica antes de
  chamar `acr_120Open`. Portanto, API `Port=1` e trace `port=0` são coerentes.
  No ramo ACS de `Read_Snr`, a porta do objeto é usada; não foi demonstrado
  que valores arbitrários do primeiro argumento alteram esse ramo.

Não foi localizado `PMSSaga.exe` para validar um call-site da aplicação.
Não se deve inferir o seletor da DLL diretamente de um índice `LEITOR` do PMS.

## Contrato do AcsReader original

Uma cópia isolada do fabricante foi analisada sem instalá-la no serviço.

| Export | Retorno de stack | Argumentos observados |
| --- | --- | --- |
| `acr_120Open` | `ret 0x10` | porta byte, comprimento de versão (saída), versão (saída), status (saída) |
| `acr_120Select` | `ret 0x0C` | tipo (saída byte), comprimento (saída byte), UID (saída) |
| `acr_120Read` | `ret 0x18` | setor, bloco absoluto, tipo de chave, ponteiro de chave, limite superior do array Delphi, saída de 16 bytes |
| `acr_120Write` | `ret 0x1C` | setor, bloco absoluto, tipo de chave, ponteiro de chave, limite superior do array, dados, limite superior dos dados |
| `acr_120Close` | `ret` | nenhum argumento; chamador ignora retorno original |
| `acr_120Beep` | `ret 0x04` | byte de controle |

Open/Select/Read/Write retornam resultado de 16 bits com sinal; zero representa
sucesso. Os wrappers originais encaminham versão/status/seleção ao ACR120.
O codec reserva 128 bytes para versão e 128 para status em `0x4B5C90`, além
de um byte para comprimento; reserva 256 bytes para serial em `0x4B6044`.
Esses são limites comprovados **neste chamador**, não capacidades universais
da API do fabricante. O shim atual cabe nesses buffers. O teste
`native/AcsReaderShim/ShimReadOnlyTest.cpp` verifica guards e executa apenas
Open/Select/Close em x86.

## Diagnóstico

Com `EnableHotelCardWrites=false`, configure localmente o nome PC/SC retornado
pelo Windows e consulte `GET /api/vendor/read-snr`. O endpoint executa
`Read_Snr(1,4,buffer)` com os defaults atuais, sob o mesmo lock da emissão.
Em erro nativo, `success=false`, `vendorResult` preserva o retorno e `serial=null`.
Falhas de P/Invoke/PCSC usam o tratamento HTTP existente.

Para habilitar trace, defina `BeTech57:ShimTrace=true` na configuração local.
Antes da chamada nativa, o serviço define no processo:

* `BIS_API_PCSC_READER`: leitor realmente detectado;
* `BIS_API_SHIM_TRACE=1`: somente quando solicitado na configuração.

O shim grava `C:\ProgramData\BisApi\logs\shim-trace.log` com timestamp e PID,
entrada/retorno dos exports, caminho efetivo do módulo, leitor, códigos
WinSCard decimais/hexadecimais, protocolo, UID, setor/bloco/tipo de chave e
status APDU. Nunca grava HPASS, valores de chaves ou dados de blocos.
O trace é desabilitado por padrão. `ShimTrace=false` o desliga na próxima
chamada de codec; reiniciar elimina o estado de ambiente anterior.

O log de `acr_120Open` usa o endereço da própria função para consultar o módulo,
comprovando qual DLL executou. Isso continua sendo evidência válida quando o
codec descarrega a DLL antes de uma enumeração externa de módulos.

## Compilação e validação

O script `scripts/build-native-shim.ps1` usa MSVC por padrão. Alternativamente,
aceita `-LlvmMingwBin <pasta-bin>` para LLVM-MinGW portátil, gerando x86 com
runtime C++ estático. Valide exports não decorados e ausência de dependência
`acr120u.dll`. A publicação gerenciada deve usar `win-x86 --self-contained true`.

Validação local em Windows 11: Open/Select e guards aprovados; `Read_Snr`
retornou zero com trace comprovando o módulo instalado e UID consistente.
Uma única emissão autorizada para `001200`, válida por 24 horas, retornou
HTTP 200, `written=true`, `vendorResult=0`. O trace confirmou atualizações
dos blocos 1 e 2 com SW `9000`. A escrita foi desabilitada em `finally` e
o serviço reiniciado. Nenhum sector trailer foi escrito nesse teste.
O funcionamento na fechadura física ainda requer validação no local.
