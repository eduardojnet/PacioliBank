// Stryker disable String : mensagens de excecao sao texto para humanos. O
// contrato com o chamador e o codigo da EF secao 8.6, decidido pelo tipo da
// excecao, e os dados que ela carrega, verificados nos testes (card 36).

namespace PacioliBank.Ledger.Domain;

/// <summary>Raiz das excecoes de invariante do dominio do ledger.</summary>
/// <remarks>
/// Cada excecao tem so os construtores que o codigo usa, em regra o que exige
/// os dados da rejeicao. Os construtores "padrao" (sem argumento, so mensagem,
/// mensagem com excecao interna) foram removidos no card 31.2: ninguem os
/// chamava, e permitiam criar, por exemplo, saldo insuficiente sem os valores
/// que a borda HTTP devolve ao chamador. A regra CA1032, que os exigiria, nao
/// esta ativa no modo Recommended.
/// </remarks>
public class LedgerDomainException : Exception
{
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
    public UnsupportedCurrencyException(string code)
        : base($"Moeda nao suportada: '{code}'.")
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Valor com mais casas decimais do que a moeda expoe. Ver RN-002.</summary>
public sealed class InvalidMoneyScaleException : LedgerDomainException
{
    public InvalidMoneyScaleException(decimal amount, string currencyCode)
        : base($"O valor {amount} excede a escala da moeda '{currencyCode}'.")
    {
        Amount = amount;
        CurrencyCode = currencyCode;
    }

    public decimal Amount { get; }

    public string CurrencyCode { get; }
}

/// <summary>Operacao aritmetica entre moedas distintas. Ver RN-007.</summary>
public sealed class CurrencyMismatchException : LedgerDomainException
{
    public CurrencyMismatchException(string left, string right)
        : base($"Operacao entre moedas distintas: '{left}' e '{right}'.")
    {
        Left = left;
        Right = right;
    }

    public string Left { get; }

    public string Right { get; }
}

/// <summary>A conta nao aceita lancamentos na situacao atual. Ver RN-008.</summary>
public sealed class AccountInactiveException : LedgerDomainException
{
    public AccountInactiveException(Guid accountId, string status)
        : base($"A conta '{accountId}' nao aceita lancamentos na situacao '{status}'.")
    {
        AccountId = accountId;
        Status = status;
    }

    public Guid AccountId { get; }

    public string Status { get; }
}

/// <summary>Valor de lancamento nao positivo. Ver RN-002.</summary>
public sealed class InvalidEntryAmountException : LedgerDomainException
{
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

}

/// <summary>
/// A posicao resultante seria negativa. Rejeicao integral, sem gravacao parcial.
/// Ver RN-001 e o cenario BDD F02.
/// </summary>
public sealed class InsufficientFundsException : LedgerDomainException
{
    public InsufficientFundsException(decimal availableBalance, decimal requestedAmount)
        : base($"Saldo insuficiente. Disponivel: {availableBalance}. Solicitado: {requestedAmount}.")
    {
        AvailableBalance = availableBalance;
        RequestedAmount = requestedAmount;
    }

    public decimal AvailableBalance { get; }

    public decimal RequestedAmount { get; }
}

/// <summary>Tentativa de estornar lancamento de outra conta.</summary>
public sealed class EntryNotFromThisAccountException : LedgerDomainException
{
    public EntryNotFromThisAccountException(Guid entryId, Guid accountId)
        : base($"O lancamento '{entryId}' nao pertence a conta '{accountId}'.")
    {
        EntryId = entryId;
        AccountId = accountId;
    }

    public Guid EntryId { get; }

    public Guid AccountId { get; }
}

/// <summary>Estorno de estorno e proibido. Ver RN-004.</summary>
public sealed class CannotReverseReversalException : LedgerDomainException
{
    public CannotReverseReversalException(Guid entryId)
        : base($"O lancamento '{entryId}' ja e um estorno e nao pode ser estornado.")
    {
        EntryId = entryId;
    }

    public Guid EntryId { get; }
}

/// <summary>Conta inexistente.</summary>
public sealed class AccountNotFoundException : LedgerDomainException
{
    public AccountNotFoundException(Guid accountId)
        : base($"Conta nao encontrada: '{accountId}'.")
    {
        AccountId = accountId;
    }

    public Guid AccountId { get; }
}

/// <summary>
/// Chave de idempotencia reutilizada com conteudo diferente. Ver RN-005 e ADR-0006.
/// </summary>
public sealed class IdempotencyConflictException : LedgerDomainException
{
    public IdempotencyConflictException(Guid accountId, string idempotencyKey)
        : base($"A chave de idempotencia '{idempotencyKey}' ja foi usada na conta '{accountId}' com conteudo diferente.")
    {
        AccountId = accountId;
        IdempotencyKey = idempotencyKey;
    }

    public Guid AccountId { get; }

    public string IdempotencyKey { get; }
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

    public LedgerUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Lancamento inexistente. Tambem usado, na borda, para lancamento de outra
/// conta: responder de forma distinta revelaria a existencia do lancamento
/// (mesmo principio do ADR-0009 para contas).
/// </summary>
public sealed class EntryNotFoundException : LedgerDomainException
{
    public EntryNotFoundException(Guid entryId)
        : base($"Lancamento nao encontrado: '{entryId}'.")
    {
        EntryId = entryId;
    }

    public Guid EntryId { get; }
}

/// <summary>
/// O lancamento ja possui estorno. Ver RN-004. A deteccao e estrutural, pela
/// constraint <c>uq_entries_reversal</c>, nunca por consulta previa.
/// </summary>
public sealed class EntryAlreadyReversedException : LedgerDomainException
{
    public EntryAlreadyReversedException(Guid entryId)
        : base($"O lancamento '{entryId}' ja foi estornado.")
    {
        EntryId = entryId;
    }

    public Guid EntryId { get; }
}

/// <summary>Limite de pagina do extrato acima do maximo admitido. Ver RF-005.</summary>
public sealed class PageSizeExceededException : LedgerDomainException
{
    public PageSizeExceededException(int requested, int maximum)
        : base($"Limite de pagina invalido: {requested}. O maximo admitido e {maximum}.")
    {
        Requested = requested;
        Maximum = maximum;
    }

    public int Requested { get; }

    public int Maximum { get; }
}

/// <summary>Instante de consulta invalido: futuro, ou intervalo invertido. Ver RN-009 e RF-004.</summary>
public sealed class InvalidPointInTimeException : LedgerDomainException
{
    public InvalidPointInTimeException(string message)
        : base(message)
    {
    }

}

/// <summary>Transferencia com origem e destino na mesma conta. Ver RN-013.</summary>
public sealed class SameAccountTransferException : LedgerDomainException
{
    public SameAccountTransferException(Guid accountId)
        : base($"Origem e destino da transferencia sao a mesma conta: '{accountId}'.")
    {
        AccountId = accountId;
    }

    public Guid AccountId { get; }
}
