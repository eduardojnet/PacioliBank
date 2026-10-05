using System.Diagnostics.Metrics;
using Npgsql;
using PacioliBank.Events;

namespace PacioliBank.Api.Observability;

/// <summary>
/// Profundidade da fila de eventos (RNF-032): mensagens da outbox ainda nao
/// publicadas, separadas em pendentes e estacionadas (ADR-0008).
/// </summary>
/// <remarks>
/// Lida do banco so quando a metrica e coletada, e nao a cada escrita: a fila
/// e estado do banco, e o despachante pode rodar em outra instancia. Banco
/// indisponivel na coleta resulta em ausencia de medicao, nao em falha da coleta.
/// </remarks>
public sealed class OutboxMetrics
{
    private const string CountSql = """
        SELECT count(*) FILTER (WHERE attempts < @maxAttempts)  AS pending,
               count(*) FILTER (WHERE attempts >= @maxAttempts) AS parked
          FROM ledger.outbox_messages
         WHERE published_at IS NULL
        """;

    private readonly NpgsqlDataSource _dataSource;
    private readonly int _maxAttempts;

    public OutboxMetrics(NpgsqlDataSource dataSource, OutboxDispatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _dataSource = dataSource;
        _maxAttempts = options.MaxAttempts;

        LedgerTelemetry.Meter.CreateObservableGauge(
            "ledger.outbox.messages", Measure, unit: "{message}",
            description: "Mensagens da outbox ainda nao publicadas, por estado.");
    }

    private IEnumerable<Measurement<long>> Measure()
    {
        try
        {
            using var command = _dataSource.CreateCommand(CountSql);
            command.Parameters.AddWithValue("maxAttempts", _maxAttempts);
            using var reader = command.ExecuteReader();
            reader.Read();

            return
            [
                new Measurement<long>(reader.GetInt64(0), new KeyValuePair<string, object?>("state", "pending")),
                new Measurement<long>(reader.GetInt64(1), new KeyValuePair<string, object?>("state", "parked")),
            ];
        }
        catch (NpgsqlException)
        {
            return [];
        }
    }
}
