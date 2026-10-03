-- Massa minima para o ambiente local. Nao e usada em teste automatizado:
-- cada teste de integracao cria a sua propria massa (ADR-0010).
--
-- 1111 e 2222: exemplos do README e testes livres.
-- 3333, 4444 e 5555: reservadas ao painel de evidencia (ADR-0011), uma por
-- demonstracao, para que uma nao altere o que a outra mostra. Nao ha endpoint
-- de criacao de conta: o ciclo de vida da conta pertence ao Cadastro (EF 3.2).
INSERT INTO ledger.accounts (account_id, customer_id, currency, status, last_sequence)
VALUES
    ('11111111-1111-1111-1111-111111111111', '99999999-9999-9999-9999-999999999999', 'BRL', 1, 0),
    ('22222222-2222-2222-2222-222222222222', '88888888-8888-8888-8888-888888888888', 'BRL', 1, 0),
    ('33333333-3333-3333-3333-333333333333', '77777777-7777-7777-7777-777777777777', 'BRL', 1, 0),
    ('44444444-4444-4444-4444-444444444444', '77777777-7777-7777-7777-777777777777', 'BRL', 1, 0),
    ('55555555-5555-5555-5555-555555555555', '77777777-7777-7777-7777-777777777777', 'BRL', 1, 0);
