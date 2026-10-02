namespace PacioliBank.Ledger.Domain;

/// <summary>
/// Comando de estorno, ja autenticado e autorizado na borda (RN-004).
/// Sentido e valor nao viajam aqui: derivam do lancamento original, e
/// aceita-los do chamador permitiria um estorno que nao compensa o original.
/// </summary>
/// <param name="OccurredAt">Instante do fato do estorno (RN-009).</param>
/// <param name="IdempotencyKey">Chave fornecida pelo chamador. Obrigatoria (RN-005).</param>
/// <param name="CorrelationId">Rastreabilidade ponta a ponta.</param>
public sealed record ReversalRequest(
    DateTimeOffset OccurredAt,
    string IdempotencyKey,
    Guid CorrelationId);
