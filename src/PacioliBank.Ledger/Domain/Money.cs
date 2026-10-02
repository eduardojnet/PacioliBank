using System.Globalization;

namespace PacioliBank.Ledger.Domain;

/// <summary>
/// Valor monetario exato. Ver ADR-0004.
/// O construtor e privado: nao existe instancia de <see cref="Money"/> com escala invalida.
/// A moeda acompanha o valor, de modo que operacao entre moedas distintas lanca excecao
/// em vez de produzir resultado silencioso.
/// </summary>
public readonly record struct Money
{
    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>Valor exato, em base decimal. Nunca ponto flutuante binario.</summary>
    public decimal Amount { get; }

    /// <summary>Moeda a que o valor pertence.</summary>
    public Currency Currency { get; }

    /// <summary>Verdadeiro quando o valor e estritamente maior que zero. Ver RN-002.</summary>
    public bool IsPositive => Amount > 0m;

    /// <summary>Verdadeiro quando o valor e estritamente menor que zero. Ver RN-001.</summary>
    public bool IsNegative => Amount < 0m;

    public bool IsZero => Amount == 0m;

    /// <summary>
    /// Cria um valor monetario, recusando escala incompativel com a moeda.
    /// </summary>
    /// <exception cref="InvalidMoneyScaleException">
    /// Quando o valor tem mais casas decimais do que a moeda expoe.
    /// </exception>
    public static Money Of(decimal amount, Currency currency)
    {
        if (currency.IsUndefined)
        {
            throw new UnsupportedCurrencyException(string.Empty);
        }

        if (decimal.Round(amount, currency.Scale, MidpointRounding.ToEven) != amount)
        {
            throw new InvalidMoneyScaleException(amount, currency.Code);
        }

        return new Money(amount, currency);
    }

    /// <summary>Valor zero na moeda informada.</summary>
    public static Money Zero(Currency currency) => Of(0m, currency);

    /// <summary>
    /// Converte a representacao textual usada no contrato de API (string decimal, cultura invariante).
    /// Ver EF secao 8.2: valores monetarios trafegam como string para preservar precisao no consumidor.
    /// </summary>
    public static Money Parse(string value, Currency currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var amount = decimal.Parse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return Of(amount, currency);
    }

    /// <summary>Alternativa nomeada ao operador de soma.</summary>
    public static Money Add(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    /// <summary>Alternativa nomeada ao operador de subtracao.</summary>
    public static Money Subtract(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator +(Money left, Money right) => Add(left, right);

    public static Money operator -(Money left, Money right) => Subtract(left, right);

    /// <summary>
    /// Compara dois valores da mesma moeda. Negativo, zero ou positivo,
    /// conforme a convencao de <see cref="decimal.Compare(decimal, decimal)"/>.
    /// </summary>
    public static int Compare(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return decimal.Compare(left.Amount, right.Amount);
    }

    /// <summary>Representacao no formato do contrato de API: casas fixas, cultura invariante.</summary>
    public string ToContractString() =>
        Amount.ToString("F" + Currency.Scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    public override string ToString() =>
        string.Concat(Currency.Code ?? string.Empty, " ", ToContractString());

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new CurrencyMismatchException(left.Currency.Code ?? string.Empty, right.Currency.Code ?? string.Empty);
        }
    }
}
