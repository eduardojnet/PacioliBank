using System.Globalization;
using PacioliBank.Ledger.Domain;

namespace PacioliBank.Ledger.Application;

/// <summary>
/// Payload dos eventos <c>pacioli.ledger.entry-recorded.v1</c> e
/// <c>pacioli.ledger.entry-reversed.v1</c> (EF secao 9).
/// </summary>
/// <remarks>
/// Tipo proprio, separado do resultado da API, de proposito: o evento e um
/// contrato com consumidores que nao conhecem o dominio, e serializar um tipo
/// do dominio vaza o que ele tem por dentro (propriedades calculadas do
/// <see cref="Money"/>, sentido como numero). Aqui so ha texto e numero,
/// no formato do contrato:
/// <list type="bullet">
///   <item>valores monetarios como numero, na escala da moeda (EF secao 8.2);</item>
///   <item>instantes em ISO 8601 UTC com sufixo Z (EF secao 8.1);</item>
///   <item>sentido como <c>Credit</c> ou <c>Debit</c>, como na API;</item>
///   <item><c>reversalOf</c> presente apenas no estorno.</item>
/// </list>
/// O <c>message_id</c> de deduplicacao nao esta aqui: e o identificador da
/// mensagem na outbox e viaja junto do evento, fora do payload (ADR-0008).
/// </remarks>
public sealed record LedgerEntryEvent(
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
    /// <summary>Tipo do evento de lancamento comum (convencoes, secao 8).</summary>
    public const string RecordedType = "pacioli.ledger.entry-recorded.v1";

    /// <summary>Tipo do evento de estorno (convencoes, secao 8).</summary>
    public const string ReversedType = "pacioli.ledger.entry-reversed.v1";

    /// <summary>Tipo do evento correspondente ao lancamento.</summary>
    public static string TypeOf(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsReversal ? ReversedType : RecordedType;
    }

    /// <summary>Monta o payload a partir do lancamento ja decidido pelo agregado.</summary>
    public static LedgerEntryEvent From(LedgerEntry entry, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new LedgerEntryEvent(
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
}
