using PacioliBank.Ledger.Domain;

namespace PacioliBank.Ledger.Application;

/// <summary>
/// Comando de credito ou debito, no formato em que chega da borda.
/// </summary>
/// <remarks>
/// O valor chega como o numero exato do contrato (EF secao 8.2), e a moeda
/// como texto. A conversao para <see cref="Money"/>, com a escala da moeda,
/// acontece na porta de entrada, e nao no adaptador HTTP, para que a regra de
/// formato monetario e o codigo de erro correspondente (RN-002) tenham um
/// unico dono.
/// </remarks>
/// <param name="IdempotencyKey">Nulo ou vazio e rejeitado com RN-005, antes de qualquer I/O.</param>
public sealed record PostingCommand(
    Guid AccountId,
    EntryDirection Direction,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAt,
    string? IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// Comando de estorno (RF-007). Sentido e valor nao fazem parte do comando:
/// derivam do lancamento original (RN-004).
/// </summary>
public sealed record ReversalCommand(
    Guid AccountId,
    Guid EntryId,
    DateTimeOffset OccurredAt,
    string? IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// Comando de transferencia entre contas (RF-012), no formato em que chega da
/// borda. Valor e moeda pela mesma razao do <see cref="PostingCommand"/>.
/// </summary>
/// <param name="IdempotencyKey">Chave na conta de origem. Nulo ou vazio e rejeitado com RN-005, antes de qualquer I/O.</param>
public sealed record TransferCommand(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAt,
    string? IdempotencyKey,
    Guid CorrelationId);

/// <summary>
/// Consulta de extrato (RF-005), paginada por cursor.
/// </summary>
/// <param name="From">Limite inferior inclusivo, pela data do fato.</param>
/// <param name="To">Limite superior inclusivo, pela data do fato (RN-011).</param>
/// <param name="AfterSequence">
/// Cursor: devolve apenas lancamentos de sequencia maior. Nulo na primeira pagina.
/// </param>
/// <param name="Limit">Nulo usa o padrao da porta de entrada.</param>
public sealed record StatementQuery(
    Guid AccountId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    long? AfterSequence,
    int? Limit);

/// <summary>Linha do extrato, com o saldo progressivo materializado na gravacao.</summary>
public sealed record StatementEntry(
    Guid EntryId,
    long Sequence,
    EntryDirection Direction,
    Money Amount,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    Money BalanceAfter,
    Guid? ReversalOf);

/// <summary>
/// Pagina do extrato, em ordem crescente de sequencia.
/// </summary>
/// <param name="NextAfterSequence">
/// Cursor da proxima pagina, ou nulo quando esta e a ultima. A sequencia e
/// estavel e sem lacunas (RN-006), entao serve de cursor sem repeticao nem
/// omissao, mesmo com lancamentos novos chegando entre uma pagina e outra.
/// </param>
public sealed record StatementPage(
    Guid AccountId,
    IReadOnlyList<StatementEntry> Entries,
    long? NextAfterSequence);
