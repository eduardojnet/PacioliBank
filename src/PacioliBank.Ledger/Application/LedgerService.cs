using PacioliBank.Ledger.Domain;

namespace PacioliBank.Ledger.Application;

/// <summary>
/// Implementacao da porta de entrada. Valida o que nao depende de estado,
/// antes de qualquer I/O, e delega ao <see cref="ILedgerStore"/> o que depende.
/// </summary>
public sealed class LedgerService : ILedgerService
{
    /// <summary>Pagina padrao do extrato, quando o chamador nao informa limite.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>
    /// Maior pagina admitida. A EF nao fixa o valor: conduta provisoria,
    /// registrada como lacuna no ESTADO.md (RF-005, RNF-012).
    /// </summary>
    public const int MaxPageSize = 200;

    private readonly ILedgerStore _store;
    private readonly TimeProvider _clock;

    public LedgerService(ILedgerStore store, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<PostEntryResult> PostAsync(PostingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var key = RequireIdempotencyKey(command.IdempotencyKey);
        var amount = ParseAmount(command.Amount, ResolveCurrency(command.Currency));

        var request = new PostingRequest(
            command.Direction,
            amount,
            command.OccurredAt,
            key,
            command.CorrelationId);

        return _store.PostAsync(
            command.AccountId,
            request,
            RequestFingerprint.Of(command.AccountId, request),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<PostEntryResult> ReverseAsync(ReversalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = new ReversalRequest(
            command.OccurredAt,
            RequireIdempotencyKey(command.IdempotencyKey),
            command.CorrelationId);

        return _store.ReverseAsync(
            command.AccountId,
            command.EntryId,
            request,
            RequestFingerprint.OfReversal(command.AccountId, command.EntryId, request),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<BalanceResult> GetBalanceAsync(Guid accountId, DateTimeOffset? asOf, CancellationToken cancellationToken)
    {
        // RF-004: posicao em instante futuro nao e consulta, e previsao.
        if (asOf > _clock.GetUtcNow())
        {
            throw new InvalidPointInTimeException("O instante de consulta nao pode estar no futuro.");
        }

        return _store.GetBalanceAsync(accountId, asOf, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StatementPage> GetStatementAsync(StatementQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = query.Limit ?? DefaultPageSize;

        if (limit is < 1 or > MaxPageSize)
        {
            throw new PageSizeExceededException(limit, MaxPageSize);
        }

        if (query.From > query.To)
        {
            throw new InvalidPointInTimeException("O inicio do periodo e posterior ao fim.");
        }

        return _store.GetStatementAsync(
            query.AccountId,
            query.From,
            query.To,
            query.AfterSequence ?? 0,
            limit,
            cancellationToken);
    }

    private static string RequireIdempotencyKey(string? key) =>
        string.IsNullOrWhiteSpace(key) ? throw new MissingIdempotencyKeyException() : key;

    private static Currency ResolveCurrency(string code) =>
        Currency.TryFromCode(code, out var currency)
            ? currency
            : throw new UnsupportedCurrencyException(code ?? string.Empty);

    private static Money ParseAmount(string value, Currency currency)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidEntryAmountException("O valor do lancamento e obrigatorio.");
        }

        try
        {
            return Money.Parse(value, currency);
        }
        catch (FormatException ex)
        {
            throw new InvalidEntryAmountException($"Valor em formato invalido: '{value}'.", ex);
        }
        catch (OverflowException ex)
        {
            throw new InvalidEntryAmountException($"Valor fora da faixa suportada: '{value}'.", ex);
        }
    }
}
