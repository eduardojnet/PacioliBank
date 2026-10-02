-- =====================================================================
-- PacioliBank Ledger - esquema inicial
-- ADR-0003 (ledger append-only) e ADR-0009 (privilegio minimo).
--
-- Executado pelo entrypoint do container PostgreSQL na PRIMEIRA criacao
-- do volume. Substituido por DbUp no proximo incremento, porque este
-- mecanismo nao reaplica em volume existente (ver RNF-038).
-- =====================================================================

CREATE SCHEMA IF NOT EXISTS ledger;

-- ---------------------------------------------------------------------
-- Papeis. ADR-0009: a imutabilidade do ledger nao depende de disciplina
-- de codigo, depende da ausencia de privilegio.
-- ---------------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pacioli_runtime') THEN
        CREATE ROLE pacioli_runtime LOGIN PASSWORD 'pacioli_local_dev';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pacioli_readonly') THEN
        CREATE ROLE pacioli_readonly LOGIN PASSWORD 'pacioli_local_dev';
    END IF;
END
$$;

GRANT USAGE ON SCHEMA ledger TO pacioli_runtime, pacioli_readonly;

-- ---------------------------------------------------------------------
-- Contas. Dado de referencia replicado do contexto Cadastro.
-- last_sequence fica fora de qualquer indice de proposito para permitir
-- atualizacao HOT (ADR-0005).
-- ---------------------------------------------------------------------
CREATE TABLE ledger.accounts (
    account_id    uuid        PRIMARY KEY,
    customer_id   uuid        NOT NULL,
    currency      char(3)     NOT NULL,
    status        smallint    NOT NULL,          -- 1 Active, 2 Blocked, 3 Closed
    last_sequence bigint      NOT NULL DEFAULT 0,
    created_at    timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_accounts_status   CHECK (status IN (1, 2, 3)),
    CONSTRAINT ck_accounts_sequence CHECK (last_sequence >= 0)
) WITH (fillfactor = 70);

CREATE INDEX ix_accounts_customer ON ledger.accounts (customer_id);

-- ---------------------------------------------------------------------
-- Lancamentos. Imutaveis. Cada constraint carrega uma regra de negocio.
-- ---------------------------------------------------------------------
CREATE TABLE ledger.ledger_entries (
    entry_id        uuid          PRIMARY KEY,
    account_id      uuid          NOT NULL REFERENCES ledger.accounts (account_id),
    sequence        bigint        NOT NULL,
    direction       smallint      NOT NULL,      -- 1 credito, -1 debito
    amount          numeric(19,4) NOT NULL,
    currency        char(3)       NOT NULL,
    occurred_at     timestamptz   NOT NULL,
    recorded_at     timestamptz   NOT NULL DEFAULT now(),
    idempotency_key text          NOT NULL,
    correlation_id  uuid          NOT NULL,
    reversal_of     uuid          NULL REFERENCES ledger.ledger_entries (entry_id),
    -- Posicao imediatamente apos este lancamento. Nao e fonte da verdade: e
    -- materializacao do que o agregado ja calculou sob bloqueio. Evita que o
    -- extrato com saldo progressivo (RF-005) custe O(n^2).
    balance_after   numeric(19,4) NOT NULL,
    metadata        jsonb         NOT NULL DEFAULT '{}'::jsonb,

    CONSTRAINT ck_entries_amount      CHECK (amount > 0),              -- RN-002
    CONSTRAINT ck_entries_direction   CHECK (direction IN (1, -1)),
    CONSTRAINT ck_entries_sequence    CHECK (sequence >= 1),           -- RN-006
    CONSTRAINT uq_entries_sequence    UNIQUE (account_id, sequence),   -- RN-006
    CONSTRAINT uq_entries_idempotency UNIQUE (account_id, idempotency_key), -- RN-005
    CONSTRAINT uq_entries_reversal    UNIQUE (reversal_of)             -- RN-004
);

-- Sustenta RNF-003: a consulta historica le apenas a faixa temporal pedida,
-- sem acessar a tabela, porque as colunas do calculo estao no indice.
CREATE INDEX ix_entries_account_occurred
    ON ledger.ledger_entries (account_id, occurred_at, sequence)
    INCLUDE (direction, amount);

-- ---------------------------------------------------------------------
-- Snapshots. Derivados e descartaveis por definicao (RN-010, ADR-0007).
-- ---------------------------------------------------------------------
CREATE TABLE ledger.balance_snapshots (
    account_id     uuid          NOT NULL REFERENCES ledger.accounts (account_id),
    up_to_sequence bigint        NOT NULL,
    balance        numeric(19,4) NOT NULL,
    as_of          timestamptz   NOT NULL,
    created_at     timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT pk_balance_snapshots PRIMARY KEY (account_id, up_to_sequence)
);

-- ---------------------------------------------------------------------
-- Idempotencia. A colisao de chave primaria E o mecanismo de deteccao,
-- nao um caso excepcional (ADR-0006).
-- ---------------------------------------------------------------------
CREATE TABLE ledger.idempotency_records (
    account_id      uuid        NOT NULL REFERENCES ledger.accounts (account_id),
    idempotency_key text        NOT NULL,
    request_hash    bytea       NOT NULL,
    response_status smallint    NOT NULL,
    -- json, e nao jsonb: jsonb reordena chaves e normaliza espacos, e a
    -- repeticao precisa devolver o texto exato da resposta original (ADR-0006).
    response_body   json        NOT NULL,
    entry_id        uuid        NOT NULL REFERENCES ledger.ledger_entries (entry_id),
    created_at      timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_idempotency PRIMARY KEY (account_id, idempotency_key)
);

CREATE INDEX ix_idempotency_created ON ledger.idempotency_records (created_at);

-- ---------------------------------------------------------------------
-- Outbox. Gravado na transacao do lancamento, publicado depois (ADR-0008).
-- O indice parcial mantem a varredura proporcional a fila, nao ao historico.
--
-- fk_outbox_entry: toda mensagem corresponde a um lancamento existente. A
-- chave e o par (account_id, sequence), unico no ledger por
-- uq_entries_sequence; garante o lancamento e, por ele, a conta. Sem indice
-- proprio do lado da outbox: o ledger e append-only, entao a chave nunca e
-- verificada por exclusao ou alteracao do lado referenciado (ADR-0008, L-12).
-- ---------------------------------------------------------------------
CREATE TABLE ledger.outbox_messages (
    message_id      uuid        PRIMARY KEY,
    account_id      uuid        NOT NULL,
    sequence        bigint      NOT NULL,
    event_type      text        NOT NULL,
    payload         jsonb       NOT NULL,
    occurred_at     timestamptz NOT NULL,
    published_at    timestamptz NULL,
    attempts        smallint    NOT NULL DEFAULT 0,
    next_attempt_at timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT fk_outbox_entry FOREIGN KEY (account_id, sequence)
        REFERENCES ledger.ledger_entries (account_id, sequence)
);

CREATE INDEX ix_outbox_pending
    ON ledger.outbox_messages (next_attempt_at)
    WHERE published_at IS NULL;

-- =====================================================================
-- Privilegios. ADR-0009 e RNF-025.
-- A aplicacao NAO recebe UPDATE nem DELETE sobre o ledger. Alterar um
-- lancamento gravado e impossivel para ela, qualquer que seja o codigo.
-- =====================================================================
GRANT SELECT, INSERT ON ledger.ledger_entries      TO pacioli_runtime;
GRANT SELECT, INSERT ON ledger.balance_snapshots   TO pacioli_runtime;
GRANT SELECT, INSERT ON ledger.idempotency_records TO pacioli_runtime;
GRANT SELECT, INSERT, UPDATE ON ledger.outbox_messages TO pacioli_runtime;

-- Somente a coluna de controle de sequencia. O ciclo de vida da conta
-- pertence a outro contexto delimitado (EF secao 3.2), e o privilegio
-- reflete essa fronteira.
GRANT SELECT ON ledger.accounts TO pacioli_runtime;
GRANT UPDATE (last_sequence) ON ledger.accounts TO pacioli_runtime;

GRANT SELECT ON ALL TABLES IN SCHEMA ledger TO pacioli_readonly;
