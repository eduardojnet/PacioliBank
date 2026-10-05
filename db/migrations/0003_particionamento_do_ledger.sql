-- =====================================================================
-- PacioliBank Ledger - migracao 0003: ledger particionado por mes de registro
-- ADR-0013, card 37 (risco R-04 da ENF).
--
-- No PostgreSQL, restricao unica de tabela particionada so e aceita com a
-- coluna da particao, e entao so vale dentro de cada particao. Para nao
-- perder as garantias de sequencia (RN-006), idempotencia (RN-005), estorno
-- unico (RN-004) e identidade, elas passam para entry_keys, uma tabela NAO
-- particionada, gravada na mesma transacao do lancamento. Cada linha do
-- ledger e amarrada pelo banco a sua chave, com os mesmos valores.
--
-- Particao por recorded_at (data do registro): cresce em ordem, e o
-- lancamento retroativo cai no mes corrente, nunca num periodo antigo.
-- Retencao (QA-004) nao decidida: nada aqui arquiva ou apaga periodo.
-- =====================================================================

-- ---------------------------------------------------------------------
-- 1. Libera os nomes. O ledger atual vira ledger_entries_antigo, e suas
--    restricoes e indices sao renomeados, para que os nomes acompanhem as
--    regras na estrutura nova. O codigo reconhece o conflito pelo nome.
-- ---------------------------------------------------------------------
ALTER TABLE ledger.ledger_entries RENAME TO ledger_entries_antigo;
ALTER TABLE ledger.ledger_entries_antigo RENAME CONSTRAINT ledger_entries_pkey    TO antigo_pkey;
ALTER TABLE ledger.ledger_entries_antigo RENAME CONSTRAINT uq_entries_sequence    TO antigo_uq_sequence;
ALTER TABLE ledger.ledger_entries_antigo RENAME CONSTRAINT uq_entries_idempotency TO antigo_uq_idempotency;
ALTER TABLE ledger.ledger_entries_antigo RENAME CONSTRAINT uq_entries_reversal    TO antigo_uq_reversal;
ALTER INDEX ledger.ix_entries_account_occurred RENAME TO antigo_ix_account_occurred;

-- ---------------------------------------------------------------------
-- 2. Chaves. Nao particionada: as restricoes valem para todo o historico.
-- ---------------------------------------------------------------------
CREATE TABLE ledger.entry_keys (
    entry_id        uuid        NOT NULL,
    account_id      uuid        NOT NULL REFERENCES ledger.accounts (account_id),
    sequence        bigint      NOT NULL,
    idempotency_key text        NOT NULL,
    reversal_of     uuid        NULL,
    recorded_at     timestamptz NOT NULL,

    CONSTRAINT pk_entry_keys          PRIMARY KEY (entry_id),
    CONSTRAINT ck_keys_sequence       CHECK (sequence >= 1),                 -- RN-006
    CONSTRAINT uq_entries_sequence    UNIQUE (account_id, sequence),         -- RN-006
    CONSTRAINT uq_entries_idempotency UNIQUE (account_id, idempotency_key),  -- RN-005
    CONSTRAINT uq_entries_reversal    UNIQUE (reversal_of),                  -- RN-004
    CONSTRAINT fk_keys_reversal       FOREIGN KEY (reversal_of) REFERENCES ledger.entry_keys (entry_id),

    -- Alvos das chaves estrangeiras compostas do ledger (passo 3).
    CONSTRAINT uq_keys_row            UNIQUE (entry_id, account_id, sequence, idempotency_key),
    CONSTRAINT uq_keys_reversal_row   UNIQUE (entry_id, reversal_of)
);

-- ---------------------------------------------------------------------
-- 3. Ledger particionado. Mesmas colunas e verificacoes de antes.
--
-- fk_entries_keys: toda linha tem a sua chave, com a mesma conta, sequencia
-- e chave de idempotencia. Sem ela, uma linha com sequencia diferente da
-- registrada escaparia da unicidade.
-- fk_entries_reversal: o estorno registrado na linha e o da chave. So e
-- verificada quando reversal_of nao e nulo (MATCH SIMPLE); o caso inverso,
-- chave com estorno e linha sem, so impediria um estorno legitimo, nunca
-- permitiria um segundo.
-- ---------------------------------------------------------------------
CREATE TABLE ledger.ledger_entries (
    entry_id        uuid          NOT NULL,
    account_id      uuid          NOT NULL REFERENCES ledger.accounts (account_id),
    sequence        bigint        NOT NULL,
    direction       smallint      NOT NULL,      -- 1 credito, -1 debito
    amount          numeric(19,4) NOT NULL,
    currency        char(3)       NOT NULL,
    occurred_at     timestamptz   NOT NULL,
    recorded_at     timestamptz   NOT NULL DEFAULT now(),
    idempotency_key text          NOT NULL,
    correlation_id  uuid          NOT NULL,
    reversal_of     uuid          NULL,
    balance_after   numeric(19,4) NOT NULL,
    metadata        jsonb         NOT NULL DEFAULT '{}'::jsonb,

    CONSTRAINT pk_ledger_entries     PRIMARY KEY (entry_id, recorded_at),
    CONSTRAINT ck_entries_amount     CHECK (amount > 0),               -- RN-002
    CONSTRAINT ck_entries_direction  CHECK (direction IN (1, -1)),
    CONSTRAINT ck_entries_sequence   CHECK (sequence >= 1),            -- RN-006
    CONSTRAINT fk_entries_keys       FOREIGN KEY (entry_id, account_id, sequence, idempotency_key)
        REFERENCES ledger.entry_keys (entry_id, account_id, sequence, idempotency_key),
    CONSTRAINT fk_entries_reversal   FOREIGN KEY (entry_id, reversal_of)
        REFERENCES ledger.entry_keys (entry_id, reversal_of)
) PARTITION BY RANGE (recorded_at);

-- RNF-003: consulta historica pelo indice, sem acessar a tabela.
CREATE INDEX ix_entries_account_occurred
    ON ledger.ledger_entries (account_id, occurred_at, sequence)
    INCLUDE (direction, amount);

-- Extrato e posicao corrente por sequencia. Antes atendidos pelo indice de
-- uq_entries_sequence, que agora vive em entry_keys.
CREATE INDEX ix_entries_account_sequence
    ON ledger.ledger_entries (account_id, sequence);

-- ---------------------------------------------------------------------
-- 4. Particao de um mes. Chamada aqui e pelo migrador a cada execucao, que
--    abre os meses seguintes. Nao e da aplicacao, que nao tem DDL.
-- ---------------------------------------------------------------------
CREATE FUNCTION ledger.ensure_month_partition(month date) RETURNS boolean
LANGUAGE plpgsql AS $$
DECLARE
    inicio date := date_trunc('month', month)::date;
    nome   text := 'ledger_entries_' || to_char(date_trunc('month', month), 'YYYY_MM');
BEGIN
    IF to_regclass('ledger.' || nome) IS NOT NULL THEN
        RETURN false;
    END IF;

    EXECUTE format(
        'CREATE TABLE ledger.%I PARTITION OF ledger.ledger_entries FOR VALUES FROM (%L) TO (%L)',
        nome,
        inicio::timestamp AT TIME ZONE 'UTC',
        (inicio + interval '1 month')::timestamp AT TIME ZONE 'UTC');
    RETURN true;
END
$$;

REVOKE ALL ON FUNCTION ledger.ensure_month_partition(date) FROM PUBLIC;

-- ---------------------------------------------------------------------
-- 5. Particoes: do primeiro mes com dado ate 12 meses a frente, e a padrao,
--    que recebe o que cair fora delas em vez de recusar o lancamento.
-- ---------------------------------------------------------------------
CREATE TABLE ledger.ledger_entries_default PARTITION OF ledger.ledger_entries DEFAULT;

DO $$
DECLARE
    mes date;
    fim date := (date_trunc('month', now() AT TIME ZONE 'UTC') + interval '12 months')::date;
BEGIN
    SELECT date_trunc('month', LEAST(COALESCE(min(recorded_at), now()), now()) AT TIME ZONE 'UTC')::date
      INTO mes
      FROM ledger.ledger_entries_antigo;

    WHILE mes <= fim LOOP
        PERFORM ledger.ensure_month_partition(mes);
        mes := (mes + interval '1 month')::date;
    END LOOP;
END
$$;

-- ---------------------------------------------------------------------
-- 6. Dados. Chaves primeiro: o ledger aponta para elas.
-- ---------------------------------------------------------------------
INSERT INTO ledger.entry_keys (entry_id, account_id, sequence, idempotency_key, reversal_of, recorded_at)
SELECT entry_id, account_id, sequence, idempotency_key, reversal_of, recorded_at
  FROM ledger.ledger_entries_antigo;

INSERT INTO ledger.ledger_entries
    (entry_id, account_id, sequence, direction, amount, currency, occurred_at, recorded_at,
     idempotency_key, correlation_id, reversal_of, balance_after, metadata)
SELECT entry_id, account_id, sequence, direction, amount, currency, occurred_at, recorded_at,
       idempotency_key, correlation_id, reversal_of, balance_after, metadata
  FROM ledger.ledger_entries_antigo;

-- Outbox e idempotencia passam a apontar para as chaves, que tem a
-- unicidade global; o nome fk_outbox_entry e mantido (ADR-0008).
ALTER TABLE ledger.outbox_messages DROP CONSTRAINT fk_outbox_entry;
ALTER TABLE ledger.outbox_messages ADD CONSTRAINT fk_outbox_entry
    FOREIGN KEY (account_id, sequence) REFERENCES ledger.entry_keys (account_id, sequence);

ALTER TABLE ledger.idempotency_records DROP CONSTRAINT idempotency_records_entry_id_fkey;
ALTER TABLE ledger.idempotency_records ADD CONSTRAINT fk_idempotency_entry
    FOREIGN KEY (entry_id) REFERENCES ledger.entry_keys (entry_id);

DROP TABLE ledger.ledger_entries_antigo;

-- ---------------------------------------------------------------------
-- 7. Privilegios (ADR-0009): os mesmos de antes. Ledger e chaves, so
--    SELECT e INSERT para a aplicacao; nada de UPDATE nem DELETE.
-- ---------------------------------------------------------------------
GRANT SELECT, INSERT ON ledger.ledger_entries TO pacioli_runtime;
GRANT SELECT, INSERT ON ledger.entry_keys     TO pacioli_runtime;
GRANT SELECT ON ledger.ledger_entries, ledger.entry_keys TO pacioli_readonly;
