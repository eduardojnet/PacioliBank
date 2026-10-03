# Esquema do ledger: entidades e relacionamentos

As 5 tabelas do esquema `ledger`, transcritas de [`db/migrations/0001_esquema_inicial.sql`](../../db/migrations/0001_esquema_inicial.sql) coluna por coluna. Migração nova que altere tabela deste diagrama atualiza o diagrama no mesmo commit. O [C4](./README.md) descreve a arquitetura; este diagrama descreve onde os dados e as regras moram. Os dois se complementam.

**Por que este diagrama importa:** neste sistema, várias regras de negócio não estão no código, estão no banco. Lançamento positivo, sequência sem duplicata, um único estorno por lançamento e chave de idempotência única são constraints; a imutabilidade do ledger é ausência de privilégio. Ler o esquema é ler as invariantes.

```mermaid
erDiagram
    accounts ||--o{ ledger_entries : "account_id"
    accounts ||--o{ balance_snapshots : "account_id"
    accounts ||--o{ idempotency_records : "account_id"
    ledger_entries ||--o| idempotency_records : "entry_id"
    ledger_entries |o--o| ledger_entries : "reversal_of: no máximo um estorno"
    ledger_entries ||--o| outbox_messages : "(account_id, sequence)"

    accounts {
        uuid account_id PK
        uuid customer_id "NOT NULL, indexado. Sem FK: o titular vive no Cadastro"
        char currency "char(3), NOT NULL"
        smallint status "CHECK IN (1, 2, 3)"
        bigint last_sequence "DEFAULT 0, CHECK maior ou igual a 0, fora de índice"
        timestamptz created_at "DEFAULT now()"
    }

    ledger_entries {
        uuid entry_id PK
        uuid account_id FK "UNIQUE com sequence; UNIQUE com idempotency_key"
        bigint sequence "CHECK maior ou igual a 1"
        smallint direction "CHECK IN (1, -1)"
        numeric amount "numeric(19,4), CHECK maior que 0"
        char currency "char(3)"
        timestamptz occurred_at "data do fato"
        timestamptz recorded_at "DEFAULT now(), data do registro"
        text idempotency_key "NOT NULL"
        uuid correlation_id "NOT NULL"
        uuid reversal_of FK "NULL, UNIQUE, aponta para ledger_entries"
        numeric balance_after "numeric(19,4), posição após o lançamento"
        jsonb metadata "DEFAULT vazio"
    }

    balance_snapshots {
        uuid account_id PK, FK
        bigint up_to_sequence PK "âncora em sequência, não em data"
        numeric balance "numeric(19,4)"
        timestamptz as_of
        timestamptz created_at "DEFAULT now()"
    }

    outbox_messages {
        uuid message_id PK
        uuid account_id FK "com sequence: aponta para o lançamento"
        bigint sequence FK "ordem por conta"
        text event_type
        jsonb payload
        timestamptz occurred_at
        timestamptz published_at "NULL até publicar"
        smallint attempts "DEFAULT 0"
        timestamptz next_attempt_at "DEFAULT now()"
    }

    idempotency_records {
        uuid account_id PK, FK
        text idempotency_key PK
        bytea request_hash "SHA-256 do comando canônico"
        smallint response_status
        json response_body "resposta original, texto exato: json, não jsonb"
        uuid entry_id FK
        timestamptz created_at "DEFAULT now(), indexado"
    }
```

## As quatro notas

### 1. Autorrelacionamento do estorno: no máximo um estorno por lançamento

`ledger_entries.reversal_of` aponta para outro `ledger_entries.entry_id`: o estorno é um lançamento novo que referencia o original, nunca uma alteração dele (RN-003, RN-004). Duas constraints fazem o trabalho:

- A FK garante que o original **existe**
- `uq_entries_reversal UNIQUE (reversal_of)` garante que **cada lançamento admite no máximo um estorno**. É garantia estrutural: dois estornos simultâneos do mesmo lançamento não passam, qualquer que seja o código. Medido em `ReversalTests`, com 10 estornos simultâneos e exatamente um aceito

O que a FK **não** garante, e por isso fica com o agregado (`Account.Reverse`): que o original pertence à mesma conta, que não é ele próprio um estorno, e que sentido e valor são os opostos. A FK é sobre existência; a regra de negócio é sobre significado.

### 2. Snapshot ancorado em sequência, não em data

A chave primária de `balance_snapshots` é composta por `(account_id, up_to_sequence)`: o snapshot diz "a posição depois do lançamento de sequência N", não "a posição no dia D". A posição corrente é o snapshot mais recente somado aos lançamentos de sequência maior ([ADR-0007](../adr/ADR-0007-snapshot-e-projecao.md)).

**Consequência:** a consulta histórica (`?asOf=`) não usa snapshot. Sequência é ordem de registro; `occurred_at` é ordem do fato. Com lançamento retroativo, as duas ordens divergem, e um snapshot "até a sequência N" pode conter um fato posterior ao instante pedido. A consulta histórica agrega `ledger_entries` pelo índice `ix_entries_account_occurred`, que cobre as colunas do cálculo. É limitação assumida e declarada no ADR-0007.

### 3. Toda mensagem da outbox aponta para um lançamento

`outbox_messages` referencia `ledger_entries` pela chave composta `fk_outbox_entry (account_id, sequence)`. O par já identifica o lançamento e já é único no ledger (`uq_entries_sequence`), então a chave garante que o lançamento existe e, por ele, que a conta existe. Mensagem sem lançamento correspondente, o "evento fantasma" que o [ADR-0008](../adr/ADR-0008-outbox-transacional.md) existe para eliminar, é recusada pelo banco, e não só evitada pelo código.

**Histórico:** até 2026-10-02 a tabela não tinha chave estrangeira, e nenhum documento dizia por quê (lacuna L-12). A chave foi acrescentada por decisão registrada no ADR-0008, que também registra as alternativas rejeitadas: chave simples para `accounts`, que aceitaria mensagem de lançamento inexistente, e coluna `entry_id`, que duplicaria uma identificação existente.

Sem índice próprio do lado da outbox: o ledger é append-only, então a chave nunca é verificada por exclusão ou alteração do lado referenciado.

### 4. Duas barreiras de idempotência, não duplicação

A unicidade da chave de idempotência aparece duas vezes, e não é redundância: são controles em níveis diferentes ([ADR-0006](../adr/ADR-0006-idempotencia.md)).

| Onde | O que guarda | Para que serve |
|---|---|---|
| `idempotency_records`, PK `(account_id, idempotency_key)` | A impressão do comando (`request_hash`) e a resposta original | Responder o reenvio: impressão igual devolve o original, diferente devolve `409`. É lida sob o bloqueio da conta, antes do agregado decidir (revisão do ADR-0006) |
| `ledger_entries`, `uq_entries_idempotency UNIQUE (account_id, idempotency_key)` | Nada além da chave | Barreira independente no próprio ledger: mesmo que algum caminho grave o lançamento sem passar pelo registro, a mesma chave não gera dois lançamentos |

O registro pode ser expurgado no futuro (ADR-0006 prevê 90 dias); a constraint no ledger, não, porque o ledger é append-only.

## Índices

| Índice | Tabela | Colunas | Para quê |
|---|---|---|---|
| `ix_accounts_customer` | `accounts` | `customer_id` | Contas de um titular |
| `ix_entries_account_occurred` | `ledger_entries` | `(account_id, occurred_at, sequence)` `INCLUDE (direction, amount)` | Consulta histórica sem acessar a tabela (RNF-003) |
| `ix_idempotency_created` | `idempotency_records` | `created_at` | Expurgo por idade |
| `ix_outbox_pending` | `outbox_messages` | `next_attempt_at` `WHERE published_at IS NULL` | Varredura proporcional à fila, não ao histórico |

As constraints `UNIQUE` também criam índices; `uq_entries_sequence` é o que atende o extrato paginado por cursor.

## Privilégios do papel da aplicação

A imutabilidade do ledger não está desenhada acima porque não é estrutura: é ausência de privilégio ([ADR-0009](../adr/ADR-0009-seguranca-e-privilegio-minimo.md)).

| Tabela | `pacioli_runtime` pode | Não pode |
|---|---|---|
| `ledger_entries` | `SELECT`, `INSERT` | `UPDATE`, `DELETE` |
| `balance_snapshots` | `SELECT`, `INSERT` | `UPDATE`, `DELETE` |
| `idempotency_records` | `SELECT`, `INSERT` | `UPDATE`, `DELETE` |
| `outbox_messages` | `SELECT`, `INSERT`, `UPDATE` | `DELETE` |
| `accounts` | `SELECT`, e `UPDATE` só na coluna `last_sequence` | Alterar status, moeda ou titular |

`pacioli_readonly` tem só `SELECT`. `pacioli_migrator` é dono do esquema e não é usado pela aplicação em execução.

## Conferência contra o script

Conferido em 2026-10-02 contra `db/init/001_roles_and_schema.sql` (movido sem alteração de esquema para `db/migrations/0001_esquema_inicial.sql` no card 27), linha por linha, e contra o catálogo do PostgreSQL com o script aplicado (`information_schema.columns` e `pg_constraint`), por comparação automática de nome, tipo e ordem de cada coluna:

| Item do script | No diagrama |
|---|---|
| 5 tabelas do esquema `ledger` | 5 entidades |
| 40 colunas (6 + 13 + 5 + 7 + 9) | 40 atributos, mesmo nome, mesmo tipo, mesma ordem |
| 5 PK: `account_id`; `entry_id`; `pk_balance_snapshots (account_id, up_to_sequence)`; `pk_idempotency (account_id, idempotency_key)`; `message_id` | 5 chaves primárias, duas compostas |
| 6 FK: `ledger_entries.account_id`, `ledger_entries.reversal_of`, `balance_snapshots.account_id`, `idempotency_records.account_id`, `idempotency_records.entry_id`, `fk_outbox_entry (account_id, sequence)` | 6 relacionamentos |
| 3 UNIQUE: `uq_entries_sequence`, `uq_entries_idempotency`, `uq_entries_reversal` | Nos atributos de `ledger_entries` |
| 5 CHECK: `ck_accounts_status`, `ck_accounts_sequence`, `ck_entries_amount`, `ck_entries_direction`, `ck_entries_sequence` | Nos atributos |
| 4 índices explícitos | Tabela de índices |
| `GRANT`s dos 3 papéis | Tabela de privilégios |

`accounts` é criada com `fillfactor = 70`, para que a atualização de `last_sequence` caiba na mesma página (atualização HOT, ADR-0005). Atributo físico, sem representação em diagrama de entidades.
