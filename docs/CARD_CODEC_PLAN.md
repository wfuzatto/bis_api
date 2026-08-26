# Plano do codec Saga / BIS Hotel 5.7

## Meta

Substituir a etapa de emissão de cartão do BIS por código próprio no `bis_api`, usando diretamente o gravador ACR120/RW-41.

O runtime final deverá funcionar sem `btlock57.exe` e sem automação de tela.

## O que já está resolvido

O pacote BIS 5.7 fornecido contém `acr120u.dll`, cuja export table inclui:

- `ACR120_Open`
- `ACR120_Close`
- `ACR120_Select`
- `ACR120_Login`
- `ACR120_Read`
- `ACR120_Write`
- `ACR120_RequestDLLVersion`

Isso permite controlar diretamente o hardware e cartões MIFARE Classic.

## O que ainda precisa ser identificado

Para um cartão de hóspede válido, ainda precisamos localizar:

1. setores/blocos utilizados pela fechadura;
2. Key A / Key B usadas em cada setor;
3. representação do número do quarto/fechadura;
4. codificação de data e hora de início/fim;
5. contador/número de emissão;
6. permissões de portas comuns;
7. código do hotel/sistema;
8. checksum/CRC/MAC, caso exista;
9. alterações de sector trailer e access bits, caso o sistema faça isso.

## Método de laboratório

Use cartões exclusivamente de teste e, de preferência, uma fechadura fora de produção.

### Fase A — confirmar hardware

1. Copiar as DLLs com `scripts/install-vendor-dlls.ps1`.
2. Iniciar `bis_api` em x86.
3. Abrir USB1.
4. Confirmar versão da DLL.
5. Colocar cartão e ler UID.
6. Tentar autenticação com `DefaultA`, `DefaultB` e `DefaultF` nos setores.

### Fase B — criar pares controlados

Para descobrir o formato, produzir cartões de referência variando **uma única informação por vez**. O BIS pode ser usado somente nesta fase de análise; não fará parte do sistema final.

Exemplos:

- cartão A: quarto 101, 10:00–11:00;
- cartão B: quarto 102, 10:00–11:00;
- cartão C: quarto 101, 10:00–12:00;
- cartão D: quarto 101, dia seguinte, mesmo horário;
- cartão E: segunda emissão idêntica à A.

Comparar os dumps permite separar campos de quarto, validade e contador.

### Fase C — análise estática do BIS

O executável `btlock57.exe` contém referências a rotinas como `CardPrepareBuffer`, `CardCanWrite`, `CardBeforeOperate` e `CardAfterWrite`. A análise deverá localizar as rotinas chamadas imediatamente antes de `ACR120_Write`/`acr_120Write` e reconstruir apenas o formato necessário para emissão de cartões autorizados.

Não é necessário reproduzir a interface gráfica do BIS.

### Fase D — implementar codec

A implementação ficará isolada da camada de hardware:

```text
HotelCardRequest
       |
       v
SagaBis57CardCodec
       |
       | gera setores/blocos
       v
Acr120Service
       |
       v
acr120u.dll -> gravador -> cartão
```

O codec não deve conter parâmetros do hotel hard-coded. Chaves, código da propriedade, portas comuns e demais dados deverão ficar em configuração local protegida.

### Fase E — validação

Antes de liberar para o totem:

1. gerar cartão pelo BIS e pelo `bis_api` com os mesmos dados;
2. comparar os blocos esperados;
3. validar os dois cartões em uma fechadura de teste;
4. testar cartões expirados;
5. testar emissão sucessiva para o mesmo quarto;
6. testar alteração de validade;
7. confirmar que um cartão de outro hotel/propriedade não é aceito;
8. bloquear qualquer operação de sector trailer não explicitamente necessária.

## Critério para considerar a integração pronta

A integração estará pronta quando `POST /api/hotel-card/encode` conseguir receber quarto + validade, gravar um cartão novo e esse cartão abrir apenas as fechaduras/período configurados, sem qualquer processo do BIS em execução.
