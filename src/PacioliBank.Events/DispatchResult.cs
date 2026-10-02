namespace PacioliBank.Events;

/// <summary>Resultado de uma passada do despachante.</summary>
/// <param name="Published">Mensagens publicadas e marcadas nesta passada.</param>
/// <param name="Failed">Mensagens cuja publicacao falhou e que voltam para a fila com recuo.</param>
/// <param name="Parked">
/// Subconjunto de <paramref name="Failed"/> que atingiu o limite de tentativas
/// e saiu da fila. Exige alerta: sao eventos que os consumidores nao receberao
/// sem intervencao.
/// </param>
public sealed record DispatchResult(
    IReadOnlyList<Guid> Published,
    IReadOnlyList<Guid> Failed,
    IReadOnlyList<Guid> Parked)
{
    /// <summary>Quantas mensagens a passada leu.</summary>
    public int Read => Published.Count + Failed.Count;
}
