namespace PacioliBank.Ledger.Domain;

/// <summary>
/// Situacao da conta. Dado de referencia replicado do contexto Cadastro:
/// este sistema le o status, nunca o altera (EF secao 3.2, ADR-0009).
/// </summary>
public enum AccountStatus
{
    /// <summary>Aceita lancamentos e consultas.</summary>
    Active = 1,

    /// <summary>Aceita apenas consultas.</summary>
    Blocked = 2,

    /// <summary>Encerrada. Aceita apenas consultas.</summary>
    Closed = 3,
}
