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
    bool Replayed);
