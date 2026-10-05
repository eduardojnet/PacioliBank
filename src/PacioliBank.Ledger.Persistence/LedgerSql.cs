namespace PacioliBank.Ledger.Persistence;

/// <summary>
/// SQL do modulo, reunido em um unico lugar para revisao.
/// </summary>
/// <remarks>
/// O SQL e explicito por decisao (ADR-0002): a consulta executada e a consulta
/// escrita, revisavel em code review e estavel em plano de execucao. Em sistema
/// financeiro isso e vantagem de auditoria, nao preferencia de estilo.
/// </remarks>
internal static class LedgerSql
{
    /// <summary>
    /// Serializa as escritas desta conta e carrega o estado de controle.
    ///
    /// FOR NO KEY UPDATE em vez de FOR UPDATE: a variante mais fraca nao bloqueia
    /// leituras de chave estrangeira sobre a conta, reduzindo interferencia sem
    /// perder a exclusividade entre escritores (ADR-0005).
    ///
    /// O bloqueio ocorre ANTES da leitura da posicao. Essa ordem e a decisao:
    /// inverte-la reabre a condicao de corrida que a invariante RN-001 proibe.
    /// </summary>
    internal const string LockAccountForWrite = """
        SELECT account_id   AS AccountId,
               customer_id  AS CustomerId,
               currency     AS Currency,
               status       AS Status,
               last_sequence AS LastSequence
          FROM ledger.accounts
         WHERE account_id = @accountId
           FOR NO KEY UPDATE
        """;

    /// <summary>
    /// Posicao corrente: snapshot mais os lancamentos posteriores a ele.
    /// Com snapshot a cada N lancamentos, a soma percorre no maximo N-1 linhas,
    /// qualquer que seja o tamanho do historico da conta (ADR-0007, RNF-003).
    /// </summary>
    internal const string SelectCurrentBalance = """
        WITH snap AS (
            SELECT up_to_sequence, balance
              FROM ledger.balance_snapshots
             WHERE account_id = @accountId
             ORDER BY up_to_sequence DESC
             LIMIT 1
        ),
        delta AS (
            SELECT COALESCE(SUM(e.direction * e.amount), 0) AS amount,
                   COUNT(*)                                 AS entries
              FROM ledger.ledger_entries e
             WHERE e.account_id = @accountId
               AND e.sequence > COALESCE((SELECT up_to_sequence FROM snap), 0)
        )
        SELECT COALESCE((SELECT balance FROM snap), 0) + (SELECT amount FROM delta) AS Balance,
               (SELECT entries FROM delta)                                          AS EntriesReplayed,
               (SELECT up_to_sequence FROM snap) IS NOT NULL                        AS FromSnapshot
        """;

    /// <summary>
    /// Posicao em instante passado, pela data do fato (RN-009, RN-011).
    /// Limite inclusivo. Fechamento do ultimo dia anterior ao dia do instante,
    /// mais os lancamentos do proprio dia ate o instante (ADR-0007, revisao do
    /// card 32). O snapshot nao serve aqui: e ancorado em sequencia, que e
    /// ordem de registro, e com lancamento retroativo ela diverge da ordem do
    /// fato. O fechamento e por data do fato, e a escrita o corrige.
    /// <para>
    /// Um unico comando: fechamento e lancamentos do dia sao lidos no mesmo
    /// instante do banco. A parte do dia e atendida por
    /// ix_entries_account_occurred.
    /// </para>
    /// </summary>
    internal const string SelectBalanceAsOf = """
        WITH bounds AS (
            SELECT (@asOf::timestamptz AT TIME ZONE 'UTC')::date AS day
        ),
        closing AS (
            SELECT d.closing_balance, d.last_sequence
              FROM ledger.daily_balances d, bounds b
             WHERE d.account_id = @accountId
               AND d.day < b.day
             ORDER BY d.day DESC
             LIMIT 1
        ),
        intraday AS (
            SELECT COALESCE(SUM(e.direction * e.amount), 0) AS amount,
                   COUNT(*)                                 AS entries,
                   COALESCE(MAX(e.sequence), 0)             AS last_sequence
              FROM ledger.ledger_entries e, bounds b
             WHERE e.account_id = @accountId
               AND e.occurred_at >= b.day::timestamp AT TIME ZONE 'UTC'
               AND e.occurred_at <= @asOf
        )
        SELECT COALESCE((SELECT closing_balance FROM closing), 0) + i.amount        AS Balance,
               i.entries                                                             AS EntriesReplayed,
               GREATEST(COALESCE((SELECT last_sequence FROM closing), 0), i.last_sequence) AS LastSequence,
               EXISTS (SELECT 1 FROM closing)                                        AS FromDailyBalance
          FROM intraday i
        """;

    /// <summary>
    /// Fechamento do dia do fato do lancamento recem-gravado (ADR-0007, card 32).
    /// Dia sem linha nasce do fechamento do dia anterior mais o lancamento; dia
    /// com linha soma o lancamento. O lancamento novo tem a maior sequencia da
    /// conta, entao e o last_sequence de todo dia a partir do seu.
    /// </summary>
    internal const string UpsertDailyBalance = """
        WITH bounds AS (
            SELECT (@occurredAt::timestamptz AT TIME ZONE 'UTC')::date AS day
        )
        INSERT INTO ledger.daily_balances (account_id, day, closing_balance, last_sequence)
        SELECT @accountId,
               b.day,
               COALESCE((SELECT d.closing_balance
                           FROM ledger.daily_balances d
                          WHERE d.account_id = @accountId AND d.day < b.day
                          ORDER BY d.day DESC
                          LIMIT 1), 0) + @signedAmount,
               @sequence
          FROM bounds b
        ON CONFLICT (account_id, day) DO UPDATE
           SET closing_balance = ledger.daily_balances.closing_balance + @signedAmount,
               last_sequence   = @sequence
        """;

    /// <summary>
    /// Lancamento retroativo: os fechamentos dos dias seguintes ao dia do fato
    /// passam a incluir o valor. Na mesma transacao e sob o bloqueio da conta,
    /// entao nenhuma consulta ve fechamento desatualizado.
    /// </summary>
    internal const string ShiftLaterDailyBalances = """
        UPDATE ledger.daily_balances
           SET closing_balance = closing_balance + @signedAmount,
               last_sequence   = @sequence
         WHERE account_id = @accountId
           AND day > (@occurredAt::timestamptz AT TIME ZONE 'UTC')::date
        """;

    internal const string InsertEntry = """
        INSERT INTO ledger.ledger_entries
            (entry_id, account_id, sequence, direction, amount, currency,
             occurred_at, recorded_at, idempotency_key, correlation_id,
             reversal_of, balance_after)
        VALUES
            (@entryId, @accountId, @sequence, @direction, @amount, @currency,
             @occurredAt, @recordedAt, @idempotencyKey, @correlationId,
             @reversalOf, @balanceAfter)
        """;

    internal const string AdvanceSequence = """
        UPDATE ledger.accounts
           SET last_sequence = @sequence
         WHERE account_id = @accountId
        """;

    /// <summary>
    /// Registro de idempotencia, na MESMA transacao do lancamento.
    /// A repeticao e reconhecida antes, por SelectForReplay sob o bloqueio da
    /// conta, onde nao ha janela de corrida (revisao do ADR-0006, L-10). A
    /// violacao da chave primaria aqui continua como segunda barreira: consulta
    /// previa FORA do bloqueio abriria a janela entre o SELECT e o INSERT, e e
    /// nessa janela que as requisicoes simultaneas chegam.
    /// </summary>
    internal const string InsertIdempotency = """
        INSERT INTO ledger.idempotency_records
            (account_id, idempotency_key, request_hash, response_status,
             response_body, entry_id)
        VALUES
            (@accountId, @idempotencyKey, @requestHash, @responseStatus,
             @responseBody::json, @entryId)
        """;

    /// <summary>Evento de integracao, na mesma transacao (ADR-0008).</summary>
    internal const string InsertOutbox = """
        INSERT INTO ledger.outbox_messages
            (message_id, account_id, sequence, event_type, payload, occurred_at)
        VALUES
            (@messageId, @accountId, @sequence, @eventType, @payload::jsonb, @occurredAt)
        """;

    /// <summary>Snapshot amortizado, gravado a cada N lancamentos (ADR-0007).</summary>
    internal const string InsertSnapshot = """
        INSERT INTO ledger.balance_snapshots
            (account_id, up_to_sequence, balance, as_of)
        VALUES
            (@accountId, @sequence, @balance, @asOf)
        ON CONFLICT (account_id, up_to_sequence) DO NOTHING
        """;

    /// <summary>
    /// Lancamento original de um estorno. Lido pela chave primaria, sem filtro
    /// de conta: a titularidade e decidida pelo agregado (RN-004), e filtrar
    /// aqui esconderia essa regra dentro do SQL.
    ///
    /// AlreadyReversed so ordena a rejeicao; quem impede o segundo estorno e
    /// uq_entries_reversal. Lido sob o bloqueio da conta, e o estorno de um
    /// lancamento so pode ser gravado por quem detem esse mesmo bloqueio, entao
    /// o valor nao muda entre esta leitura e a gravacao. O indice da constraint
    /// atende o EXISTS.
    /// </summary>
    internal const string SelectEntry = """
        SELECT e.entry_id        AS EntryId,
               e.account_id      AS AccountId,
               e.sequence        AS Sequence,
               e.direction       AS Direction,
               e.amount          AS Amount,
               e.currency        AS Currency,
               e.occurred_at     AS OccurredAt,
               e.idempotency_key AS IdempotencyKey,
               e.correlation_id  AS CorrelationId,
               e.reversal_of     AS ReversalOf,
               e.balance_after   AS BalanceAfter,
               EXISTS (SELECT 1
                         FROM ledger.ledger_entries r
                        WHERE r.reversal_of = e.entry_id) AS AlreadyReversed
          FROM ledger.ledger_entries e
         WHERE e.entry_id = @entryId
        """;

    /// <summary>Moeda da conta, que tambem serve de verificacao de existencia.</summary>
    internal const string SelectAccountCurrency = """
        SELECT currency
          FROM ledger.accounts
         WHERE account_id = @accountId
        """;

    /// <summary>
    /// Pagina do extrato. O cursor e a sequencia: estavel e sem lacunas
    /// (RN-006), entao nao repete nem omite linha entre paginas. Atendida pelo
    /// indice de <c>uq_entries_sequence</c>, em ordem, sem ordenacao adicional.
    /// Os casts explicitos permitem filtro opcional sem que o PostgreSQL precise
    /// inferir o tipo de um parametro nulo.
    /// </summary>
    internal const string SelectStatementPage = """
        SELECT entry_id      AS EntryId,
               sequence      AS Sequence,
               direction     AS Direction,
               amount        AS Amount,
               occurred_at   AS OccurredAt,
               recorded_at   AS RecordedAt,
               balance_after AS BalanceAfter,
               reversal_of   AS ReversalOf
          FROM ledger.ledger_entries
         WHERE account_id = @accountId
           AND sequence > @afterSequence
           AND (@from::timestamptz IS NULL OR occurred_at >= @from::timestamptz)
           AND (@to::timestamptz   IS NULL OR occurred_at <= @to::timestamptz)
         ORDER BY sequence
         LIMIT @take
        """;

    /// <summary>
    /// Impressao e resposta originais, para a repeticao idempotente. A resposta
    /// devolvida e response_body, o texto gravado; as colunas do lancamento
    /// servem ao resultado interno, nao ao corpo HTTP (ADR-0006, card 19.5).
    /// </summary>
    internal const string SelectForReplay = """
        SELECT i.request_hash  AS RequestHash,
               i.response_body::text AS ResponseBody,
               e.entry_id      AS EntryId,
               e.account_id    AS AccountId,
               e.sequence      AS Sequence,
               e.direction     AS Direction,
               e.amount        AS Amount,
               e.currency      AS Currency,
               e.occurred_at   AS OccurredAt,
               e.recorded_at   AS RecordedAt,
               e.balance_after AS BalanceAfter
          FROM ledger.idempotency_records i
          JOIN ledger.ledger_entries e ON e.entry_id = i.entry_id
         WHERE i.account_id = @accountId
           AND i.idempotency_key = @idempotencyKey
        """;
}
