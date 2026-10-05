using System.Globalization;
using System.Text.Json;
using PacioliBank.Ledger.Domain;

namespace PacioliBank.Ledger.Application;

/// <summary>
/// Corpo da resposta de uma transferencia (EF secao 8.4.1).
/// </summary>
/// <remarks>
/// Gravado no registro de idempotencia e devolvido byte a byte na repeticao,
/// como o corpo de <see cref="PostingResponse"/> (ADR-0006).
/// <para>
/// Da perna de credito, so o identificador do lancamento. Saldo e sequencia do
/// destino sao do titular do destino: o pagador nao os ve (ADR-0014).
/// </para>
/// </remarks>
public sealed record TransferResponse(
    string TransferId,
    string SourceAccountId,
    string DestinationAccountId,
    string Amount,
    string Currency,
    string OccurredAt,
    string RecordedAt,
    TransferDebitLeg Debit,
    TransferCreditLeg Credit)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Monta o corpo a partir das pernas ja decididas.</summary>
    public static TransferResponse From(TransferLegs legs, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(legs);

        return new TransferResponse(
            Id(legs.TransferId),
            Id(legs.Debit.AccountId),
            Id(legs.Credit.AccountId),
            legs.Debit.Amount.ToContractString(),
            legs.Debit.Amount.Currency.Code,
            WireFormat.Instant(legs.Debit.OccurredAt),
            WireFormat.Instant(recordedAt),
            new TransferDebitLeg(Id(legs.Debit.EntryId), legs.Debit.Sequence, legs.Debit.BalanceAfter.ToContractString()),
            new TransferCreditLeg(Id(legs.Credit.EntryId)));
    }

    /// <summary>O texto que e gravado e devolvido.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    private static string Id(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>Perna de debito, na conta de origem: a do pagador, com o saldo dele.</summary>
public sealed record TransferDebitLeg(string EntryId, long Sequence, string BalanceAfter);

/// <summary>Perna de credito, na conta de destino: so o identificador do lancamento.</summary>
public sealed record TransferCreditLeg(string EntryId);
