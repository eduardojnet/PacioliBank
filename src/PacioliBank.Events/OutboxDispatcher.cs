using System.Data;
using Dapper;
using Npgsql;

namespace PacioliBank.Events;

/// <summary>
/// Le a outbox e publica as mensagens pendentes (ADR-0008).
/// </summary>
/// <remarks>
/// Uma passada e uma transacao: reserva um lote com <c>FOR UPDATE SKIP LOCKED</c>,
/// publica cada mensagem e marca o resultado antes do <c>COMMIT</c>.
/// <list type="bullet">
///   <item><c>SKIP LOCKED</c> deixa varios despachantes consumirem em paralelo
///   sem coordenacao: cada um ignora as linhas que outro ja reservou.</item>
///   <item>A confirmacao no barramento e a marcacao no banco nao sao atomicas.
///   Falha entre as duas republica a mensagem com o mesmo <c>message_id</c>:
///   entrega ao menos uma vez, por decisao, nao exatamente uma vez.</item>
///   <item>Falha de publicacao nao derruba o lote: a mensagem recebe
///   <c>attempts + 1</c> e volta para a fila com recuo exponencial.</item>
/// </list>
/// </remarks>
public sealed class OutboxDispatcher
{
    /// <summary>
    /// Ordenado por conta e sequencia, e nao por <c>occurred_at</c> como no
    /// exemplo do ADR-0008: com lancamento retroativo, a ordem do fato difere
    /// da ordem de registro, e a garantia oferecida ao consumidor e a ordem por
    /// sequencia dentro da conta (EF secao 9). Dentro de um lote, publicar na
    /// ordem prometida custa nada; entre despachantes paralelos ela nao e
    /// garantida, e o consumidor ordena pelo campo <c>sequence</c>.
    /// </summary>
    private const string SelectPending = """
        SELECT message_id   AS MessageId,
               account_id   AS AccountId,
               sequence     AS Sequence,
               event_type   AS EventType,
               payload::text AS Payload,
               occurred_at  AS OccurredAt,
               attempts::int AS Attempts
          FROM ledger.outbox_messages
         WHERE published_at IS NULL
           AND next_attempt_at <= now()
           AND attempts < @maxAttempts
         ORDER BY account_id, sequence
         LIMIT @batchSize
           FOR UPDATE SKIP LOCKED
        """;

    private const string MarkPublished = """
        UPDATE ledger.outbox_messages
           SET published_at = now()
         WHERE message_id = ANY(@ids)
        """;

    /// <summary>
    /// Recuo exponencial de 2^tentativas segundos, com teto. <c>attempts</c> na
    /// expressao e o valor anterior ao incremento.
    /// </summary>
    private const string MarkFailed = """
        UPDATE ledger.outbox_messages
           SET attempts = attempts + 1,
               next_attempt_at = now() + make_interval(secs => LEAST(power(2, attempts), @maxBackoffSeconds))
         WHERE message_id = ANY(@ids)
        """;

    private readonly NpgsqlDataSource _dataSource;
    private readonly IEventPublisher _publisher;
    private readonly OutboxDispatcherOptions _options;

    public OutboxDispatcher(NpgsqlDataSource dataSource, IEventPublisher publisher, OutboxDispatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(options);

        _dataSource = dataSource;
        _publisher = publisher;
        _options = options;
    }

    /// <summary>Executa uma passada: um lote, uma transacao.</summary>
    public async Task<DispatchResult> DispatchOnceAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        var pending = (await connection.QueryAsync<PendingRow>(new CommandDefinition(
            SelectPending,
            new { maxAttempts = _options.MaxAttempts, batchSize = _options.BatchSize },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();

        var published = new List<Guid>();
        var failed = new List<Guid>();
        var parked = new List<Guid>();

        foreach (var row in pending)
        {
            var message = new OutboxMessage(
                row.MessageId,
                row.AccountId,
                row.Sequence,
                row.EventType,
                row.Payload,
                new DateTimeOffset(row.OccurredAt, TimeSpan.Zero),
                row.Attempts);

            try
            {
                await _publisher.PublishAsync(message, cancellationToken).ConfigureAwait(false);
                published.Add(row.MessageId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Desligamento: nada e marcado, a transacao e desfeita e o lote
                // volta inteiro para a fila. O que ja foi ao barramento sera
                // republicado, com o mesmo message_id.
                throw;
            }
#pragma warning disable CA1031 // Falha de qualquer tipo no publicador vira nova tentativa, nunca derruba o lote.
            catch (Exception)
#pragma warning restore CA1031
            {
                failed.Add(row.MessageId);

                if (row.Attempts + 1 >= _options.MaxAttempts)
                {
                    parked.Add(row.MessageId);
                }
            }
        }

        if (published.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                MarkPublished, new { ids = published.ToArray() }, transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        if (failed.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                MarkFailed,
                new { ids = failed.ToArray(), maxBackoffSeconds = (double)_options.MaxBackoffSeconds },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new DispatchResult(published, failed, parked);
    }

    private sealed class PendingRow
    {
        public Guid MessageId { get; set; }

        public Guid AccountId { get; set; }

        public long Sequence { get; set; }

        public string EventType { get; set; } = string.Empty;

        public string Payload { get; set; } = string.Empty;

        public DateTime OccurredAt { get; set; }

        public int Attempts { get; set; }
    }
}
