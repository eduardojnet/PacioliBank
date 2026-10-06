-- =====================================================================
-- PacioliBank Ledger - migracao 0005: valores monetarios como numero
-- ADR-0004 (revisao do card 47), EF secao 8.2.
--
-- O contrato passou a levar o valor monetario como numero JSON, e nao mais
-- como texto. Duas colunas guardam texto produzido no formato anterior:
--
--   1. idempotency_records.response_body: a repeticao devolve esse texto
--      (ADR-0006). Sem conversao, o reenvio de um comando anterior a mudanca
--      sairia com o valor entre aspas, num contrato que ja nao o aceita.
--   2. outbox_messages.payload: mensagem ainda nao publicada sairia no
--      formato antigo. As ja publicadas sao convertidas tambem, para que a
--      tabela tenha um formato so.
--
-- A troca e textual e restrita: so os campos monetarios do contrato
-- (amount, balanceAfter), e so quando o valor e um numero decimal entre
-- aspas. response_body e json, e nao jsonb, para preservar o texto exato
-- (ADR-0006); a substituicao mantem a ordem das chaves e tira apenas as
-- aspas. A escala gravada (sempre a da moeda) e preservada: "150.00" vira
-- 150.00, que e o que o codigo novo produz.
--
-- Roda com o papel de migracao. A aplicacao continua sem UPDATE nessas
-- tabelas alem do que ja tinha (ADR-0009).
-- =====================================================================

UPDATE ledger.idempotency_records
   SET response_body = regexp_replace(
           response_body::text,
           '"(amount|balanceAfter)":"(-?[0-9]+(\.[0-9]+)?)"',
           '"\1":\2',
           'g')::json
 WHERE response_body::text ~ '"(amount|balanceAfter)":"-?[0-9]';

UPDATE ledger.outbox_messages
   SET payload = regexp_replace(
           payload::text,
           '"(amount|balanceAfter)": ?"(-?[0-9]+(\.[0-9]+)?)"',
           '"\1": \2',
           'g')::jsonb
 WHERE payload::text ~ '"(amount|balanceAfter)": ?"-?[0-9]';
