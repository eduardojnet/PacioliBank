namespace PacioliBank.Ledger.Domain;

/// <summary>Raiz das excecoes de invariante do dominio do ledger.</summary>
public class LedgerDomainException : Exception
{
    public LedgerDomainException()
    {
    }

    public LedgerDomainException(string message)
        : base(message)
    {
    }

    public LedgerDomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Moeda fora do catalogo suportado. Ver RN-007.</summary>
public sealed class UnsupportedCurrencyException : LedgerDomainException
{
    public UnsupportedCurrencyException()
        : base("Moeda nao suportada.")
    {
    }

    public UnsupportedCurrencyException(string code)
        : base($"Moeda nao suportada: '{code}'.")
    {
        Code = code;
    }

    public UnsupportedCurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? Code { get; }
}

/// <summary>Valor com mais casas decimais do que a moeda expoe. Ver RN-002.</summary>
public sealed class InvalidMoneyScaleException : LedgerDomainException
{
    public InvalidMoneyScaleException()
        : base("Escala monetaria invalida.")
    {
    }

    public InvalidMoneyScaleException(decimal amount, string currencyCode)
        : base($"O valor {amount} excede a escala da moeda '{currencyCode}'.")
    {
        Amount = amount;
        CurrencyCode = currencyCode;
    }

    public InvalidMoneyScaleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public decimal Amount { get; }

    public string? CurrencyCode { get; }
}

/// <summary>Operacao aritmetica entre moedas distintas. Ver RN-007.</summary>
public sealed class CurrencyMismatchException : LedgerDomainException
{
    public CurrencyMismatchException()
        : base("Operacao entre moedas distintas.")
    {
    }

    public CurrencyMismatchException(string left, string right)
        : base($"Operacao entre moedas distintas: '{left}' e '{right}'.")
    {
        Left = left;
        Right = right;
    }

    public CurrencyMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? Left { get; }

    public string? Right { get; }
}

/// <summary>A conta nao aceita lancamentos na situacao atual. Ver RN-008.</summary>
public sealed class AccountInactiveException : LedgerDomainException
{
    public AccountInactiveException()
        : base("A conta nao aceita lancamentos.")
    {
    }

    public AccountInactiveException(Guid accountId, string status)
        : base($"A conta '{accountId}' nao aceita lancamentos na situacao '{status}'.")
    {
        AccountId = accountId;
        Status = status;
    }

    public AccountInactiveException(string message)
        : base(message)
    {
    }

    public AccountInactiveException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public Guid AccountId { get; }

    public string? Status { get; }
}

/// <summary>Valor de lancamento nao positivo. Ver RN-002.</summary>
public sealed class InvalidEntryAmountException : LedgerDomainException
{
    public InvalidEntryAmountException()
        : base("O valor do lancamento deve ser positivo.")
    {
    }

    public InvalidEntryAmountException(decimal amount)
        : base($"O valor do lancamento deve ser positivo. Recebido: {amount}.")
    {
        Amount = amount;
    }

    public InvalidEntryAmountException(string message)
        : base(message)
    {
    }

    public InvalidEntryAmountException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public decimal Amount { get; }
}

/// <summary>Comando de escrita sem chave de idempotencia. Ver RN-005.</summary>
public sealed class MissingIdempotencyKeyException : LedgerDomainException
{
    public MissingIdempotencyKeyException()
        : base("A chave de idempotencia e obrigatoria em todo comando de escrita.")
    {
    }

    public MissingIdempotencyKeyException(string message)
        : base(message)
    {
    }

    public MissingIdempotencyKeyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A posicao resultante seria negativa. Rejeicao integral, sem gravacao parcial.
/// Ver RN-001 e o cenario BDD F02.
/// </summary>
public sealed class InsufficientFundsException : LedgerDomainException
{
    public InsufficientFundsException()
        : base("Saldo insuficiente.")
    {
    }

    public InsufficientFundsException(decimal availableBalance, decimal requestedAmount)
        : base($"Saldo insuficiente. Disponivel: {availableBalance}. Solicitado: {requestedAmount}.")
    {
        AvailableBalance = availableBalance;
        RequestedAmount = requestedAmount;
    }

    public InsufficientFundsException(string message)
        : base(message)
    {
    }

    public InsufficientFundsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public decimal AvailableBalance { get; }

    public decimal RequestedAmount { get; }
}

/// <summary>Tentativa de estornar lancamento de outra conta.</summary>
public sealed class EntryNotFromThisAccountException : LedgerDomainException
{
    public EntryNotFromThisAccountException()
        : base("O lancamento nao pertence a esta conta.")
    {
    }

    public EntryNotFromThisAccountException(Guid entryId, Guid accountId)
        : base($"O lancamento '{entryId}' nao pertence a conta '{accountId}'.")
    {
        EntryId = entryId;
        AccountId = accountId;
    }

    public EntryNotFromThisAccountException(string message)
        : base(message)
    {
    }

    public EntryNotFromThisAccountException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public Guid EntryId { get; }

    public Guid AccountId { get; }
}

/// <summary>Estorno de estorno e proibido. Ver RN-004.</summary>
public sealed class CannotReverseReversalException : LedgerDomainException
{
    public CannotReverseReversalException()
        : base("Nao e possivel estornar um lancamento de estorno.")
    {
    }

    public CannotReverseReversalException(Guid entryId)
        : base($"O lancamento '{entryId}' ja e um estorno e nao pode ser estornado.")
    {
        EntryId = entryId;
    }

    public CannotReverseReversalException(string message)
        : base(message)
    {
    }

    public CannotReverseReversalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public Guid EntryId { get; }
}

/// <summary>Conta inexistente.</summary>
public sealed class AccountNotFoundException : LedgerDomainException
{
    public AccountNotFoundException()
        : base("Conta nao encontrada.")
    {
    }

    public AccountNotFoundException(Guid accountId)
        : base($"Conta nao encontrada: '{accountId}'.")
    {
        AccountId = accountId;
    }

    public AccountNotFoundException(string message)
        : base(message)
    {
    }

    public AccountNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public Guid AccountId { get; }
}

/// <summary>
/// Chave de idempotencia reutilizada com conteudo diferente. Ver RN-005 e ADR-0006.
/// </summary>
public sealed class IdempotencyConflictException : LedgerDomainException
{
    public IdempotencyConflictException()
        : base("A chave de idempotencia ja foi usada com conteudo diferente.")
    {
    }

    public IdempotencyConflictException(Guid accountId, string idempotencyKey)
        : base($"A chave de idempotencia '{idempotencyKey}' ja foi usada na conta '{accountId}' com conteudo diferente.")
    {
        AccountId = accountId;
        IdempotencyKey = idempotencyKey;
    }

    public IdempotencyConflictException(string message)
        : base(message)
    {
    }

    public IdempotencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public Guid AccountId { get; }

    public string? IdempotencyKey { get; }
}

/// <summary>
/// Conflito de concorrencia que sobreviveu as novas tentativas. Repetivel com
/// seguranca pelo chamador, porque a chave de idempotencia protege o reenvio.
/// Ver RNF-004.
/// </summary>
public sealed class LedgerUnavailableException : LedgerDomainException
{
    public LedgerUnavailableException()
        : base("O ledger esta temporariamente indisponivel. A nova tentativa e segura.")
    {
    }

    public LedgerUnavailableException(string message)
        : base(message)
    {
    }

    public LedgerUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
