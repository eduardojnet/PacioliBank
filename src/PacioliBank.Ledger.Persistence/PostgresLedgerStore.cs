using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Npgsql;
using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Domain;

namespace PacioliBank.Ledger.Persistence;

/// <summary>
/// Implementacao do ledger sobre PostgreSQL.
/// </summary>
/// <remarks>
/// Esta classe e onde o ADR-0005 deixa de ser texto e vira garantia. A ordem
/// dos passos dentro da transacao e a decisao arquitetural, nao detalhe de
/// implementacao:
/// <list type="number">
///   <item>bloqueia a linha da conta;</item>
///   <item>so entao le a posicao;</item>
///   <item>o agregado decide;</item>
///   <item>lancamento, sequencia, idempotencia e outbox sao gravados juntos.</item>
/// </list>
/// Inverter 1 e 2 reabre a condicao de corrida. Separar o passo 4 em
/// transacoes distintas cria janela de inconsistencia em dado financeiro.
/// </remarks>
public sealed class PostgresLedgerStore : ILedgerStore
{
    /// <summary>Snapshot a cada N lancamentos (ADR-0007). Calibrar por RNF-006.</summary>
    private const int SnapshotEvery = 100;

    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;

    public PostgresLedgerStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    /// <inheritdoc />
    public async Task<PostEntryResult> PostAsync(
        Guid accountId,
        PostingRequest request,
        ReadOnlyMemory<byte> requestHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await PostOnceAsync(accountId, request, requestHash, cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (IsRetryableConflict(ex))
            {
                // RNF-004: o conflito e resolvido por nova tentativa, invisivel ao
                // chamador. So vira erro quando as tentativas se esgotam, e ainda
                // assim como repetivel, porque a chave de idempotencia protege
                // o reenvio (ADR-0006).
                if (attempt == MaxAttempts)
                {
                    throw new LedgerUnavailableException(
                        "Conflito de concorrencia persistente. A nova tentativa com a mesma chave e segura.",
                        ex);
                }

                await Task.Delay(BackoffFor(attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new LedgerUnavailableException();
    }

    private async Task<PostEntryResult> PostOnceAsync(
        Guid accountId,
        PostingRequest request,
        ReadOnlyMemory<byte> requestHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        // Espera limitada pelo bloqueio: melhor rejeitar como repetivel do que
        // esgotar o pool de conexoes sob contencao (ADR-0005).
        await connection.ExecuteAsync(new CommandDefinition(
            "SET LOCAL lock_timeout = '3s'", transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var control = await connection.QuerySingleOrDefaultAsync<AccountControlRow>(new CommandDefinition(
            LedgerSql.LockAccountForWrite, new { accountId }, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (control is null)
        {
            throw new AccountNotFoundException(accountId);
        }

        var balanceRow = await connection.QuerySingleAsync<CurrentBalanceRow>(new CommandDefinition(
            LedgerSql.SelectCurrentBalance, new { accountId }, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var currency = Currency.FromCode(control.Currency);

        var account = Account.Rehydrate(
            control.AccountId,
            control.CustomerId,
            currency,
            (AccountStatus)control.Status,
            control.LastSequence,
            Money.Of(balanceRow.Balance, currency));

        // O dominio decide. Qualquer rejeicao sai daqui como excecao, antes de
        // qualquer gravacao e antes de a sequencia ser consumida (RN-006).
        var entry = account.Post(request);

        var recordedAt = DateTimeOffset.UtcNow;

        try
        {
            await WriteAsync(connection, transaction, entry, requestHash, recordedAt, cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (IsIdempotencyViolation(ex))
        {
            // Caminho esperado, nao excepcional: outra requisicao com a mesma
            // chave venceu a insercao. Devolve-se o resultado original.
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return await ReplayAsync(accountId, request, requestHash, cancellationToken).ConfigureAwait(false);
        }

        return ToResult(entry, recordedAt, replayed: false);
    }

    private static async Task WriteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LedgerEntry entry,
        ReadOnlyMemory<byte> requestHash,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(LedgerSql.InsertEntry, new
        {
            entryId = entry.EntryId,
            accountId = entry.AccountId,
            sequence = entry.Sequence,
            direction = (short)entry.Direction,
            amount = entry.Amount.Amount,
            currency = entry.Amount.Currency.Code,
            occurredAt = entry.OccurredAt.UtcDateTime,
            recordedAt = recordedAt.UtcDateTime,
            idempotencyKey = entry.IdempotencyKey,
            correlationId = entry.CorrelationId,
            reversalOf = entry.ReversalOf,
            balanceAfter = entry.BalanceAfter.Amount,
        }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(LedgerSql.AdvanceSequence, new
        {
            accountId = entry.AccountId,
            sequence = entry.Sequence,
        }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(LedgerSql.InsertIdempotency, new
        {
            accountId = entry.AccountId,
            idempotencyKey = entry.IdempotencyKey,
            requestHash = requestHash.ToArray(),
            responseStatus = (short)201,
            responseBody = JsonSerializer.Serialize(ToResult(entry, recordedAt, replayed: false), JsonOptions),
            entryId = entry.EntryId,
        }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(LedgerSql.InsertOutbox, new
        {
            messageId = Guid.NewGuid(),
            accountId = entry.AccountId,
            sequence = entry.Sequence,
            eventType = entry.IsReversal
                ? "pacioli.ledger.entry-reversed.v1"
                : "pacioli.ledger.entry-recorded.v1",
            payload = JsonSerializer.Serialize(ToResult(entry, recordedAt, replayed: false), JsonOptions),
            occurredAt = entry.OccurredAt.UtcDateTime,
        }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        // Snapshot amortizado: uma em cada N escritas paga uma insercao, usando
        // a posicao que esta transacao ja calculou. Sem processo assincrono e
        // sem leitura adicional (ADR-0007).
        if (entry.Sequence % SnapshotEvery == 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(LedgerSql.InsertSnapshot, new
            {
                accountId = entry.AccountId,
                sequence = entry.Sequence,
                balance = entry.BalanceAfter.Amount,
                asOf = entry.OccurredAt.UtcDateTime,
            }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    private async Task<PostEntryResult> ReplayAsync(
        Guid accountId,
        PostingRequest request,
        ReadOnlyMemory<byte> requestHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var row = await connection.QuerySingleOrDefaultAsync<ReplayRow>(new CommandDefinition(
            LedgerSql.SelectForReplay,
            new { accountId, idempotencyKey = request.IdempotencyKey },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (row is null)
        {
            // A chave colidiu mas o registro nao esta visivel: trata-se de outra
            // transacao ainda em curso. Repetivel com seguranca.
            throw new LedgerUnavailableException();
        }

        if (!requestHash.Span.SequenceEqual(row.RequestHash))
        {
            throw new IdempotencyConflictException(accountId, request.IdempotencyKey);
        }

        var currency = Currency.FromCode(row.Currency);

        return new PostEntryResult(
            row.EntryId,
            row.AccountId,
            row.Sequence,
            (EntryDirection)row.Direction,
            Money.Of(row.Amount, currency),
            new DateTimeOffset(row.OccurredAt, TimeSpan.Zero),
            new DateTimeOffset(row.RecordedAt, TimeSpan.Zero),
            Money.Of(row.BalanceAfter, currency),
            Replayed: true);
    }

    /// <inheritdoc />
    public async Task<BalanceResult> GetBalanceAsync(
        Guid accountId,
        DateTimeOffset? asOf,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var control = await connection.QuerySingleOrDefaultAsync<AccountControlRow>(new CommandDefinition(
            """
            SELECT account_id    AS AccountId,
                   customer_id   AS CustomerId,
                   currency      AS Currency,
                   status        AS Status,
                   last_sequence AS LastSequence
              FROM ledger.accounts
             WHERE account_id = @accountId
            """,
            new { accountId }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (control is null)
        {
            throw new AccountNotFoundException(accountId);
        }

        var currency = Currency.FromCode(control.Currency);

        if (asOf is null)
        {
            var current = await connection.QuerySingleAsync<CurrentBalanceRow>(new CommandDefinition(
                LedgerSql.SelectCurrentBalance, new { accountId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            return new BalanceResult(
                accountId,
                Money.Of(current.Balance, currency),
                DateTimeOffset.UtcNow,
                control.LastSequence,
                current.FromSnapshot ? BalanceSource.Snapshot : BalanceSource.Ledger,
                (int)current.EntriesReplayed);
        }

        var point = await connection.QuerySingleAsync<PointInTimeBalanceRow>(new CommandDefinition(
            LedgerSql.SelectBalanceAsOf,
            new { accountId, asOf = asOf.Value.UtcDateTime },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return new BalanceResult(
            accountId,
            Money.Of(point.Balance, currency),
            asOf.Value,
            point.LastSequence,
            BalanceSource.Ledger,
            (int)point.EntriesReplayed);
    }

    private static PostEntryResult ToResult(LedgerEntry entry, DateTimeOffset recordedAt, bool replayed) =>
        new(entry.EntryId,
            entry.AccountId,
            entry.Sequence,
            entry.Direction,
            entry.Amount,
            entry.OccurredAt,
            recordedAt,
            entry.BalanceAfter,
            replayed);

    private static bool IsIdempotencyViolation(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.UniqueViolation
        && (exception.ConstraintName is "pk_idempotency" or "uq_entries_idempotency");

    private static bool IsRetryableConflict(PostgresException exception) =>
        exception.SqlState is PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.DeadlockDetected
            or PostgresErrorCodes.LockNotAvailable
        || (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == "uq_entries_sequence");

    private static TimeSpan BackoffFor(int attempt) =>
        TimeSpan.FromMilliseconds((20 * Math.Pow(2, attempt - 1)) + RandomNumberGenerator.GetInt32(0, 25));

    private sealed class AccountControlRow
    {
        public Guid AccountId { get; set; }

        public Guid CustomerId { get; set; }

        public string Currency { get; set; } = string.Empty;

        public short Status { get; set; }

        public long LastSequence { get; set; }
    }

    private sealed class CurrentBalanceRow
    {
        public decimal Balance { get; set; }

        public long EntriesReplayed { get; set; }

        public bool FromSnapshot { get; set; }
    }

    private sealed class PointInTimeBalanceRow
    {
        public decimal Balance { get; set; }

        public long EntriesReplayed { get; set; }

        public long LastSequence { get; set; }
    }

    private sealed class ReplayRow
    {
        public byte[] RequestHash { get; set; } = [];

        public Guid EntryId { get; set; }

        public Guid AccountId { get; set; }

        public long Sequence { get; set; }

        public short Direction { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; } = string.Empty;

        public DateTime OccurredAt { get; set; }

        public DateTime RecordedAt { get; set; }

        public decimal BalanceAfter { get; set; }
    }
}
