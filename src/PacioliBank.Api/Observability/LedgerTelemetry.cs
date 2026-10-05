using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PacioliBank.Api.Observability;

/// <summary>Fonte de spans e instrumentos de metrica do ledger (ADR-0012).</summary>
/// <remarks>
/// As metricas de negocio da RNF-032. A de maior valor diagnostico e a razao
/// entre consultas com <c>computed_from=Ledger</c> e o total: ela cresce antes
/// de a latencia degradar (ENF secao 6), e sai de <see cref="BalanceQueries"/>.
/// </remarks>
public static class LedgerTelemetry
{
    /// <summary>Nome da fonte de spans e do medidor.</summary>
    public const string Name = "PacioliBank.Ledger";

    internal static readonly ActivitySource Source = new(Name);

    internal static readonly Meter Meter = new(Name);

    /// <summary>Lancamentos gravados, por sentido e operacao (post ou reverse).</summary>
    internal static readonly Counter<long> EntriesRecorded = Meter.CreateCounter<long>(
        "ledger.entries.recorded", unit: "{entry}", description: "Lancamentos gravados, por sentido e operacao.");

    /// <summary>Reenvios idempotentes atendidos com a resposta original, sem lancamento novo.</summary>
    internal static readonly Counter<long> Replays = Meter.CreateCounter<long>(
        "ledger.replays", unit: "{request}", description: "Reenvios idempotentes atendidos com a resposta original.");

    /// <summary>Rejeicoes por codigo da EF secao 8.6.</summary>
    internal static readonly Counter<long> Rejections = Meter.CreateCounter<long>(
        "ledger.rejections", unit: "{request}", description: "Comandos e consultas recusados, por codigo da EF 8.6.");

    /// <summary>Consultas de posicao por origem do calculo (snapshot ou ledger).</summary>
    internal static readonly Counter<long> BalanceQueries = Meter.CreateCounter<long>(
        "ledger.balance.queries", unit: "{query}", description: "Consultas de posicao, por origem do calculo.");

    /// <summary>Lancamentos somados alem do snapshot em cada consulta de posicao (RNF-006).</summary>
    internal static readonly Histogram<int> EntriesReplayed = Meter.CreateHistogram<int>(
        "ledger.balance.entries_replayed", unit: "{entry}", description: "Lancamentos somados alem do snapshot, por consulta.");
}
