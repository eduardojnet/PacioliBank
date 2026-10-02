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
///   <item>reconhece a repeticao de comando ja efetivado (L-10);</item>
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

    /// <summary>
    /// Payload de evento: campo nulo e omitido, para que <c>reversalOf</c> so
    /// apareca no estorno, como o contrato da EF secao 9 define.
    /// </summary>
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly NpgsqlDataSource _dataSource;

    public PostgresLedgerStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    /// <inheritdoc />
    public Task<PostEntryResult> PostAsync(
        Guid accountId,
        PostingRequest request,
        ReadOnlyMemory<byte> requestHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return WithRetryAsync(
            () => PostOnceAsync(accountId, request, requestHash, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<PostEntryResult> ReverseAsync(
        Guid accountId,
        Guid entryId,
        ReversalRequest request,
        ReadOnlyMemory<byte> requestHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return WithRetryAsync(
            () => ReverseOnceAsync(accountId, entryId, request, requestHash, cancellationToken),
            cancellationToken);
    }

    private static async Task<PostEntryResult> WithRetryAsync(
        Func<Task<PostEntryResult>> operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await operation().ConfigureAwait(false);
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

        var account = await LockAndLoadAsync(connection, transaction, accountId, cancellationToken).ConfigureAwait(false);

        var earlier = await ReplayUnderLockAsync(
            connection, transaction, accountId, request.IdempotencyKey, requestHash, cancellationToken).ConfigureAwait(false);

        if (earlier is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return earlier;
        }

        // O dominio decide. Qualquer rejeicao sai daqui como excecao, antes de
        // qualquer gravacao e antes de a sequencia ser consumida (RN-006).
        var entry = account.Post(request);

        var recordedAt = ToStoredPrecision(DateTimeOffset.UtcNow);

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
            return await ReplayAsync(accountId, request.IdempotencyKey, requestHash, cancellationToken).ConfigureAwait(false);
        }

        return ToResult(entry, recordedAt, replayed: false);
    }

    private async Task<PostEntryResult> ReverseOnceAsync(
        Guid accountId,
        Guid entryId,
        ReversalRequest request,
        ReadOnlyMemory<byte> requestHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        // Mesmo bloqueio do lancamento comum: o estorno altera a posicao e
        // precisa da mesma serializacao por conta (ADR-0005).
        var account = await LockAndLoadAsync(connection, transaction, accountId, cancellationToken).ConfigureAwait(false);

        // A repeticao tem precedencia sobre qualquer rejeicao, inclusive sobre
        // "ja estornado": o reenvio do proprio estorno encontra o original
        // estornado, e precisa receber o resultado original (ADR-0006).
        var earlier = await ReplayUnderLockAsync(
            connection, transaction, accountId, request.IdempotencyKey, requestHash, cancellationToken).ConfigureAwait(false);

        if (earlier is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return earlier;
        }

        // O original e imutavel (RN-003), entao le-lo sem bloqueio proprio e seguro.
        var row = await connection.QuerySingleOrDefaultAsync<EntryRow>(new CommandDefinition(
            LedgerSql.SelectEntry, new { entryId }, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (row is null)
        {
            throw new EntryNotFoundException(entryId);
        }

        // Titularidade, estorno de estorno, duplicidade e saldo sao decididos
        // pelo agregado, nessa ordem, e nao por comparacao de colunas aqui
        // (RN-001, RN-004).
        var entry = account.Reverse(
            ToEntry(row), request.OccurredAt, request.IdempotencyKey, request.CorrelationId, row.AlreadyReversed);

        var recordedAt = ToStoredPrecision(DateTimeOffset.UtcNow);

        try
        {
            await WriteAsync(connection, transaction, entry, requestHash, recordedAt, cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (IsIdempotencyViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return await ReplayAsync(accountId, request.IdempotencyKey, requestHash, cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (IsReversalViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            // O reenvio do mesmo estorno viola as duas unicidades, a da chave e a
            // do estorno, e qual delas o banco reporta primeiro nao e contratual.
            // A repeticao legitima tem precedencia; sem registro da chave, e um
            // segundo estorno de verdade (RN-004).
            var replayed = await TryReplayAsync(accountId, request.IdempotencyKey, requestHash, cancellationToken)
                .ConfigureAwait(false);

            return replayed ?? throw new EntryAlreadyReversedException(entryId);
        }

        return ToResult(entry, recordedAt, replayed: false);
    }

    /// <summary>
    /// Reconhece a repeticao de um comando ja efetivado, ANTES de o agregado
    /// decidir (lacuna L-10, revisao do ADR-0006).
    /// </summary>
    /// <remarks>
    /// Sem este passo, o agregado julgaria o reenvio pelo estado ATUAL da conta:
    /// um debito ja efetivado seria recusado por saldo se o saldo tivesse caido
    /// depois, e o chamador receberia rejeicao para um pagamento consumado.
    /// <para>
    /// A consulta previa que o ADR-0006 proibe e a feita FORA de serializacao,
    /// com janela entre o SELECT e o INSERT. Esta e feita sob o bloqueio da
    /// conta: o registro de uma chave so e gravado por quem detem esse mesmo
    /// bloqueio, e em READ COMMITTED a leitura apos adquiri-lo enxerga tudo o
    /// que o detentor anterior confirmou. Nao ha janela. A violacao de chave
    /// primaria continua como segunda barreira, para qualquer caminho futuro
    /// que grave sem o bloqueio.
    /// </para>
    /// </remarks>
    private static Task<PostEntryResult?> ReplayUnderLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid accountId,
        string idempotencyKey,
        ReadOnlyMemory<byte> requestHash,
        CancellationToken cancellationToken) =>
        ReadReplayAsync(connection, transaction, accountId, idempotencyKey, requestHash, cancellationToken);

    /// <summary>
    /// Bloqueia a linha da conta e, so entao, carrega a posicao e reidrata o
    /// agregado. A ordem e a decisao do ADR-0005: inverte-la reabre a corrida.
    /// </summary>
    private static async Task<Account> LockAndLoadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid accountId,
        CancellationToken cancellationToken)
    {
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

        return Account.Rehydrate(
            control.AccountId,
            control.CustomerId,
            currency,
            (AccountStatus)control.Status,
            control.LastSequence,
            Money.Of(balanceRow.Balance, currency));
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
            eventType = LedgerEntryEvent.TypeOf(entry),
            payload = JsonSerializer.Serialize(LedgerEntryEvent.From(entry, recordedAt), EventJsonOptions),
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
        string idempotencyKey,
        ReadOnlyMemory<byte> requestHash,
        CancellationToken cancellationToken)
    {
        var replayed = await TryReplayAsync(accountId, idempotencyKey, requestHash, cancellationToken)
            .ConfigureAwait(false);

        // A chave colidiu mas o registro nao esta visivel: trata-se de outra
        // transacao ainda em curso. Repetivel com seguranca.
        return replayed ?? throw new LedgerUnavailableException();
    }

    /// <summary>
    /// Devolve o resultado original da chave, ou nulo quando a chave nao tem
    /// registro visivel. Impressao divergente e reuso indevido (ADR-0006).
    /// </summary>
    private async Task<PostEntryResult?> TryReplayAsync(
        Guid accountId,
        string idempotencyKey,
        ReadOnlyMemory<byte> requestHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        return await ReadReplayAsync(connection, transaction: null, accountId, idempotencyKey, requestHash, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<PostEntryResult?> ReadReplayAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid accountId,
        string idempotencyKey,
        ReadOnlyMemory<byte> requestHash,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ReplayRow>(new CommandDefinition(
            LedgerSql.SelectForReplay,
            new { accountId, idempotencyKey },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        if (!requestHash.Span.SequenceEqual(row.RequestHash))
        {
            throw new IdempotencyConflictException(accountId, idempotencyKey);
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

    /// <inheritdoc />
    public async Task<StatementPage> GetStatementAsync(
        Guid accountId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var code = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            LedgerSql.SelectAccountCurrency, new { accountId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (code is null)
        {
            throw new AccountNotFoundException(accountId);
        }

        var currency = Currency.FromCode(code);

        // Uma linha a mais que o limite revela se ha proxima pagina, sem COUNT.
        var rows = (await connection.QueryAsync<StatementRow>(new CommandDefinition(
            LedgerSql.SelectStatementPage,
            new
            {
                accountId,
                afterSequence,
                from = from?.UtcDateTime,
                to = to?.UtcDateTime,
                take = limit + 1,
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();

        var hasMore = rows.Count > limit;
        var page = rows.Take(limit)
            .Select(row => new StatementEntry(
                row.EntryId,
                row.Sequence,
                (EntryDirection)row.Direction,
                Money.Of(row.Amount, currency),
                new DateTimeOffset(row.OccurredAt, TimeSpan.Zero),
                new DateTimeOffset(row.RecordedAt, TimeSpan.Zero),
                Money.Of(row.BalanceAfter, currency),
                row.ReversalOf))
            .ToList();

        return new StatementPage(accountId, page, hasMore ? page[^1].Sequence : null);
    }

    private static PostEntryResult ToResult(LedgerEntry entry, DateTimeOffset recordedAt, bool replayed) =>
        new(entry.EntryId,
            entry.AccountId,
            entry.Sequence,
            entry.Direction,
            entry.Amount,
            ToStoredPrecision(entry.OccurredAt),
            recordedAt,
            entry.BalanceAfter,
            replayed);

    /// <summary>
    /// Trunca para microssegundos, a precisao do <c>timestamptz</c>.
    /// </summary>
    /// <remarks>
    /// O .NET guarda ticks de 100 ns. Sem truncar, a primeira resposta levaria
    /// o instante em memoria e a repeticao, lida do banco, o instante gravado:
    /// corpos diferentes para o mesmo comando, contra a EF secao 8.4. O Npgsql
    /// tambem trunca na escrita, entao o valor devolvido e o gravado coincidem.
    /// </remarks>
    private static DateTimeOffset ToStoredPrecision(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % 10), value.Offset);

    private static LedgerEntry ToEntry(EntryRow row)
    {
        var currency = Currency.FromCode(row.Currency);

        return LedgerEntry.Rehydrate(
            row.EntryId,
            row.AccountId,
            row.Sequence,
            (EntryDirection)row.Direction,
            Money.Of(row.Amount, currency),
            new DateTimeOffset(row.OccurredAt, TimeSpan.Zero),
            row.IdempotencyKey,
            row.CorrelationId,
            row.ReversalOf,
            Money.Of(row.BalanceAfter, currency));
    }

    private static bool IsReversalViolation(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.UniqueViolation
        && exception.ConstraintName == "uq_entries_reversal";

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

    private sealed class StatementRow
    {
        public Guid EntryId { get; set; }

        public long Sequence { get; set; }

        public short Direction { get; set; }

        public decimal Amount { get; set; }

        public DateTime OccurredAt { get; set; }

        public DateTime RecordedAt { get; set; }

        public decimal BalanceAfter { get; set; }

        public Guid? ReversalOf { get; set; }
    }

    private sealed class EntryRow
    {
        public Guid EntryId { get; set; }

        public Guid AccountId { get; set; }

        public long Sequence { get; set; }

        public short Direction { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; } = string.Empty;

        public DateTime OccurredAt { get; set; }

        public string IdempotencyKey { get; set; } = string.Empty;

        public Guid CorrelationId { get; set; }

        public Guid? ReversalOf { get; set; }

        public decimal BalanceAfter { get; set; }

        public bool AlreadyReversed { get; set; }
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
