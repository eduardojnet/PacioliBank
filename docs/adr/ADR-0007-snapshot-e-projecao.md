# ADR-0007: Gerar snapshot de posição de forma síncrona amortizada, sem componente assíncrono no caminho de leitura

- **Status:** Aceito, revisado em 2026-10-05 (card 32: fechamento diário implementado)
- **Data:** 2026-10-02
- **Decisor:** Eduardo J. G. do Carmo
- **Requisitos dirigentes:** RF-003, RF-004, RF-010, RN-010, RNF-002, RNF-003, RNF-006, R-03

## Contexto e problema

O [ADR-0003](./ADR-0003-ledger-append-only.md) tornou a posição um dado derivado. Isso resolve auditoria e temporalidade, e cria um problema novo: calcular a posição exige somar lançamentos, e o custo dessa soma cresce com o histórico da conta.

Sem tratamento, o sistema reintroduz pelo caminho da leitura exatamente o defeito que o legado tem pelo caminho da escrita: degradação proporcional ao uso. Um cliente de dez anos teria consulta de saldo mais lenta que um cliente de dez dias. Isso viola RNF-003 e, pior, faz a experiência piorar com a fidelidade do cliente.

Existe ainda uma segunda consequência, menos evidente: a leitura da posição ocorre **dentro da transação de escrita**, para validar a invariante de saldo ([ADR-0005](./ADR-0005-controle-de-concorrencia.md), passo 2). O custo da leitura é, portanto, também a duração do bloqueio. Snapshot não é só desempenho de consulta: é desenho de concorrência.

## Critérios de decisão

1. Custo de leitura constante em relação ao histórico da conta (RNF-003)
2. Resultado idêntico ao cálculo integral pelo ledger, sempre (RN-010)
3. Ausência de snapshot não produz erro, apenas lentidão (RF-010)
4. Mínimo de componentes móveis: cada processo assíncrono é um ponto de falha e de atraso (R-03)
5. Não aumenta a duração do bloqueio de escrita

## Opções consideradas

1. Sem snapshot, agregação integral do ledger a cada consulta
2. Snapshot gerado por processo assíncrono em segundo plano
3. Snapshot gerado de forma síncrona e amortizada, a cada N lançamentos
4. Saldo materializado atualizado a cada lançamento
5. Cache externo de posição

## Decisão

**Snapshot síncrono amortizado por sequência**, gravado na mesma transação do lançamento quando a sequência atinge múltiplo de `N` (valor inicial: 100, configurável).

```sql
CREATE TABLE balance_snapshots (
    account_id       uuid          NOT NULL,
    up_to_sequence   bigint        NOT NULL,
    balance          numeric(19,4) NOT NULL,
    as_of            timestamptz   NOT NULL,
    created_at       timestamptz   NOT NULL DEFAULT now(),

    PRIMARY KEY (account_id, up_to_sequence)
);
```

Cálculo da posição corrente:

```
posição = snapshot.balance (maior up_to_sequence da conta)
        + Σ lançamentos com sequence > snapshot.up_to_sequence
```

Com `N = 100`, a soma percorre no máximo 99 lançamentos, independentemente de a conta ter cem ou cem milhões de registros. O custo de leitura torna-se constante, atendendo RNF-003 e RNF-006.

### Por que síncrono e não assíncrono

Esta é a inversão deliberada em relação ao desenho mais comum em CQRS.

Um projetor assíncrono introduz: um processo adicional a implantar e monitorar; um ponto de parada silenciosa; e atraso variável, que degrada a leitura progressivamente sem sinal claro. É o risco R-03, e ele se manifesta justamente no pico, quando a fila de projeção cresce mais rápido do que é consumida.

A geração síncrona amortizada troca isso por um custo conhecido e pequeno: **uma em cada cem escritas paga uma inserção adicional**. A inserção usa a posição já calculada pela transação em curso, de modo que não há leitura extra e o critério 5 é preservado. Não há processo a operar, não há fila a monitorar, não há atraso possível.

Em sistema financeiro, previsibilidade vale mais que vazão de pico. Trocar um componente assíncrono por 1% de latência adicional distribuída é uma troca favorável.

### Consulta histórica no escopo atual

Para posição em instante passado (RF-004), o cálculo usa o índice `ix_entries_account_occurred` ([ADR-0003](./ADR-0003-ledger-append-only.md)), que cobre a agregação sem acessar a tabela.

**Limitação assumida e declarada:** o snapshot é ancorado em sequência, que é ordem de registro, enquanto a consulta histórica usa `occurred_at`, que é ordem do fato. Com lançamentos retroativos permitidos (RN-012), o snapshot por sequência não é aplicável à consulta histórica sem verificação adicional. Por isso a consulta histórica agrega diretamente pelo índice.

Pelas premissas da [ENF](../specs/ENF-especificacao-nao-funcional.md) §3, uma conta acumula cerca de 400 lançamentos por ano, o que torna a agregação por faixa temporal barata. **Esta decisão depende dessa premissa e deve ser revista quando o volume real for conhecido** (QA-006).

### Evolução especificada, não implementada

Quando a consulta histórica deixar de atender RNF-002, a solução é o **saldo de fechamento diário** (`daily_balances`), que é o modelo contábil padrão: uma linha por conta por dia, com a posição de fechamento. Lançamento retroativo invalida os fechamentos a partir daquela data, marcando-os para recomputação.

Isso não é implementado agora porque adicionaria um processo de fechamento e uma lógica de invalidação para resolver um problema que, sob as premissas atuais, não existe. **Construir contra premissa não validada é a forma mais cara de errar.** O gatilho de adoção está declarado abaixo.

### Reconstrução

Um comando administrativo reconstrói todos os snapshots de uma conta a partir do ledger. Serve à verificação periódica de RNF-033 e é o procedimento de recuperação caso a tabela seja perdida.

## Consequências

**Positivas**

- Custo de leitura constante, atendendo RNF-003 e RNF-006
- Duração do bloqueio de escrita limitada e previsível, reforçando o [ADR-0005](./ADR-0005-controle-de-concorrencia.md)
- Nenhum processo assíncrono no caminho da posição corrente: R-03 é eliminado por desenho, não mitigado
- Snapshot é derivado puro: apagá-lo não altera nenhuma resposta, apenas o tempo
- Consistência entre snapshot e ledger é garantida pela transação, não por conciliação

**Negativas**

- Uma em cada `N` escritas tem latência adicional de uma inserção. Com `N = 100`, afeta 1% das escritas e é invisível no p99
- `N` é parâmetro de calibração: muito baixo infla a tabela de snapshots, muito alto aumenta o replay por consulta. Calibração inicial por premissa, ajuste por métrica `entriesReplayed`
- Crescimento adicional de armazenamento, da ordem de 1% das linhas do ledger
- **A consulta histórica não se beneficia do snapshot** e depende da premissa de volume por conta. É a fragilidade declarada desta decisão
- Conta com movimento muito baixo pode ficar longos períodos sem snapshot novo, mantendo replay pequeno de qualquer forma, o que é inofensivo

**Neutras**

- O campo `computedFrom` na resposta da consulta ([EF](../specs/EF-especificacao-funcional.md) §8.5) expõe se o snapshot foi usado, servindo como indicador antecedente de degradação
- Snapshots antigos podem ser expurgados mantendo apenas o mais recente por conta, se o armazenamento se tornar relevante

## Análise das opções rejeitadas

**Sem snapshot.** Rejeitado pelo critério 1: o custo cresceria com o histórico, violando RNF-003 e ampliando a duração do bloqueio de escrita proporcionalmente à idade da conta.

**Projetor assíncrono.** Rejeitado pelo critério 4. Acrescenta componente, atraso e modo de falha silencioso, para resolver um problema que a geração amortizada resolve sem nenhum deles. *Voltaria a ser a melhor escolha* se o cálculo da posição se tornasse caro a ponto de não caber na transação de escrita, o que não é o caso quando o replay é limitado a `N`.

**Saldo materializado atualizado a cada lançamento.** Rejeitado porque criaria uma segunda fonte da verdade passível de divergir do ledger, contrariando RN-010. É o modelo do legado com uma camada a mais.

**Cache externo.** Rejeitado como mecanismo primário por introduzir dependência cuja indisponibilidade precisa ser tratada (RNF-010) e cuja invalidação é fonte conhecida de inconsistência. *Permanece disponível como otimização posterior*, acima do snapshot, com invalidação por `sequence`, se e quando o volume de leitura justificar.

## Validação

- [BDD](../specs/BDD-comportamento.md) F10: posição com snapshot idêntica à calculada pelo ledger integral; remoção de todos os snapshots não altera nenhuma resposta; reconstrução produz resultado idêntico
- [BDD](../specs/BDD-comportamento.md) F08: posição correta com o processo de snapshot interrompido
- Teste de RNF-003: latência de consulta em conta com 100 e com 100.000 lançamentos, com variação inferior a 20%
- Métrica `entriesReplayed` por consulta, com alerta no p99 acima de `N`

> **Estado da validação em 2026-10-04 (card 30.1).** Feito: `SnapshotTests`, contra PostgreSQL real. A centésima escrita grava o snapshot com o saldo que ela mesma calculou; na âncora, a posição sai do snapshot sem somar nenhum lançamento; com mais 99, soma 99, o máximo; o valor confere sempre com a soma do ledger inteiro, calculada por fora (primeira parte do F10). Poder de detecção medido com três mutações, todas reprovadas: snapshot nunca gravado; snapshot com o valor do lançamento no lugar do saldo; leitura que soma de novo o lançamento da âncora. Não feito: remoção e reconstrução dos snapshots (resto do F10), F08 e o teste de latência da RNF-003.
>
> **Observado na mutação 2:** "descartável" vale para **apagar**, não para **errar**. Como a escrita parte do snapshot para validar o saldo (RN-001) e gravar `balance_after`, um snapshot com valor errado contaminaria todos os lançamentos seguintes e o extrato. As defesas que existem: o snapshot é gravado na mesma transação, a partir do saldo que ela calculou; o papel da aplicação não tem `UPDATE` em `balance_snapshots` (ADR-0009); e o teste acima compara o `balance_after` com a soma do ledger.

## Revisão de 2026-10-05 (card 32): fechamento diário implementado, de forma síncrona

### Por que agora

**Antecipação por decisão do usuário, não gatilho atendido.** O gatilho abaixo (p99 da consulta histórica acima do alvo da RNF-002) nunca foi medido: não há ambiente de carga (card 30). O registro é feito como antecipação, para não apresentar decisão de prazo como resposta a medição.

### Decisão

O texto acima previa o fechamento diário com **invalidação e recomputação assíncrona**. Implementado de outra forma: **fechamento mantido de forma síncrona, na transação do lançamento, sob o bloqueio da conta.**

- Tabela `daily_balances (account_id, day, closing_balance, last_sequence)`, migração `0002_saldo_diario.sql`, preenchida a partir do ledger existente. Derivada, como o snapshot ([ADR-0003](./ADR-0003-ledger-append-only.md)): pode ser reconstruída pelo mesmo `SELECT` do preenchimento
- **Escrita:** o lançamento do dia `d` cria ou soma o fechamento de `d` e soma o valor a todo fechamento de dia posterior que já exista. Lançamento retroativo, portanto, corrige os dias seguintes **na mesma transação**: nenhuma consulta vê fechamento desatualizado
- **Leitura:** posição no instante `T` = fechamento do último dia anterior ao dia de `T` + lançamentos do próprio dia até `T`, num único comando. `entriesReplayed` passa a ser só os lançamentos daquele dia; `computedFrom` ganha `dailyBalance` (EF §8.5). Sem fechamento anterior, a origem continua `ledger`
- **Dia em UTC, pela data do fato.** É partição interna: não aparece no contrato, e a posição em qualquer instante não depende dela. Fechamento contábil em horário de Brasília, se algum dia for exposto, é outra decisão
- **Privilégio:** a aplicação recebe `SELECT, INSERT, UPDATE` em `daily_balances`, nunca `DELETE`. É a primeira tabela derivada com `UPDATE`; o ledger continua só `SELECT, INSERT` ([ADR-0009](./ADR-0009-seguranca-e-privilegio-minimo.md))

### Alternativas rejeitadas

**Invalidação e recomputação assíncrona, como o texto original previa.** Exige processo de fechamento, marcação de dias inválidos e, na leitura, um caminho alternativo enquanto o dia está inválido. Rejeitada: acrescenta um componente e um estado intermediário para resolver o que a atualização síncrona resolve dentro da transação que já existe. *Voltaria a ser considerada* se o custo de corrigir os dias seguintes aparecesse no p99 de escrita (gatilho 5 abaixo).

**Movimento líquido por dia, em vez de fechamento.** Cada lançamento atualizaria uma única linha, sem corrigir dias seguintes; a leitura somaria os movimentos de todos os dias anteriores. Rejeitada: a leitura voltaria a crescer com o histórico, na proporção dos dias ativos. Com a premissa da ENF §3, cerca de 400 lançamentos por ano, dias ativos e lançamentos são da mesma ordem, e o ganho seria quase nenhum.

**Snapshot ancorado em data do fato.** Rejeitada pelo mesmo motivo que impede usar o snapshot atual na consulta histórica: o snapshot serve à posição corrente, ancorada em sequência, e a escrita parte dele para validar o saldo. Misturar as duas ordens no mesmo artefato tornaria os dois casos mais difíceis de provar.

### Custo conhecido

Lançamento retroativo atualiza uma linha por dia ativo posterior. Lançamento no dia corrente, o caso comum, atualiza uma linha. Com a premissa da ENF §3, um retroativo de um ano atualiza no máximo algumas centenas de linhas pequenas, sob o bloqueio da conta que a escrita já detém. [NVI] Não medido sob carga.

### Validação feita

- `DailyBalanceTests` (4), PostgreSQL real, conferidos contra a soma do ledger calculada por fora: posição histórica e `computedAtSequence` em 63 instantes, com retroativos gravados depois dos posteriores, débitos, estorno, lançamento à meia-noite exata e no último microssegundo do dia; todo fechamento igual ao ledger; consulta que parte do fechamento soma só o dia; papel da aplicação não apaga fechamento
- `MigrationTests`: banco com o esquema 0001 e lançamentos gravados recebe a 0002 e fica com os fechamentos corretos
- Poder de detecção, cinco mutações, todas reprovadas: sem corrigir os dias seguintes; dia novo sem o fechamento anterior; leitura usando o fechamento do próprio dia; limite do dia exclusivo (perde a meia-noite exata); preenchimento sem soma acumulada
- No ambiente local, a 0002 foi aplicada sobre o volume já em uso, sem recriar o banco (card 27): 13 fechamentos em 5 contas, nenhuma divergência; depois de um crédito retroativo pela API, posições históricas iguais à soma no banco em seis instantes, e ainda nenhuma divergência

## Gatilho de revisão

1. ~~p99 da consulta histórica ultrapassando o alvo de RNF-002, que dispara a implementação de `daily_balances`~~ Implementado por antecipação no card 32, ver a revisão acima
2. Volume real de lançamentos por conta superando em 100% a premissa da [ENF](../specs/ENF-especificacao-nao-funcional.md) §3 (QA-006)
3. `entriesReplayed` no p99 consistentemente acima de `N`, indicando calibração inadequada
4. Latência da inserção de snapshot aparecendo no p99 de escrita, que levaria a reconsiderar a geração assíncrona
5. Atualização dos fechamentos dos dias seguintes a um retroativo aparecendo no p99 de escrita, que reabriria a recomputação assíncrona
