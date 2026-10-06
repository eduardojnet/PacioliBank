using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PacioliBank.Ledger.Domain;

namespace PacioliBank.Ledger.Application;

/// <summary>
/// Corpo da resposta de um comando de escrita: credito, debito ou estorno
/// (EF secao 8.4).
/// </summary>
/// <remarks>
/// Vive na aplicacao, e nao no adaptador HTTP, porque e gravado: o texto
/// produzido aqui vai para <c>idempotency_records.response_body</c> na mesma
/// transacao do lancamento, e a repeticao devolve esse texto, byte a byte
/// (ADR-0006, revisao do card 19.5). Montado em dois lugares, o corpo da
/// primeira resposta e o da repeticao poderiam divergir; montado aqui, uma vez,
/// sao o mesmo texto por construcao.
/// <para>
/// Mesmos formatos da API: valores monetarios como numero na escala da moeda, instantes em Z,
/// <c>reversalOf</c> apenas no estorno.
/// </para>
/// </remarks>
public sealed record PostingResponse(
    string EntryId,
    string AccountId,
    long Sequence,
    string Direction,
    decimal Amount,
    string Currency,
    string OccurredAt,
    string RecordedAt,
    decimal BalanceAfter,
    string? ReversalOf)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Monta o corpo a partir do lancamento ja decidido pelo agregado.</summary>
    public static PostingResponse From(LedgerEntry entry, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new PostingResponse(
            entry.EntryId.ToString("D", CultureInfo.InvariantCulture),
            entry.AccountId.ToString("D", CultureInfo.InvariantCulture),
            entry.Sequence,
            entry.Direction.ToString(),
            entry.Amount.ToContractAmount(),
            entry.Amount.Currency.Code,
            WireFormat.Instant(entry.OccurredAt),
            WireFormat.Instant(recordedAt),
            entry.BalanceAfter.ToContractAmount(),
            entry.ReversalOf?.ToString("D", CultureInfo.InvariantCulture));
    }

    /// <summary>O texto que e gravado e devolvido.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
