namespace PacioliBank.Ledger.Domain;

/// <summary>
/// Moeda de uma conta. Ver ADR-0004 e RN-007: a moeda viaja junto do valor,
/// de modo que soma entre moedas distintas seja impossivel por construcao.
/// </summary>
public readonly record struct Currency
{
    /// <summary>Real brasileiro. Escala de exposicao: 2 casas decimais.</summary>
    public static readonly Currency Brl = new("BRL", 2);

    // Stryker disable once Block : roda uma unica vez por processo, na
    // inicializacao estatica de Brl. Mutado, contamina todo o processo de teste
    // e torna a pontuacao de mutacao instavel (medido no card 36).
    private Currency(string code, int scale)
    {
        Code = code;
        Scale = scale;
    }

    /// <summary>Codigo ISO 4217 em letras maiusculas.</summary>
    public string Code { get; }

    /// <summary>Numero de casas decimais que a moeda expoe.</summary>
    public int Scale { get; }

    /// <summary>
    /// Resolve a moeda a partir do codigo ISO 4217.
    /// QA-005 mantem o catalogo restrito a BRL ate que o requisito multimoeda seja confirmado.
    /// </summary>
    public static Currency FromCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return code.Trim().ToUpperInvariant() switch
        {
            "BRL" => Brl,
            _ => throw new UnsupportedCurrencyException(code),
        };
    }

    /// <summary>Tenta resolver a moeda sem lancar excecao.</summary>
    public static bool TryFromCode(string? code, out Currency currency)
    {
        if (!string.IsNullOrWhiteSpace(code) && string.Equals(code.Trim(), "BRL", StringComparison.OrdinalIgnoreCase))
        {
            currency = Brl;
            return true;
        }

        currency = default;
        return false;
    }

    /// <summary>Verdadeiro para a instancia default do struct, que nao representa moeda alguma.</summary>
    public bool IsUndefined => string.IsNullOrEmpty(Code);

    public override string ToString() => Code ?? string.Empty;
}
