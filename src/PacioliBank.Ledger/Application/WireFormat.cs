using System.Globalization;

namespace PacioliBank.Ledger.Application;

/// <summary>
/// Formatos de fio comuns aos contratos externos do ledger: a API (EF secao 8)
/// e os eventos de integracao (EF secao 9).
/// </summary>
/// <remarks>
/// Um unico dono para o formato evita que os dois contratos divirjam: o mesmo
/// instante nao pode sair com <c>Z</c> na API e com <c>+00:00</c> no evento.
/// O valor monetario ja tem dono em <see cref="Domain.Money.ToContractString"/>.
/// </remarks>
public static class WireFormat
{
    /// <summary>ISO 8601 em UTC com sufixo Z (EF secao 8.1).</summary>
    public static string Instant(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
}
