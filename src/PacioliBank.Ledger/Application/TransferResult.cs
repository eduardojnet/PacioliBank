using PacioliBank.Ledger.Domain;

namespace PacioliBank.Ledger.Application;

/// <summary>
/// Resultado de uma transferencia (RF-012), no formato que a borda expoe.
/// </summary>
/// <param name="Replayed">
/// Verdadeiro quando a transferencia ja havia sido efetivada com a mesma chave
/// e a resposta original foi devolvida (RN-005, ADR-0006).
/// </param>
public sealed record TransferResult(
    Guid TransferId,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    Guid DebitEntryId,
    Guid CreditEntryId,
    Money Amount,
    bool Replayed)
{
    /// <summary>
    /// Corpo HTTP da resposta (EF secao 8.4.1), o texto gravado em
    /// <c>idempotency_records.response_body</c>; na repeticao, vem do registro (ADR-0006).
    /// </summary>
    public string ResponseBody { get; init; } = string.Empty;
}
