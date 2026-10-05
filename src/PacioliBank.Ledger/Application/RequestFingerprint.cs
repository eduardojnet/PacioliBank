using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PacioliBank.Ledger.Domain;

namespace PacioliBank.Ledger.Application;

/// <summary>
/// Impressao canonica de um comando de lancamento (ADR-0006).
/// </summary>
/// <remarks>
/// Existe para distinguir repeticao legitima de reuso indevido de chave.
/// A canonicalizacao e necessaria porque dois comandos semanticamente iguais
/// podem chegar com serializacoes diferentes, e diferenca de serializacao nao
/// pode produzir conflito.
/// <para>
/// A chave de idempotencia e o identificador de correlacao NAO entram na
/// impressao: a chave e o que identifica o comando, e a correlacao muda a cada
/// tentativa do chamador sem alterar a operacao pedida.
/// </para>
/// </remarks>
public static class RequestFingerprint
{
    /// <summary>Calcula a impressao SHA-256 do comando.</summary>
    public static byte[] Of(Guid accountId, PostingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var canonical = string.Join('|',
            accountId.ToString("D", CultureInfo.InvariantCulture),
            ((int)request.Direction).ToString(CultureInfo.InvariantCulture),
            request.Amount.ToContractString(),
            request.Amount.Currency.Code,
            request.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            request.ReversalOf?.ToString("D", CultureInfo.InvariantCulture) ?? string.Empty);

        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    /// <summary>
    /// Calcula a impressao SHA-256 de um comando de estorno.
    /// </summary>
    /// <remarks>
    /// O prefixo distingue o estorno do lancamento comum: a mesma chave usada
    /// nos dois comandos e reuso indevido e precisa produzir conflito, nunca
    /// coincidencia de impressao.
    /// </remarks>
    /// <summary>
    /// Calcula a impressao SHA-256 de uma transferencia (RF-012).
    /// </summary>
    /// <remarks>
    /// O prefixo distingue a transferencia do debito comum da origem: a mesma
    /// chave nos dois comandos e reuso indevido. A ordem das contas entra na
    /// impressao: A para B e B para A sao operacoes diferentes.
    /// </remarks>
    public static byte[] OfTransfer(Guid sourceAccountId, Guid destinationAccountId, TransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var canonical = string.Join('|',
            "transfer",
            sourceAccountId.ToString("D", CultureInfo.InvariantCulture),
            destinationAccountId.ToString("D", CultureInfo.InvariantCulture),
            request.Amount.ToContractString(),
            request.Amount.Currency.Code,
            request.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }


    public static byte[] OfReversal(Guid accountId, Guid entryId, ReversalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var canonical = string.Join('|',
            "reversal",
            accountId.ToString("D", CultureInfo.InvariantCulture),
            entryId.ToString("D", CultureInfo.InvariantCulture),
            request.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }
}
