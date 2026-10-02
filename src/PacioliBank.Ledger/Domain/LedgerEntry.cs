namespace PacioliBank.Ledger.Domain;

/// <summary>
/// Lancamento: fato financeiro consumado e imutavel (RN-003, ADR-0003).
/// Nao existe operacao de alteracao nem de exclusao. Correcao se da por
/// lancamento compensatorio que referencia o original (RN-004).
/// </summary>
/// <remarks>
/// Lancamento novo so e criado por <see cref="Account.Post(PostingRequest)"/>,
/// porque a sequencia e a validacao das invariantes pertencem ao agregado.
/// <see cref="Rehydrate"/> apenas reconstroi o que ja foi gravado.
/// </remarks>
public sealed record LedgerEntry
{
    internal LedgerEntry(
        Guid entryId,
        Guid accountId,
        long sequence,
        EntryDirection direction,
        Money amount,
        DateTimeOffset occurredAt,
        string idempotencyKey,
        Guid correlationId,
        Guid? reversalOf,
        Money balanceAfter)
    {
        EntryId = entryId;
        AccountId = accountId;
        Sequence = sequence;
        Direction = direction;
        Amount = amount;
        OccurredAt = occurredAt;
        IdempotencyKey = idempotencyKey;
        CorrelationId = correlationId;
        ReversalOf = reversalOf;
        BalanceAfter = balanceAfter;
    }

    /// <summary>
    /// Reconstroi um lancamento ja gravado, lido do armazenamento.
    /// </summary>
    /// <remarks>
    /// Nao cria fato novo: a sequencia e o saldo vem do registro persistido.
    /// Existe para que o estorno (RN-004) seja decidido pelo agregado sobre o
    /// lancamento original, e nao por comparacao de colunas no adaptador.
    /// </remarks>
    public static LedgerEntry Rehydrate(
        Guid entryId,
        Guid accountId,
        long sequence,
        EntryDirection direction,
        Money amount,
        DateTimeOffset occurredAt,
        string idempotencyKey,
        Guid correlationId,
        Guid? reversalOf,
        Money balanceAfter)
    {
        if (entryId == Guid.Empty)
        {
            throw new ArgumentException("Identificador de lancamento invalido.", nameof(entryId));
        }

        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "A sequencia de um lancamento gravado comeca em 1.");
        }

        return new LedgerEntry(
            entryId,
            accountId,
            sequence,
            direction,
            amount,
            occurredAt,
            idempotencyKey,
            correlationId,
            reversalOf,
            balanceAfter);
    }

    public Guid EntryId { get; }

    public Guid AccountId { get; }

    /// <summary>Monotonica, sem lacunas, unica por conta (RN-006).</summary>
    public long Sequence { get; }

    public EntryDirection Direction { get; }

    /// <summary>Sempre positivo. O sinal esta em <see cref="Direction"/>.</summary>
    public Money Amount { get; }

    /// <summary>Instante do fato financeiro. Pode ser anterior ao registro (RN-012).</summary>
    public DateTimeOffset OccurredAt { get; }

    public string IdempotencyKey { get; }

    public Guid CorrelationId { get; }

    /// <summary>Preenchido apenas quando este lancamento e um estorno.</summary>
    public Guid? ReversalOf { get; }

    /// <summary>Posicao da conta imediatamente apos este lancamento.</summary>
    public Money BalanceAfter { get; }

    /// <summary>Verdadeiro quando o lancamento compensa outro.</summary>
    public bool IsReversal => ReversalOf.HasValue;

    /// <summary>Valor com sinal aplicado, para acumulacao.</summary>
    public Money SignedAmount =>
        Direction == EntryDirection.Credit
            ? Amount
            : Money.Zero(Amount.Currency) - Amount;
}
