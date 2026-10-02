namespace PacioliBank.Events;

/// <summary>
/// Porta de saida para o barramento de eventos.
/// </summary>
/// <remarks>
/// O barramento concreto nao e decidido: depende da plataforma de mensageria
/// do banco (ADR-0008, "Escolha do barramento"). A decisao arquitetural e o
/// padrao outbox, nao o produto; trocar o barramento e trocar a implementacao
/// desta interface, sem tocar no ledger nem no mecanismo de consistencia.
/// </remarks>
public interface IEventPublisher
{
    /// <summary>
    /// Publica uma mensagem. Excecao significa "nao publicada": a mensagem
    /// volta para a fila com recuo exponencial.
    /// </summary>
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}
