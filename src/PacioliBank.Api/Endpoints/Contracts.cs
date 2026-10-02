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
/// </remarks>
public sealed record PostingBody(string? Amount, string? Currency, DateTimeOffset? OccurredAt);

/// <summary>Corpo do estorno. O instante e obrigatorio para que o reenvio tenha a mesma impressao.</summary>
public sealed record ReversalBody(DateTimeOffset? OccurredAt);

// O corpo da resposta de escrita (EF secao 8.4) e PostingResponse, na camada
// de aplicacao: e gravado no registro de idempotencia e devolvido como texto,
// entao nao pode ser montado aqui (ADR-0006, card 19.5).

/// <summary>Posicao consolidada (EF secao 8.5).</summary>
public sealed record BalanceResponse(
    Guid AccountId,
    string Balance,
    string Currency,
    string AsOf,
    long ComputedAtSequence,
    string ComputedFrom)
{
    public static BalanceResponse From(BalanceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new(
            result.AccountId,
            result.Balance.ToContractString(),
            result.Balance.Currency.Code,
            ContractFormat.Instant(result.AsOf),
            result.ComputedAtSequence,
            result.ComputedFrom == BalanceSource.Snapshot ? "snapshot" : "ledger");
    }
}

/// <summary>Linha do extrato.</summary>
public sealed record StatementEntryResponse(
    Guid EntryId,
    long Sequence,
    string Direction,
    string Amount,
    string Currency,
    string OccurredAt,
    string RecordedAt,
    string BalanceAfter,
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
                e.Amount.ToContractString(),
                e.Amount.Currency.Code,
                ContractFormat.Instant(e.OccurredAt),
                ContractFormat.Instant(e.RecordedAt),
                e.BalanceAfter.ToContractString(),
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
