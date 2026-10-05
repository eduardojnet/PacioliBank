using System.Diagnostics;
using PacioliBank.Api.Endpoints;
using PacioliBank.Ledger.Application;

namespace PacioliBank.Api.Observability;

/// <summary>
/// Envolve a porta de entrada com o span do caso de uso (RNF-030) e as
/// metricas de negocio (RNF-032), conforme o ADR-0012.
/// </summary>
/// <remarks>
/// Decorador na API, e nao instrumentacao dentro do nucleo: o nucleo fica sem
/// codigo que os testes de dominio nao observam, e este ponto ve comando,
/// resultado e excecao, o que basta ao traco e as metricas.
/// <para>
/// Nenhum identificador vai para spans nem para metricas. A rejeicao e
/// registrada pelo tipo da excecao no span e pelo codigo da EF secao 8.6 na
/// metrica, nunca pela mensagem, que traz o identificador da conta.
/// </para>
/// </remarks>
public sealed class ObservedLedgerService(ILedgerService inner) : ILedgerService
{
    private const string Post = "post";
    private const string Reverse = "reverse";
    private const string Transfer = "transfer";
    private const string Balance = "balance";
    private const string Statement = "statement";

    public async Task<PostEntryResult> PostAsync(PostingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await Observe(Post, activity => activity?.SetTag("ledger.direction", command.Direction.ToString()),
            () => inner.PostAsync(command, cancellationToken)).ConfigureAwait(false);

        RecordWrite(result, Post);
        return result;
    }

    public async Task<PostEntryResult> ReverseAsync(ReversalCommand command, CancellationToken cancellationToken)
    {
        var result = await Observe(Reverse, _ => { }, () => inner.ReverseAsync(command, cancellationToken)).ConfigureAwait(false);

        RecordWrite(result, Reverse);
        return result;
    }

    public async Task<TransferResult> TransferAsync(TransferCommand command, CancellationToken cancellationToken)
    {
        var result = await Observe(Transfer, _ => { }, () => inner.TransferAsync(command, cancellationToken)).ConfigureAwait(false);

        // Duas pernas, dois lancamentos: a contagem continua batendo com o ledger.
        if (result.Replayed)
        {
            LedgerTelemetry.Replays.Add(1, new KeyValuePair<string, object?>("operation", Transfer));
            return result;
        }

        foreach (var direction in (string[])["Debit", "Credit"])
        {
            LedgerTelemetry.EntriesRecorded.Add(1,
                new KeyValuePair<string, object?>("direction", direction),
                new KeyValuePair<string, object?>("operation", Transfer));
        }

        return result;
    }

    public async Task<BalanceResult> GetBalanceAsync(Guid accountId, DateTimeOffset? asOf, CancellationToken cancellationToken)
    {
        var pointInTime = asOf is not null;

        var result = await Observe(Balance, activity => activity?.SetTag("ledger.point_in_time", pointInTime),
            () => inner.GetBalanceAsync(accountId, asOf, cancellationToken)).ConfigureAwait(false);

        var tags = new TagList
        {
            { "computed_from", result.ComputedFrom.ToString() },
            { "point_in_time", pointInTime.ToString() },
        };
        LedgerTelemetry.BalanceQueries.Add(1, tags);
        LedgerTelemetry.EntriesReplayed.Record(result.EntriesReplayed, tags);
        return result;
    }

    public Task<StatementPage> GetStatementAsync(StatementQuery query, CancellationToken cancellationToken) =>
        Observe(Statement, _ => { }, () => inner.GetStatementAsync(query, cancellationToken));

    // Reenvio nao e lancamento novo: contado a parte, para a contagem de
    // lancamentos bater com o ledger.
    private static void RecordWrite(PostEntryResult result, string operation)
    {
        if (result.Replayed)
        {
            LedgerTelemetry.Replays.Add(1, new KeyValuePair<string, object?>("operation", operation));
            return;
        }

        LedgerTelemetry.EntriesRecorded.Add(1,
            new KeyValuePair<string, object?>("direction", result.Direction.ToString()),
            new KeyValuePair<string, object?>("operation", operation));
    }

    private static async Task<T> Observe<T>(string operation, Action<Activity?> describe, Func<Task<T>> run)
    {
        using var activity = LedgerTelemetry.Source.StartActivity("ledger." + operation, ActivityKind.Internal);
        describe(activity);

        try
        {
            return await run().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("ledger.rejection", ex.GetType().Name);

            LedgerTelemetry.Rejections.Add(1,
                new KeyValuePair<string, object?>("code", LedgerProblems.CodeOf(ex) ?? "UNEXPECTED"),
                new KeyValuePair<string, object?>("operation", operation));
            throw;
        }
    }
}
