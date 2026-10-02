using PacioliBank.Events;

namespace PacioliBank.Api.Events;

/// <summary>
/// Publicador que registra o evento em log, no lugar de um barramento.
/// </summary>
/// <remarks>
/// E a implementacao prevista pelo ADR-0008 para o ambiente local, enquanto a
/// plataforma de mensageria do banco nao e conhecida. Registra apenas
/// identificadores, tipo e sequencia: o payload fica fora do log (RNF-021).
/// </remarks>
public sealed partial class LoggingEventPublisher : IEventPublisher
{
    private readonly ILogger<LoggingEventPublisher> _logger;

    public LoggingEventPublisher(ILogger<LoggingEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        LogPublished(_logger, message.EventType, message.MessageId, message.AccountId, message.Sequence);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Evento publicado: {EventType} message_id={MessageId} conta={AccountId} sequencia={Sequence}")]
    private static partial void LogPublished(ILogger logger, string eventType, Guid messageId, Guid accountId, long sequence);
}
