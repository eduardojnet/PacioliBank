using Npgsql;
using PacioliBank.Events;

namespace PacioliBank.Api.Events;

/// <summary>
/// Executa o despachante de outbox em laco, dentro do processo da API.
/// </summary>
/// <remarks>
/// Rodar no mesmo processo e decisao operacional, nao arquitetural (ADR-0008):
/// o mesmo despachante pode rodar em processo separado, e varias instancias
/// convivem por causa do <c>SKIP LOCKED</c>.
/// <para>
/// Lote cheio significa fila acumulada, entao a proxima passada e imediata;
/// lote parcial significa fila em dia, entao espera o intervalo. Banco fora do
/// ar nao derruba o servico: registra, espera e tenta de novo, e o lancamento
/// continua independente da publicacao (RF-011).
/// </para>
/// </remarks>
public sealed partial class OutboxDispatcherService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);

    private readonly OutboxDispatcher _dispatcher;
    private readonly OutboxDispatcherOptions _options;
    private readonly ILogger<OutboxDispatcherService> _logger;

    public OutboxDispatcherService(
        OutboxDispatcher dispatcher,
        OutboxDispatcherOptions options,
        ILogger<OutboxDispatcherService> logger)
    {
        _dispatcher = dispatcher;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;

            try
            {
                var result = await _dispatcher.DispatchOnceAsync(stoppingToken);

                if (result.Failed.Count > 0)
                {
                    LogFailed(_logger, result.Failed.Count);
                }

                foreach (var messageId in result.Parked)
                {
                    LogParked(_logger, messageId, _options.MaxAttempts);
                }

                wait = result.Read >= _options.BatchSize ? TimeSpan.Zero : PollInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (NpgsqlException ex)
            {
                LogStorageUnavailable(_logger, ex);
                wait = ErrorBackoff;
            }

            if (wait > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(wait, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Outbox: {Count} mensagem(ns) nao publicada(s); voltam para a fila com recuo exponencial")]
    private static partial void LogFailed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "ALERTA outbox: message_id={MessageId} atingiu {MaxAttempts} tentativas e saiu da fila. Exige inspecao: os consumidores nao receberao este evento")]
    private static partial void LogParked(ILogger logger, Guid messageId, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Outbox: armazenamento indisponivel; nova passada em instantes")]
    private static partial void LogStorageUnavailable(ILogger logger, Exception exception);
}
