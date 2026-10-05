-- =====================================================================
-- PacioliBank Ledger - migracao 0004: transferencia entre contas
-- ADR-0014, card 38 (RF-012, RN-013).
--
-- A transferencia e um debito na origem e um credito no destino, gravados
-- na mesma transacao, com as duas contas bloqueadas em ordem crescente de
-- identificador (ADR-0005, revisao do card 38). As pernas sao lancamentos
-- comuns do ledger, cada uma na sua conta.
--
-- Esta tabela amarra as duas pernas a uma transferencia, e o banco garante
-- a coerencia (regra 6): a perna de debito e um debito da origem, a de
-- credito e um credito do destino, as duas pelo valor e na moeda da
-- transferencia, gravadas no mesmo instante de registro, e nenhuma perna
-- serve a duas transferencias.
-- =====================================================================

-- ---------------------------------------------------------------------
-- 1. Alvo das chaves estrangeiras das pernas. Inclui recorded_at, a coluna
--    da particao: so assim a restricao unica e aceita na tabela particionada
--    (ADR-0013). entry_id ja e unico; esta restricao existe para que a
--    chave estrangeira possa comparar conta, sentido, valor e moeda.
-- ---------------------------------------------------------------------
ALTER TABLE ledger.ledger_entries
    ADD CONSTRAINT uq_entries_leg UNIQUE (entry_id, recorded_at, account_id, direction, amount, currency);

-- ---------------------------------------------------------------------
-- 2. Transferencias. debit_direction e credit_direction sao constantes,
--    fixadas por CHECK: existem para entrar na chave estrangeira, que nao
--    aceita literal.
-- ---------------------------------------------------------------------
CREATE TABLE ledger.transfers (
    transfer_id            uuid          NOT NULL,
    source_account_id      uuid          NOT NULL REFERENCES ledger.accounts (account_id),
    destination_account_id uuid          NOT NULL REFERENCES ledger.accounts (account_id),
    amount                 numeric(19,4) NOT NULL,
    currency               char(3)       NOT NULL,
    recorded_at            timestamptz   NOT NULL,
    debit_entry_id         uuid          NOT NULL,
    credit_entry_id        uuid          NOT NULL,
    debit_direction        smallint      NOT NULL DEFAULT -1,
    credit_direction       smallint      NOT NULL DEFAULT 1,

    CONSTRAINT pk_transfers                   PRIMARY KEY (transfer_id),
    CONSTRAINT ck_transfers_distinct_accounts CHECK (source_account_id <> destination_account_id),
    CONSTRAINT ck_transfers_amount            CHECK (amount > 0),                 -- RN-002
    CONSTRAINT ck_transfers_debit_direction   CHECK (debit_direction = -1),
    CONSTRAINT ck_transfers_credit_direction  CHECK (credit_direction = 1),
    CONSTRAINT uq_transfers_debit             UNIQUE (debit_entry_id),
    CONSTRAINT uq_transfers_credit            UNIQUE (credit_entry_id),
    CONSTRAINT fk_transfers_debit FOREIGN KEY
        (debit_entry_id, recorded_at, source_account_id, debit_direction, amount, currency)
        REFERENCES ledger.ledger_entries (entry_id, recorded_at, account_id, direction, amount, currency),
    CONSTRAINT fk_transfers_credit FOREIGN KEY
        (credit_entry_id, recorded_at, destination_account_id, credit_direction, amount, currency)
        REFERENCES ledger.ledger_entries (entry_id, recorded_at, account_id, direction, amount, currency)
);

-- Transferencias de uma conta, nos dois sentidos.
CREATE INDEX ix_transfers_source      ON ledger.transfers (source_account_id);
CREATE INDEX ix_transfers_destination ON ledger.transfers (destination_account_id);

-- ---------------------------------------------------------------------
-- 3. Privilegios (ADR-0009): como o ledger, so SELECT e INSERT para a
--    aplicacao. Transferencia gravada nao se altera nem se apaga.
-- ---------------------------------------------------------------------
GRANT SELECT, INSERT ON ledger.transfers TO pacioli_runtime;
GRANT SELECT ON ledger.transfers TO pacioli_readonly;
