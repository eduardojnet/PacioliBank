using PacioliBank.Ledger.Domain;

namespace PacioliBank.Ledger.Application;

/// <summary>Origem do calculo da posicao. Indicador antecedente de degradacao (ENF 7.2).</summary>
public enum BalanceSource
{
    /// <summary>Calculada a partir de um snapshot mais os lancamentos posteriores.</summary>
    Snapshot = 1,

    /// <summary>Calculada agregando o ledger. Esperado em consulta historica (ADR-0007).</summary>
    Ledger = 2,
}

/// <summary>
/// Posicao consolidada de uma conta.
/// </summary>
/// <param name="ComputedAtSequence">
/// Sequencia de referencia do calculo. Permite ao consumidor detectar leitura
/// desatualizada sem precisar comparar valores (RF-003).
/// </param>
/// <param name="EntriesReplayed">
/// Quantos lancamentos foram somados alem do snapshot. Metrica de RNF-006.
/// </param>
public sealed record BalanceResult(
    Guid AccountId,
    Money Balance,
    DateTimeOffset AsOf,
    long ComputedAtSequence,
    BalanceSource ComputedFrom,
    int EntriesReplayed);
