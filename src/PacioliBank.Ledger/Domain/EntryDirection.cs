namespace PacioliBank.Ledger.Domain;

/// <summary>
/// Sentido do efeito do lancamento sobre a posicao.
/// O valor monetario e sempre positivo: o sinal vem daqui (RN-002, ADR-0004).
/// Os valores numericos espelham a coluna <c>direction</c> da tabela.
/// </summary>
public enum EntryDirection
{
    /// <summary>Aumenta a posicao.</summary>
    Credit = 1,

    /// <summary>Reduz a posicao.</summary>
    Debit = -1,
}
