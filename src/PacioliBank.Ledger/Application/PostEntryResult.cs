using PacioliBank.Ledger.Domain;

namespace PacioliBank.Ledger.Application;

/// <summary>
/// Resultado de um comando de lancamento, no formato que a borda expoe.
/// </summary>
/// <param name="Replayed">
/// Verdadeiro quando o comando ja havia sido processado e a resposta original
/// foi devolvida em vez de um novo lancamento (RN-005, ADR-0006).
/// </param>
public sealed record PostEntryResult(
    Guid EntryId,
    Guid AccountId,
    long Sequence,
    EntryDirection Direction,
    Money Amount,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    Money BalanceAfter,
    bool Replayed)
{
    /// <summary>
    /// Corpo HTTP da resposta, no formato do contrato (EF secao 8.4). E o
    /// texto gravado em <c>idempotency_records.response_body</c>: na repeticao,
    /// vem do registro, byte a byte, e nao de reconstrucao (ADR-0006).
    /// </summary>
    public string ResponseBody { get; init; } = string.Empty;
}
