# ADR-0003: Adotar ledger append-only como única fonte da verdade, com a posição como dado derivado

- **Status:** Aceito
- **Data:** 2026-10-02
- **Decisor:** Eduardo J. G. do Carmo
- **Requisitos dirigentes:** RF-004, RN-003, RN-010, RNF-003, RNF-016, RNF-022, CQ-06

## Contexto e problema

O enunciado pede que o sistema "permita consultar a posição consolidada (saldo) de um cliente **em um determinado momento**" e trate dados financeiros onde "qualquer inconsistência gera impacto direto ao cliente".

Essas duas frases decidem o modelo de dados antes de qualquer outra consideração.

O sistema legado quase certamente mantém o saldo como coluna mutável, atualizada por `UPDATE`. Esse desenho tem três defeitos estruturais que explicam todos os sintomas relatados:

1. **Contenção.** Toda movimentação disputa a mesma linha, serializando o sistema por conta no caminho mais caro possível (bloqueio de escrita com geração de nova versão de linha).
2. **Ausência de temporalidade.** Não há como responder "qual era o saldo em 20 de janeiro" sem um histórico de alterações, que é um ledger construído de forma pior.
3. **Inauditabilidade.** Saldo errado não tem origem rastreável. Em incidente, não se reconstrói o que aconteceu.

Esta é, também, a decisão que a contabilidade tomou há mais de cinco séculos. O livro-razão de partidas dobradas é append-only por desenho: erro se corrige por lançamento de estorno, nunca por rasura. A solução correta aqui não é uma inovação técnica, é a aplicação de um modelo contábil consolidado.

## Critérios de decisão

1. Responde à posição em qualquer instante passado, com exatidão (RF-004)
2. Mantém trilha de auditoria completa e inalterável (RN-003, RNF-022)
3. Custo de leitura não cresce com o histórico da conta (RNF-003)
4. Permite reconstruir todo estado derivado a partir da fonte (RNF-016)
5. Não introduz dependência de framework que imponha seu próprio modelo

## Opções consideradas

1. Saldo materializado mutável, atualizado por `UPDATE`
2. Saldo materializado mais tabela de histórico de movimentações
3. Event sourcing genérico com framework dedicado
4. Ledger append-only de domínio, com posição derivada e snapshot

## Decisão

**Ledger append-only de domínio.** A tabela de lançamentos é a única fonte da verdade do sistema. Recebe apenas `INSERT`. A posição consolidada é resultado de consulta, nunca dado armazenado como verdade primária.

Modelo essencial:

```sql
CREATE TABLE ledger_entries (
    entry_id         uuid        PRIMARY KEY,
    account_id       uuid        NOT NULL REFERENCES accounts(account_id),
    sequence         bigint      NOT NULL,
    direction        smallint    NOT NULL,          -- 1 = credito, -1 = debito
    amount           numeric(19,4) NOT NULL CHECK (amount > 0),
    currency         char(3)     NOT NULL,
    occurred_at      timestamptz NOT NULL,
    recorded_at      timestamptz NOT NULL DEFAULT now(),
    idempotency_key  text        NOT NULL,
    correlation_id   uuid        NOT NULL,
    reversal_of      uuid        NULL REFERENCES ledger_entries(entry_id),
    metadata         jsonb       NOT NULL DEFAULT '{}'::jsonb,

    CONSTRAINT uq_entry_sequence     UNIQUE (account_id, sequence),
    CONSTRAINT uq_entry_idempotency  UNIQUE (account_id, idempotency_key),
    CONSTRAINT uq_entry_reversal     UNIQUE (reversal_of)
);

CREATE INDEX ix_entries_account_occurred
    ON ledger_entries (account_id, occurred_at, sequence)
    INCLUDE (direction, amount);
```

Cada constraint carrega uma regra de negócio, e não por acaso:

| Constraint | Regra | Por que no banco |
|---|---|---|
| `uq_entry_sequence` | RN-006, sequência sem lacuna nem duplicata | Garantia estrutural independente do código de aplicação |
| `uq_entry_idempotency` | RN-005, unicidade da chave por conta | Única defesa que resiste a envio simultâneo ([ADR-0006](./ADR-0006-idempotencia.md)) |
| `uq_entry_reversal` | RN-004, um lançamento é estornado no máximo uma vez | Elimina corrida entre dois pedidos de estorno |
| `CHECK (amount > 0)` | RN-002, valor sempre positivo | Torna impossível o erro de sinal |

O índice `ix_entries_account_occurred` é o que sustenta RNF-003: a consulta histórica percorre apenas a faixa temporal pedida daquela conta, e as colunas incluídas permitem resposta sem acessar a tabela.

A **imutabilidade é garantida por privilégio**, não por disciplina: o usuário de aplicação não recebe `UPDATE` nem `DELETE` sobre esta tabela ([ADR-0009](./ADR-0009-seguranca-e-privilegio-minimo.md)).

### Por que ledger de domínio e não event sourcing genérico

A diferença não é cosmética. Event sourcing genérico trata o evento como artefato técnico de um agregado arbitrário, trazendo consigo versionamento de esquema de evento, upcasting, replay de agregados e um event store com modelo próprio.

Aqui, o "evento" é o próprio conceito de negócio: um lançamento contábil. Ele tem semântica fixa, esquema estável e significado jurídico. Não há agregados variados para reconstruir, não há vocabulário de eventos a versionar. Adotar um framework de event sourcing importaria toda a complexidade acidental sem a contrapartida.

**O que se adota do event sourcing:** fato imutável como fonte da verdade, estado derivado por projeção, snapshot como otimização.
**O que não se adota:** event store genérico, versionamento de eventos, replay de agregados arbitrários, acoplamento a framework.

## Consequências

**Positivas**

- Posição em qualquer instante passado sem estrutura adicional (RF-004)
- Auditoria completa por construção: toda alteração tem origem, autor e instante (CQ-06)
- Contenção reduzida: escrita é inserção, que não disputa versão de linha existente
- Qualquer estado derivado é reconstruível (RNF-016), o que torna cache e snapshot descartáveis com segurança
- Base natural para conciliação contábil e para consumidores de evento

**Negativas**

- **Crescimento monotônico do armazenamento.** Nada é apagado. Com as premissas da [ENF](../specs/ENF-especificacao-nao-funcional.md) §3, a ordem de grandeza é de 2 bilhões de linhas por ano. Mitigação: particionamento declarativo por `recorded_at` e política de retenção a definir (QA-004, R-04)
- **Leitura exige agregação.** Sem snapshot, o custo da consulta cresce com o histórico, reintroduzindo o defeito do legado por outro caminho. É a razão de existir do [ADR-0007](./ADR-0007-snapshot-e-projecao.md), que é dependência obrigatória desta decisão
- **Correção é mais cara.** Erro operacional exige lançamento compensatório e explicação ao cliente, não um `UPDATE` discreto. Este custo é intencional: a alternativa é o `UPDATE` discreto, que é precisamente o que se quer impedir
- Consulta de posição é mais complexa que ler uma coluna

**Neutras**

- O modelo é familiar a qualquer pessoa com formação contábil, e menos familiar a quem vem apenas de CRUD. Exige ênfase na documentação de onboarding (CQ-07)
- O sistema passa a ter dois eixos temporais, `occurred_at` e `recorded_at`, com as implicações tratadas em RN-012

## Análise das opções rejeitadas

**Saldo materializado mutável.** Rejeitado por falhar nos critérios 1, 2, 3 e 4 simultaneamente. É o modelo do legado e a causa provável dos sintomas descritos. *Não voltaria a ser considerado* para este domínio.

**Saldo materializado mais histórico de movimentações.** Rejeitado por manter duas fontes da verdade que podem divergir. Quando divergem, não existe critério objetivo para decidir qual está certa, e a conciliação vira processo manual permanente. *Voltaria a ser considerado* apenas se a posição precisasse ser lida com latência incompatível com qualquer agregação, hipótese afastada pelo snapshot.

**Event sourcing genérico com framework.** Rejeitado pelo critério 5 e pela complexidade acidental descrita acima. *Voltaria a ser a melhor escolha* se o sistema passasse a ter múltiplos agregados com ciclos de vida distintos e necessidade real de replay arbitrário.

## Validação

- [BDD](../specs/BDD-comportamento.md) F01 (imutabilidade recusada pela persistência), F04 (posição em instante), F10 (equivalência entre snapshot e ledger integral)
- Teste de integração que tenta `UPDATE` e `DELETE` com o usuário de aplicação e espera recusa por privilégio
- Métrica `entriesReplayed` por consulta, monitorando RNF-006

## Gatilho de revisão

1. Volume real de lançamentos superando em 100% a premissa da [ENF](../specs/ENF-especificacao-nao-funcional.md) §3, exigindo reavaliar particionamento e retenção
2. Definição da política de retenção (QA-004), que pode introduzir arquivamento com snapshot de abertura
3. Entrada de múltiplos agregados com necessidade de replay, que reabriria a opção de event sourcing genérico

> **Nota de 2026-10-05 (card 37).** O ledger passou a ser particionado por mês de registro, e as restrições de unicidade foram para `entry_keys`, tabela não particionada gravada na mesma transação ([ADR-0013](./ADR-0013-particionamento-do-ledger.md)). Esta decisão não muda: o ledger continua só recebendo inserções, e a aplicação continua sem `UPDATE` nem `DELETE` nele e nas chaves.
