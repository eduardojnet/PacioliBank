namespace PacioliBank.Ledger.Domain;

/// <summary>
/// Comando de transferencia entre contas, ja autenticado e autorizado na borda
/// (RF-012). As contas nao viajam aqui: chegam como agregados, ja bloqueados.
/// </summary>
/// <param name="Amount">Valor, sempre positivo (RN-002), na moeda das duas contas (RN-007).</param>
/// <param name="OccurredAt">Instante do fato, o mesmo para as duas pernas (RN-009).</param>
/// <param name="IdempotencyKey">Chave do chamador, na conta de origem (RN-005).</param>
/// <param name="CorrelationId">Rastreabilidade ponta a ponta, a mesma nas duas pernas.</param>
public sealed record TransferRequest(
    Money Amount,
    DateTimeOffset OccurredAt,
    string IdempotencyKey,
    Guid CorrelationId);

/// <summary>As duas pernas de uma transferencia, decididas juntas (RN-013).</summary>
public sealed record TransferLegs(Guid TransferId, LedgerEntry Debit, LedgerEntry Credit);

/// <summary>
/// Servico de dominio da transferencia: um debito na origem e um credito no
/// destino, pelo mesmo valor (RN-013, ADR-0014).
/// </summary>
/// <remarks>
/// Cada perna e decidida pelo agregado da sua conta, com as mesmas regras de
/// um lancamento comum: nenhuma regra e reescrita aqui. O que este servico
/// acrescenta e o que so existe com duas contas: origem diferente do destino,
/// o mesmo valor, fato e correlacao nas duas pernas, e a chave da perna de
/// credito.
/// <para>
/// Atomicidade e ordem dos bloqueios nao sao decididas aqui: sao do adaptador
/// de dados, que grava as duas pernas na mesma transacao, com as duas contas
/// bloqueadas em ordem crescente de identificador (ADR-0005, revisao do card 38).
/// Rejeitada a transferencia, os dois agregados sao descartados com a transacao.
/// </para>
/// </remarks>
public static class Transfer
{
    /// <summary>
    /// Chave da perna de credito. A chave do chamador pertence a conta de
    /// origem; repeti-la no destino colidiria com as chaves do titular do
    /// destino, que sao dele. A chave derivada e unica porque a transferencia e.
    /// </summary>
    public static string CreditLegKey(Guid transferId) => "transfer:" + transferId.ToString("D");

    /// <summary>Decide as duas pernas, debito antes de credito.</summary>
    /// <exception cref="SameAccountTransferException">Origem e destino sao a mesma conta.</exception>
    /// <exception cref="AccountInactiveException">Uma das contas nao aceita lancamentos (RN-008).</exception>
    /// <exception cref="InsufficientFundsException">A origem ficaria negativa (RN-001).</exception>
    public static TransferLegs Between(Account source, Account destination, TransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(request);

        if (source.AccountId == destination.AccountId)
        {
            throw new SameAccountTransferException(source.AccountId);
        }

        var transferId = Guid.NewGuid();

        var debit = source.Post(new PostingRequest(
            EntryDirection.Debit,
            request.Amount,
            request.OccurredAt,
            request.IdempotencyKey,
            request.CorrelationId));

        var credit = destination.Post(new PostingRequest(
            EntryDirection.Credit,
            request.Amount,
            request.OccurredAt,
            CreditLegKey(transferId),
            request.CorrelationId));

        return new TransferLegs(transferId, debit, credit);
    }
}
