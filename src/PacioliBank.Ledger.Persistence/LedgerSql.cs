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
    /// Limite inclusivo. Nao usa snapshot: o snapshot e ancorado em sequencia,
    /// que e ordem de registro, enquanto esta consulta usa ordem do fato, e com
    /// lancamento retroativo as duas divergem (ADR-0007, limitacao declarada).
    /// Atendida pelo indice ix_entries_account_occurred, que cobre as colunas
    /// do calculo.
    /// </summary>
    internal const string SelectBalanceAsOf = """
        SELECT COALESCE(SUM(direction * amount), 0) AS Balance,
               COUNT(*)                             AS EntriesReplayed,
               COALESCE(MAX(sequence), 0)           AS LastSequence
          FROM ledger.ledger_entries
         WHERE account_id = @accountId
           AND occurred_at <= @asOf
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
    /// A violacao da chave primaria aqui e o mecanismo de deteccao, nao um caso
    /// excepcional: consulta previa abriria a janela de corrida entre o SELECT e
    /// o INSERT, e e exatamente nessa janela que as requisicoes simultaneas
    /// chegam (ADR-0006).
    /// </summary>
    internal const string InsertIdempotency = """
        INSERT INTO ledger.idempotency_records
            (account_id, idempotency_key, request_hash, response_status,
             response_body, entry_id)
        VALUES
            (@accountId, @idempotencyKey, @requestHash, @responseStatus,
             @responseBody::jsonb, @entryId)
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

    /// <summary>Lancamento e impressao originais, para a repeticao idempotente.</summary>
    internal const string SelectForReplay = """
        SELECT i.request_hash  AS RequestHash,
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
