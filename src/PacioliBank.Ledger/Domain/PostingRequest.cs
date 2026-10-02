namespace PacioliBank.Ledger.Domain;

/// <summary>
/// Comando de lancamento, ja autenticado e autorizado na borda.
/// Transporta apenas o que o dominio precisa para decidir.
/// </summary>
/// <param name="Direction">Sentido do efeito na posicao.</param>
/// <param name="Amount">Valor, sempre positivo (RN-002).</param>
/// <param name="OccurredAt">Instante do fato financeiro (RN-009).</param>
/// <param name="IdempotencyKey">Chave fornecida pelo chamador. Obrigatoria (RN-005).</param>
/// <param name="CorrelationId">Rastreabilidade ponta a ponta.</param>
/// <param name="ReversalOf">Lancamento de origem, quando o comando e um estorno (RN-004).</param>
public sealed record PostingRequest(
    EntryDirection Direction,
    Money Amount,
    DateTimeOffset OccurredAt,
    string IdempotencyKey,
    Guid CorrelationId,
    Guid? ReversalOf = null);
