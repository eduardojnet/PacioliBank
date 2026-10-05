namespace PacioliBank.Ledger.Application;

/// <summary>
/// Porta de entrada do ledger: o lado dirigente do hexagono (lacuna L-03).
/// </summary>
/// <remarks>
/// Todo adaptador de entrada (HTTP hoje, consumidor de fila amanha) chama esta
/// porta, nunca o <see cref="ILedgerStore"/> diretamente. O que fica aqui, e
/// nao no adaptador: conversao do valor monetario, calculo da impressao do
/// comando (ADR-0006), validacao de instante e de tamanho de pagina. Com isso
/// o adaptador HTTP se limita a traduzir protocolo, e uma segunda borda nao
/// precisa reimplementar regra.
/// </remarks>
public interface ILedgerService
{
    /// <summary>Registra credito ou debito (RF-001, RF-002).</summary>
    Task<PostEntryResult> PostAsync(PostingCommand command, CancellationToken cancellationToken);

    /// <summary>Estorna um lancamento (RF-007).</summary>
    Task<PostEntryResult> ReverseAsync(ReversalCommand command, CancellationToken cancellationToken);

    /// <summary>Transfere entre duas contas (RF-012).</summary>
    Task<TransferResult> TransferAsync(TransferCommand command, CancellationToken cancellationToken);

    /// <summary>Posicao corrente, ou no instante informado (RF-003, RF-004).</summary>
    Task<BalanceResult> GetBalanceAsync(Guid accountId, DateTimeOffset? asOf, CancellationToken cancellationToken);

    /// <summary>Extrato paginado por cursor (RF-005).</summary>
    Task<StatementPage> GetStatementAsync(StatementQuery query, CancellationToken cancellationToken);
}
