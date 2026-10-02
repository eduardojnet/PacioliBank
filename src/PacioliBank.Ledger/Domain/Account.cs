namespace PacioliBank.Ledger.Domain;

/// <summary>
/// Agregado raiz. Unidade de consistencia transacional e de serializacao de
/// concorrencia do sistema inteiro (EF secao 4.2, ADR-0005).
/// </summary>
/// <remarks>
/// O agregado NAO carrega os lancamentos da conta. Carregar um historico de
/// centenas de milhares de registros para validar um debito seria inviavel e
/// reintroduziria a degradacao do sistema legado.
/// <para>
/// Em vez disso, a instancia e reidratada dentro da transacao de escrita, ja
/// sob bloqueio da linha da conta, com a posicao corrente calculada por
/// snapshot mais delta (ADR-0007). O agregado decide sobre o estado de
/// controle, que e o unico de que precisa: moeda, situacao, ultima sequencia
/// e posicao corrente.
/// </para>
/// </remarks>
public sealed class Account
{
    private Account(
        Guid accountId,
        Guid customerId,
        Currency currency,
        AccountStatus status,
        long lastSequence,
        Money currentBalance)
    {
        AccountId = accountId;
        CustomerId = customerId;
        Currency = currency;
        Status = status;
        LastSequence = lastSequence;
        CurrentBalance = currentBalance;
    }

    public Guid AccountId { get; }

    /// <summary>Titular. Base do controle de acesso (RF-009).</summary>
    public Guid CustomerId { get; }

    /// <summary>Imutavel apos a criacao (RN-007).</summary>
    public Currency Currency { get; }

    public AccountStatus Status { get; }

    /// <summary>Ultima sequencia atribuida. Zero quando a conta nao tem lancamentos.</summary>
    public long LastSequence { get; private set; }

    /// <summary>
    /// Posicao corrente, calculada antes da reidratacao e mantida em memoria
    /// durante a transacao. Nunca e a fonte da verdade: o ledger e (RN-010).
    /// </summary>
    public Money CurrentBalance { get; private set; }

    /// <summary>
    /// Reconstroi o agregado a partir do estado de controle lido sob bloqueio.
    /// </summary>
    /// <exception cref="CurrencyMismatchException">
    /// Quando a posicao informada nao esta na moeda da conta.
    /// </exception>
    public static Account Rehydrate(
        Guid accountId,
        Guid customerId,
        Currency currency,
        AccountStatus status,
        long lastSequence,
        Money currentBalance)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Identificador de conta invalido.", nameof(accountId));
        }

        if (lastSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lastSequence), lastSequence, "A sequencia nao pode ser negativa.");
        }

        if (currentBalance.Currency != currency)
        {
            throw new CurrencyMismatchException(currency.Code ?? string.Empty, currentBalance.Currency.Code ?? string.Empty);
        }

        return new Account(accountId, customerId, currency, status, lastSequence, currentBalance);
    }

    /// <summary>
    /// Valida as invariantes e produz o proximo lancamento da conta.
    /// </summary>
    /// <remarks>
    /// Toda rejeicao ocorre ANTES de a sequencia ser consumida, de modo que
    /// comando recusado nao abre lacuna na serie (RN-006).
    /// </remarks>
    /// <exception cref="AccountInactiveException">A conta nao aceita lancamentos (RN-008).</exception>
    /// <exception cref="CurrencyMismatchException">Moeda divergente da conta (RN-007).</exception>
    /// <exception cref="InvalidEntryAmountException">Valor nao positivo (RN-002).</exception>
    /// <exception cref="MissingIdempotencyKeyException">Chave de idempotencia ausente (RN-005).</exception>
    /// <exception cref="InsufficientFundsException">A posicao resultante seria negativa (RN-001).</exception>
    public LedgerEntry Post(PostingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        EnsureAcceptsPostings();
        EnsureIdempotencyKey(request.IdempotencyKey);
        EnsureOccurredAt(request.OccurredAt);
        EnsureAmount(request.Amount);

        var signed = request.Direction == EntryDirection.Credit
            ? request.Amount
            : Money.Zero(Currency) - request.Amount;

        var resulting = CurrentBalance + signed;

        // RN-001: a invariante vale para qualquer sentido, inclusive para o
        // estorno de um credito que ja foi gasto (QA-003 decidido por rejeitar).
        if (resulting.IsNegative)
        {
            throw new InsufficientFundsException(CurrentBalance.Amount, request.Amount.Amount);
        }

        var entry = new LedgerEntry(
            entryId: Guid.NewGuid(),
            accountId: AccountId,
            sequence: LastSequence + 1,
            direction: request.Direction,
            amount: request.Amount,
            occurredAt: request.OccurredAt,
            idempotencyKey: request.IdempotencyKey,
            correlationId: request.CorrelationId,
            reversalOf: request.ReversalOf,
            balanceAfter: resulting);

        LastSequence = entry.Sequence;
        CurrentBalance = resulting;

        return entry;
    }

    /// <summary>
    /// Produz o lancamento compensatorio de um lancamento desta conta (RN-004).
    /// </summary>
    /// <remarks>
    /// A unicidade do estorno NAO e verificada aqui. O dominio nao conhece os
    /// demais lancamentos da conta, e uma verificacao em memoria abriria janela
    /// de corrida entre dois pedidos simultaneos. A garantia e estrutural, pela
    /// constraint <c>uq_entries_reversal</c> (ADR-0003 e ADR-0006): o segundo
    /// estorno e recusado pelo banco, nao pela esperanca de que o codigo olhe
    /// antes.
    /// </remarks>
    /// <exception cref="EntryNotFromThisAccountException">O lancamento pertence a outra conta.</exception>
    /// <exception cref="CannotReverseReversalException">O lancamento ja e um estorno (RN-004).</exception>
    public LedgerEntry Reverse(
        LedgerEntry original,
        DateTimeOffset occurredAt,
        string idempotencyKey,
        Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(original);

        if (original.AccountId != AccountId)
        {
            throw new EntryNotFromThisAccountException(original.EntryId, AccountId);
        }

        if (original.IsReversal)
        {
            throw new CannotReverseReversalException(original.EntryId);
        }

        var opposite = original.Direction == EntryDirection.Credit
            ? EntryDirection.Debit
            : EntryDirection.Credit;

        return Post(new PostingRequest(
            Direction: opposite,
            Amount: original.Amount,
            OccurredAt: occurredAt,
            IdempotencyKey: idempotencyKey,
            CorrelationId: correlationId,
            ReversalOf: original.EntryId));
    }

    private void EnsureAcceptsPostings()
    {
        if (Status != AccountStatus.Active)
        {
            throw new AccountInactiveException(AccountId, Status.ToString());
        }
    }

    private void EnsureAmount(Money amount)
    {
        if (amount.Currency != Currency)
        {
            throw new CurrencyMismatchException(Currency.Code ?? string.Empty, amount.Currency.Code ?? string.Empty);
        }

        if (!amount.IsPositive)
        {
            throw new InvalidEntryAmountException(amount.Amount);
        }
    }

    private static void EnsureIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new MissingIdempotencyKeyException();
        }
    }

    private static void EnsureOccurredAt(DateTimeOffset occurredAt)
    {
        if (occurredAt == default)
        {
            throw new ArgumentException("O instante do fato e obrigatorio.", nameof(occurredAt));
        }
    }
}
