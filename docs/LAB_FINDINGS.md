# Resultados do laboratório

Atualizado em 2026-09-10.

## Leitor NFC

- O equipamento conectado é um **ACS ACR122** (`ACR122 Smart Card Reader`).
- O leitor foi detectado pelo Windows Smart Card Resource Manager como `ACS ACR122 0`.
- A comunicação PC/SC foi validada com o comando `FF CA 00 00 00` (Get UID), retornando `90 00`.
- A integração atual do projeto ainda usa a DLL legada `acr120u.dll`, voltada ao ACR120/RW-41. Para este equipamento, deve ser implementado um backend PC/SC/CCID.

## Cartão de laboratório

- O ATR observado foi:

  `3B 8F 80 01 80 4F 0C A0 00 00 03 06 03 00 01 00 00 00 00 6A`

- O ATR identifica uma **MIFARE Classic 1K**.
- A autenticação com a chave padrão `FF FF FF FF FF FF` falhou nos setores/blocos testados (`63 00`). Isso indica que a tag usa chaves diferentes da padrão ou não contém uma estrutura de dados compatível.
- Nenhuma operação de escrita foi realizada durante os testes.

## Banco `btlock57.mdb`

O banco Access foi aberto em modo somente leitura. Foram encontradas 47 tabelas, incluindo:

- `doors`: contém a porta `door_id = 001200`, com nome `1200`.
- `issuedcards`: contém histórico de cartões, portas, números, datas de início/fim e opções de acesso.
- `activecards`: contém cartões ativos em memória do sistema.
- `cardsector`: tabela de setores/blocos, atualmente sem registros na cópia analisada.
- `handset_data`: tabela de dados de aparelho/cartão, atualmente sem registros na cópia analisada.
- `sysinfo`: contém informações internas do sistema; valores de senha e credenciais não devem ser publicados.

Não foi localizado um registro em `issuedcards` para a porta `001200` exatamente no intervalo de 10/09/2026 a 11/09/2026.

## Próximos passos

1. Implementar comunicação PC/SC para o ACR122.
2. Obter um cartão original que abra a porta `1200` para comparação de setores.
3. Mapear as chaves, setores, campos, checksums e regras de validade do formato Saga/Be-Tech.
4. Só então habilitar uma gravação controlada em cartão de laboratório.

