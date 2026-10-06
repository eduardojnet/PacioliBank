using System.Globalization;
using PacioliBank.Ledger.Application;

namespace PacioliBank.Api.Endpoints;

/// <summary>Cabecalhos do contrato (convencoes de nomenclatura, secao 7).</summary>
public static class LedgerHeaders
{
    public const string IdempotencyKey = "Idempotency-Key";
    public const string IdempotencyReplayed = "Idempotency-Replayed";
    public const string CorrelationId = "X-Correlation-Id";
}

/// <summary>
/// Corpo de credito e debito (EF secao 8.4). Todos os campos sao anulaveis
/// de proposito: ausencia vira 400 com codigo estavel, e nao falha de
/// desserializacao sem corpo de erro.
/// </summary>
/// <remarks>
/// <c>description</c> e <c>metadata</c>, presentes no exemplo da EF, ainda nao
/// sao persistidos: o lancamento de dominio nao os carrega. Lacuna declarada
/// no ESTADO.md, e nao descarte silencioso.
/// <para>
/// <c>amount</c> e numero JSON (EF secao 8.2). Texto no lugar do numero e
/// recusado pelo desserializador, em modo estrito, como requisicao invalida.
/// </para>
/// </remarks>
public sealed record PostingBody(decimal? Amount, string? Currency, DateTimeOffset? OccurredAt);

/// <summary>Corpo do estorno. O instante e obrigatorio para que o reenvio tenha a mesma impressao.</summary>
public sealed record ReversalBody(DateTimeOffset? OccurredAt);

/// <summary>
/// Corpo da transferencia (EF secao 8.4.1). As contas viajam no corpo, e nao
/// na rota: a operacao e de duas contas, e nenhuma delas e o recurso.
/// </summary>
public sealed record TransferBody(
    Guid? SourceAccountId,
    Guid? DestinationAccountId,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? OccurredAt);

// O corpo da resposta de escrita (EF secao 8.4) e PostingResponse, na camada
// de aplicacao: e gravado no registro de idempotencia e devolvido como texto,
// entao nao pode ser montado aqui (ADR-0006, card 19.5).

/// <summary>
/// Posicao consolidada (EF secao 8.5). <c>EntriesReplayed</c> e quantos
/// lancamentos foram somados alem do snapshot: metrica de RNF-006, e o que o
/// painel de evidencia mostra para tornar visivel o snapshot (ADR-0007, ADR-0011).
/// </summary>
public sealed record BalanceResponse(
    Guid AccountId,
    decimal Balance,
    string Currency,
    string AsOf,
    long ComputedAtSequence,
    string ComputedFrom,
    int EntriesReplayed)
{
    public static BalanceResponse From(BalanceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new(
            result.AccountId,
            result.Balance.ToContractAmount(),
            result.Balance.Currency.Code,
            ContractFormat.Instant(result.AsOf),
            result.ComputedAtSequence,
            result.ComputedFrom switch
            {
                BalanceSource.Snapshot => "snapshot",
                BalanceSource.DailyBalance => "dailyBalance",
                _ => "ledger",
            },
            result.EntriesReplayed);
    }
}

/// <summary>Linha do extrato.</summary>
public sealed record StatementEntryResponse(
    Guid EntryId,
    long Sequence,
    string Direction,
    decimal Amount,
    string Currency,
    string OccurredAt,
    string RecordedAt,
    decimal BalanceAfter,
    Guid? ReversalOf);

/// <summary>Pagina do extrato. <c>NextCursor</c> ausente indica a ultima pagina.</summary>
public sealed record StatementResponse(
    Guid AccountId,
    IReadOnlyList<StatementEntryResponse> Entries,
    string? NextCursor)
{
    public static StatementResponse From(StatementPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var entries = page.Entries
            .Select(e => new StatementEntryResponse(
                e.EntryId,
                e.Sequence,
                e.Direction.ToString(),
                e.Amount.ToContractAmount(),
                e.Amount.Currency.Code,
                ContractFormat.Instant(e.OccurredAt),
                ContractFormat.Instant(e.RecordedAt),
                e.BalanceAfter.ToContractAmount(),
                e.ReversalOf))
            .ToList();

        return new(
            page.AccountId,
            entries,
            page.NextAfterSequence?.ToString(CultureInfo.InvariantCulture));
    }
}

/// <summary>Formatos do contrato que nao pertencem ao dominio.</summary>
public static class ContractFormat
{
    /// <summary>ISO 8601 em UTC com sufixo Z (EF secao 8.1). Mesmo dono do formato dos eventos.</summary>
    public static string Instant(DateTimeOffset value) => WireFormat.Instant(value);
}
