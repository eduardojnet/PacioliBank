# ADR-0013: Particionar o ledger por mês de registro, com as chaves de unicidade numa tabela não particionada

- **Status:** Aceito e implementado (card 37)
- **Data:** 2026-10-05
- **Decisor:** Eduardo J. G. do Carmo
- **Requisitos dirigentes:** risco R-04 da [ENF](../specs/ENF-especificacao-nao-funcional.md), RN-004, RN-005, RN-006, regra 6 do projeto (invariante financeira garantida pelo banco), QA-004

## Contexto e problema

O ledger só cresce: nada é alterado nem apagado ([ADR-0003](./ADR-0003-ledger-append-only.md)). Com as premissas da ENF §3, a ordem de grandeza é de bilhões de linhas por ano (risco R-04). Particionar por tempo deixa cada período numa tabela física própria: índices menores por partição, manutenção por período e, quando houver política de retenção, arquivamento de um período inteiro sem tocar nos outros.

**Antecipação por decisão do usuário:** a política de retenção (QA-004) continua sem resposta. Particiona-se agora; nenhum período é arquivado ou apagado.

**O obstáculo, verificado no PostgreSQL 17:** restrição única e chave primária de tabela particionada só são aceitas com a coluna da partição (*"unique constraint on partitioned table must include all partitioning columns"*). Com ela, valem só dentro de cada partição. As quatro garantias estruturais do ledger passariam a ser por período:

| Restrição | Regra |
|---|---|
| chave primária `entry_id` | identidade do lançamento |
| `uq_entries_sequence (account_id, sequence)` | RN-006, sequência sem duplicata |
| `uq_entries_idempotency (account_id, idempotency_key)` | RN-005, idempotência |
| `uq_entries_reversal (reversal_of)` | RN-004, no máximo um estorno |

A mesma chave de idempotência num mês e no seguinte deixaria de ser barrada pelo banco.

## Critérios de decisão

1. As quatro garantias continuam no banco, para todo o histórico (regra 6)
2. O código que reconhece conflito pelo nome da restrição não muda
3. Os dados existentes migram sem perda, sem recriar o banco ([ADR-0002](./ADR-0002-plataforma-e-armazenamento.md), revisão do card 27)
4. A aplicação continua só com `SELECT` e `INSERT` no ledger ([ADR-0009](./ADR-0009-seguranca-e-privilegio-minimo.md))

## Decisão

**O ledger é particionado por mês de `recorded_at`. As chaves vivem em `entry_keys`, uma tabela não particionada, gravada na mesma transação do lançamento.**

- **`entry_keys (entry_id, account_id, sequence, idempotency_key, reversal_of, recorded_at)`** carrega as quatro restrições, **com os mesmos nomes de antes**. O código que traduz `uq_entries_sequence` em nova tentativa, `uq_entries_reversal` em estorno duplicado e `uq_entries_idempotency` em repetição não mudou
- **Cada linha do ledger é amarrada à sua chave pelo banco:** chave estrangeira composta `fk_entries_keys (entry_id, account_id, sequence, idempotency_key)`. Sem isso, uma linha com sequência diferente da registrada escaparia da unicidade. O estorno é amarrado por `fk_entries_reversal (entry_id, reversal_of)`, verificada quando `reversal_of` não é nulo; o caso inverso, chave com estorno e linha sem, só impediria um estorno legítimo, nunca permitiria um segundo
- **Outbox e idempotência apontam para `entry_keys`**, que tem a unicidade global; `fk_outbox_entry` mantém o nome ([ADR-0008](./ADR-0008-outbox-transacional.md))
- **Partição por `recorded_at`, a data do registro, e não `occurred_at`, a data do fato.** O registro cresce em ordem: todo lançamento novo, retroativo inclusive, cai no mês corrente, e um período antigo nunca recebe escrita. A consulta histórica, por data do fato, não depende disso: parte do fechamento diário ([ADR-0007](./ADR-0007-snapshot-e-projecao.md), revisão do card 32)
- **Partições mensais**, nomeadas `ledger_entries_AAAA_MM`, mais a partição padrão `ledger_entries_default`, que recebe o que cair fora delas em vez de recusar o lançamento
- **Partições futuras abertas pelo migrador a cada execução**, até 12 meses à frente, pela função `ledger.ensure_month_partition`, que a aplicação não pode executar. Implantação regular mantém as partições à frente do tempo
- **Índices:** `ix_entries_account_occurred` (consulta histórica) e `ix_entries_account_sequence` (extrato e posição corrente), em cada partição. A verificação de estorno existente lê `entry_keys`, onde está o índice único
- **Privilégios:** aplicação com `SELECT, INSERT` no ledger e em `entry_keys`; nada de `UPDATE` nem `DELETE` em nenhuma das duas

## Consequências

**Positivas**

- As quatro garantias continuam estruturais e globais: duplicidade em outro período é recusada pelo banco, o que os testes verificam gravando de propósito em outro mês
- Escrita sempre no mês corrente: partições antigas ficam paradas e prontas para arquivamento, quando houver retenção
- Quando a retenção for definida, arquivar um período do ledger mantém as chaves: uma chave de idempotência antiga continua impedindo reuso

**Negativas**

- Uma inserção a mais por lançamento, e uma segunda tabela que também cresce sem limite. As linhas são pequenas (identificadores, sequência, chave), sem valor, metadados nem saldo. [NVI] Custo não medido sob carga
- Busca por sequência ou por identificador percorre o índice de cada partição: o custo cresce com o número de partições, não com o volume de cada uma. Com partições mensais, dezenas por ano
- Se o migrador ficar mais de 12 meses sem rodar, os lançamentos caem na partição padrão. Abrir depois a partição de um mês que já tenha linhas na padrão falha, e o migrador sai com erro: a falha é visível, e a correção é mover as linhas antes. [NVI] Procedimento não automatizado
- Duas tabelas a manter coerentes, o que só é aceitável porque o banco faz a coerência (chave estrangeira composta), e não o código

**Neutras**

- O contrato da API não muda; o extrato, a posição e os eventos são os mesmos

## Análise das opções rejeitadas

**Particionar e aceitar as restrições por período (opção apresentada como b1).** Rejeitada pelo critério 1: a unicidade entre períodos passaria a depender do bloqueio por conta e do código, o que contraria a regra 6. *Voltaria a ser considerada* se o custo da inserção em `entry_keys` aparecesse no p99 de escrita e nenhuma alternativa estrutural existisse.

**Particionar por conta (hash), mantendo a conta em todas as chaves.** As restrições de sequência e idempotência já têm a conta, e as outras duas poderiam ganhá-la. Rejeitada para este card: não trata o R-04, que é crescimento no tempo, e não permite arquivar período. *Voltaria a ser considerada* se o problema passasse a ser concorrência ou distribuição entre instâncias, e não volume histórico.

**Partição pela data do fato (`occurred_at`).** Rejeitada: lançamento retroativo escreveria em partições antigas, e período nenhum ficaria parado para arquivar. Além disso, a chave primária com `recorded_at` não é aceita com a partição por `occurred_at`; a estrutura teria de mudar sem ganho.

**Não particionar e arquivar por cópia para outra tabela.** Exige mover linhas de uma tabela que a aplicação não pode alterar, e a retenção não está definida. *Voltaria a ser considerada* com a QA-004 respondida, como complemento ao particionamento, não como substituto.

**Extensão `pg_partman` para manter as partições.** Rejeitada: não vem na imagem oficial do PostgreSQL, e a manutenção necessária é uma função de dez linhas chamada pelo migrador que já existe.

## Validação

- `PartitioningTests` (8), PostgreSQL real: ledger particionado por `RANGE (recorded_at)` com partição padrão; lançamento cai na partição do seu mês; mesma sequência, mesma chave de idempotência e segundo estorno, gravados em **outro período**, recusados pelo banco com o nome da restrição; linha do ledger sem chave correspondente recusada por `fk_entries_keys`; aplicação não altera nem apaga chave; migrador abre os meses seguintes sem duplicar
- `MigrationTests`: banco com 0001 e 0002 e dados de três meses, com estorno, mensagem na outbox e registro de idempotência, recebe a 0003; cada linha na partição do seu mês, soma e chaves preservadas, outbox e idempotência apontando para `entry_keys`, tabela antiga removida
- Suíte inteira verde sobre a estrutura nova (192 testes), inclusive os de concorrência, que disputam sequência e idempotência
- Poder de detecção: unicidade da idempotência e do estorno só por período, cada uma reprovada pelo seu teste; amarração da linha só pelo identificador reprovada; função de partição sem verificar mês existente reprovada. Unicidade da sequência só por período e partição por data do fato são recusadas pela própria estrutura: a migração falha
- No ambiente local, a 0003 foi aplicada sobre o volume em uso: 260 lançamentos e a mesma soma antes e depois, 260 chaves, coleção do Insomnia verde, fechamentos diários sem divergência; reexecução do migrador sem script novo e sem partição nova

## Gatilho de revisão

1. QA-004 respondida: definir e automatizar o arquivamento de partições antigas
2. Custo da inserção em `entry_keys` aparecendo no p99 de escrita
3. Número de partições alto o bastante para a busca por sequência pesar (partições trimestrais ou anuais)
4. Lançamentos aparecendo na partição padrão em produção
