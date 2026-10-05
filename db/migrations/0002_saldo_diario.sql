-- =====================================================================
-- PacioliBank Ledger - migracao 0002: fechamento diario por conta
-- ADR-0007 (revisao do card 32): posicao em instante passado a partir do
-- fechamento do dia anterior, em vez de agregar todo o historico.
--
-- Derivado do ledger, como o snapshot (ADR-0003): o ledger continua sendo a
-- unica fonte da verdade, e esta tabela pode ser reconstruida a partir dele
-- com o mesmo SELECT do preenchimento abaixo.
--
-- Dia em UTC, pela data do fato (occurred_at). E particao interna: nao
-- aparece no contrato, e a posicao em qualquer instante nao depende dela.
-- =====================================================================

CREATE TABLE ledger.daily_balances (
    account_id      uuid          NOT NULL REFERENCES ledger.accounts (account_id),
    day             date          NOT NULL,
    -- Posicao ao fim do dia: soma de todo lancamento com fato ate 23:59:59.999999 UTC.
    closing_balance numeric(19,4) NOT NULL,
    -- Maior sequencia entre esses lancamentos; e o computedAtSequence da consulta.
    last_sequence   bigint        NOT NULL,

    CONSTRAINT pk_daily_balances       PRIMARY KEY (account_id, day),
    CONSTRAINT ck_daily_last_sequence  CHECK (last_sequence >= 1)
);

-- Preenchimento a partir do ledger ja gravado: soma acumulada, por conta,
-- dos movimentos de cada dia.
INSERT INTO ledger.daily_balances (account_id, day, closing_balance, last_sequence)
SELECT account_id,
       day,
       SUM(day_amount)  OVER (PARTITION BY account_id ORDER BY day),
       MAX(day_last)    OVER (PARTITION BY account_id ORDER BY day)
  FROM (SELECT account_id,
               (occurred_at AT TIME ZONE 'UTC')::date AS day,
               SUM(direction * amount)               AS day_amount,
               MAX(sequence)                         AS day_last
          FROM ledger.ledger_entries
         GROUP BY account_id, (occurred_at AT TIME ZONE 'UTC')::date) movimentos;

-- ---------------------------------------------------------------------
-- Privilegios (ADR-0009). Tabela derivada: a aplicacao atualiza o
-- fechamento na transacao do lancamento, inclusive os dias seguintes a um
-- lancamento retroativo. DELETE, nunca. O ledger continua so SELECT e INSERT.
-- O papel de leitura recebeu SELECT nas tabelas existentes na 0001; tabela
-- nova precisa do seu.
-- ---------------------------------------------------------------------
GRANT SELECT, INSERT, UPDATE ON ledger.daily_balances TO pacioli_runtime;
GRANT SELECT ON ledger.daily_balances TO pacioli_readonly;
