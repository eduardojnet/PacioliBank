namespace PacioliBank.Events;

/// <summary>
/// Mensagem da outbox, como o despachante a entrega ao publicador.
/// </summary>
/// <param name="MessageId">
/// Estavel entre republicacoes: e o que permite ao consumidor deduplicar
/// (entrega ao menos uma vez, ADR-0008).
/// </param>
/// <param name="Sequence">
/// Sequencia do lancamento na conta. A ordem so e garantida dentro da conta,
/// por este campo, e nao pela ordem de chegada (EF secao 9).
/// </param>
/// <param name="Payload">Corpo do evento em JSON, gravado na transacao do lancamento.</param>
/// <param name="Attempts">Tentativas de publicacao ja falhadas antes desta.</param>
public sealed record OutboxMessage(
    Guid MessageId,
    Guid AccountId,
    long Sequence,
    string EventType,
    string Payload,
    DateTimeOffset OccurredAt,
    int Attempts);
