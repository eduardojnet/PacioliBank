-- Massa minima para o ambiente local. Nao e usada em teste automatizado:
-- cada teste de integracao cria a sua propria massa (ADR-0010).
INSERT INTO ledger.accounts (account_id, customer_id, currency, status, last_sequence)
VALUES
    ('11111111-1111-1111-1111-111111111111', '99999999-9999-9999-9999-999999999999', 'BRL', 1, 0),
    ('22222222-2222-2222-2222-222222222222', '88888888-8888-8888-8888-888888888888', 'BRL', 1, 0);
