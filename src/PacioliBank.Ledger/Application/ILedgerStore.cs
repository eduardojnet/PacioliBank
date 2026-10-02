using PacioliBank.Ledger.Domain;

namespace PacioliBank.Ledger.Application;

/// <summary>
/// Porta de persistencia do ledger.
/// </summary>
/// <remarks>
/// A interface e deliberadamente estreita e orientada a caso de uso, nao um
/// repositorio generico. Razao: a serializacao de escrita por conta (ADR-0005),
/// a gravacao transacional conjunta de lancamento, idempotencia e outbox
/// (ADR-0006 e ADR-0008) e o snapshot amortizado (ADR-0007) formam uma unica
/// operacao indivisivel. Expo-los como metodos separados de um repositorio
/// generico permitiria compo-los na ordem errada, e a ordem errada aqui
/// significa posicao negativa ou duplicidade financeira.
/// </remarks>
public interface ILedgerStore
{
    /// <summary>
    /// Registra um lancamento de forma atomica e idempotente.
    /// </summary>
    /// <exception cref="AccountNotFoundException">A conta nao existe.</exception>
    /// <exception cref="AccountInactiveException">A conta nao aceita lancamentos.</exception>
    /// <exception cref="InsufficientFundsException">A posicao resultante seria negativa.</exception>
    /// <exception cref="IdempotencyConflictException">Chave reutilizada com conteudo diferente.</exception>
    Task<PostEntryResult> PostAsync(
        Guid accountId,
        PostingRequest request,
        ReadOnlyMemory<byte> requestHash,
        CancellationToken cancellationToken);

    /// <summary>
    /// Calcula a posicao consolidada. Com <paramref name="asOf"/> nulo,
    /// devolve a posicao corrente; com instante informado, a posicao daquele
    /// momento pela data do fato (RN-009, RN-011).
    /// </summary>
    Task<BalanceResult> GetBalanceAsync(
        Guid accountId,
        DateTimeOffset? asOf,
        CancellationToken cancellationToken);
}
