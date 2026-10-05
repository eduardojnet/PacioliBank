using System.Diagnostics;
using PacioliBank.Ledger.Application;

namespace PacioliBank.Api.Observability;

/// <summary>
/// Envolve a porta de entrada com o span do caso de uso (RNF-030, ADR-0012).
/// </summary>
/// <remarks>
/// Decorador na API, e nao instrumentacao dentro do nucleo: o nucleo fica sem
/// codigo que os testes de dominio nao observam, e este ponto ve comando,
/// resultado e excecao, o que basta ao traco e as metricas.
/// <para>
/// Nenhum identificador vai para os spans. A rejeicao e registrada pelo tipo
/// da excecao, nunca pela mensagem, que traz o identificador da conta.
/// </para>
/// </remarks>
public sealed class ObservedLedgerService(ILedgerService inner) : ILedgerService
{
    public Task<PostEntryResult> PostAsync(PostingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Observe("ledger.post", activity => activity?.SetTag("ledger.direction", command.Direction.ToString()),
            () => inner.PostAsync(command, cancellationToken));
    }

    public Task<PostEntryResult> ReverseAsync(ReversalCommand command, CancellationToken cancellationToken) =>
        Observe("ledger.reverse", _ => { }, () => inner.ReverseAsync(command, cancellationToken));

    public Task<BalanceResult> GetBalanceAsync(Guid accountId, DateTimeOffset? asOf, CancellationToken cancellationToken) =>
        Observe("ledger.balance", activity => activity?.SetTag("ledger.point_in_time", asOf is not null),
            () => inner.GetBalanceAsync(accountId, asOf, cancellationToken));

    public Task<StatementPage> GetStatementAsync(StatementQuery query, CancellationToken cancellationToken) =>
        Observe("ledger.statement", _ => { }, () => inner.GetStatementAsync(query, cancellationToken));

    private static async Task<T> Observe<T>(string operation, Action<Activity?> describe, Func<Task<T>> run)
    {
        using var activity = LedgerTelemetry.Source.StartActivity(operation, ActivityKind.Internal);
        describe(activity);

        try
        {
            return await run().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("ledger.rejection", ex.GetType().Name);
            throw;
        }
    }
}
