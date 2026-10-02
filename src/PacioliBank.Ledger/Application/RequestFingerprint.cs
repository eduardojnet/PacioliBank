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
}
