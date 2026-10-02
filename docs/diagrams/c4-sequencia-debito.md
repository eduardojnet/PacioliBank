# C4: Registro de débito sob concorrência

Nível de código para o caminho mais crítico do sistema: um débito, do HTTP ao `COMMIT`. Desenhado **a partir de** `PostgresLedgerStore.PostOnceAsync`. Convenções em [README](./README.md).

```mermaid
sequenceDiagram
    autonumber
    actor O as Originador
    participant E as LedgerEndpoints
    participant S as LedgerService
    participant P as PostgresLedgerStore
    participant A as Account (agregado)
    participant DB as PostgreSQL

    O->>E: POST /api/v1/accounts/{id}/debits com Idempotency-Key
    Note over O,E: Autorização por titularidade: especificada, NÃO implementada (RF-009)
    E->>S: PostAsync(PostingCommand)
    S->>S: exige a chave, converte o valor, calcula a impressão SHA-256
    S->>P: PostAsync(conta, PostingRequest, impressão)
    P->>DB: BEGIN e SET LOCAL lock_timeout = 3s

    rect rgb(255, 243, 214)
        Note over P,DB: ADR-0005: a ordem destes dois passos é a arquitetura
        P->>DB: SELECT da linha da conta FOR NO KEY UPDATE
        Note right of DB: Serializa as escritas desta conta.<br/>Contas distintas não se bloqueiam.
        P->>DB: posição = último snapshot + lançamentos posteriores
    end

    P->>A: Rehydrate(controle, posição)
    P->>A: Post(comando)

    alt posição resultante seria negativa
        A-->>P: InsufficientFundsException
        Note over A: RN-006: a sequência não foi consumida
        P-->>E: exceção, transação descartada
        E-->>O: 422 INSUFFICIENT_FUNDS
    else invariantes satisfeitas
        A-->>P: LedgerEntry com sequência e posição após

        rect rgb(222, 236, 252)
            Note over P,DB: Mesma transação: tudo ou nada
            P->>DB: INSERT ledger_entries
            P->>DB: UPDATE accounts.last_sequence
            P->>DB: INSERT idempotency_records
            P->>DB: INSERT outbox_messages
            opt sequência múltipla de 100
                P->>DB: INSERT balance_snapshots (ADR-0007, sem processo assíncrono)
            end
        end

        alt chave já gravada, violação de pk_idempotency
            Note over P,DB: ADR-0006: caminho esperado, não excepcional
            P->>DB: ROLLBACK e leitura do registro original
            alt impressão confere
                P-->>E: resultado original
                E-->>O: 200 com Idempotency-Replayed: true, corpo idêntico
            else impressão difere
                P-->>E: IdempotencyConflictException
                E-->>O: 409 IDEMPOTENCY_KEY_CONFLICT
            end
        else gravação bem-sucedida
            P->>DB: COMMIT
            P-->>E: PostEntryResult
            E-->>O: 201 Created
        end
    end
```

## O que o diagrama fixa

| Passo | Decisão | Onde verificar |
|---|---|---|
| 6 e 7 | Bloquear **antes** de ler a posição. Inverter reabre a corrida que RN-001 proíbe | `LedgerSql.LockAccountForWrite`; teste F07 em `ConcurrencyTests` |
| 6 | `FOR NO KEY UPDATE`, não `FOR UPDATE`: não bloqueia leitura de chave estrangeira sobre a conta | ADR-0005 |
| 5 | `lock_timeout` de 3 s: sob contenção, rejeitar como repetível em vez de esgotar o pool | ADR-0005, RNF-014 |
| 9 a 12 | Rejeição antes de qualquer gravação: a sequência não abre lacuna | RN-006; teste `Rejeicao_nao_consome_sequencia` |
| 14 a 18 | Lançamento, sequência, idempotência e outbox na mesma transação | EF CU-01, passo 8 |
| 19 a 23 | Repetição detectada pela violação de chave primária, nunca por consulta prévia | ADR-0006 |

**Fora do diagrama, mas no mesmo método:** conflito de concorrência (deadlock, `lock_timeout`, sequência duplicada) leva a até 3 tentativas com recuo exponencial; esgotadas, `503 SERVICE_UNAVAILABLE`, repetível com a mesma chave (RNF-004).

## Limitação conhecida que o diagrama torna visível

O agregado decide (passo 9) **antes** de a repetição ser detectada (passo 19). Se o saldo caiu entre o envio original e o reenvio, o reenvio é rejeitado no passo 10 e nunca chega à detecção de chave: o cliente recebe `422` para um débito que foi efetivado. Lacuna **L-10** no [`ESTADO.md`](../ESTADO.md) §6, verificada por execução e aberta: a correção exige revisar o ADR-0006.

O estorno segue o mesmo caminho, com um passo a mais: o lançamento original é lido sob o mesmo bloqueio, e quem decide titularidade, estorno de estorno, duplicidade e saldo, nessa ordem, é o agregado (`Account.Reverse`).
