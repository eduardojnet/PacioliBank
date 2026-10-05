using PacioliBank.Ledger.Domain;

namespace PacioliBank.Domain.Tests;

/// <summary>
/// Transferencia entre contas (RF-012, RN-013, ADR-0014), na camada de unidade:
/// a decisao sobre as duas pernas. Atomicidade, ordem dos bloqueios e
/// amarracao das pernas pelo banco exigem PostgreSQL real (ADR-0010) e estao
/// nos testes de integracao.
/// </summary>
public class TransferTests
{
    private static readonly Guid Origem = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Destino = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Fato = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static Account Conta(Guid id, decimal saldo, long ultimaSequencia = 0, AccountStatus status = AccountStatus.Active) =>
        Account.Rehydrate(id, Guid.NewGuid(), Currency.Brl, status, ultimaSequencia, Money.Of(saldo, Currency.Brl));

    private static TransferRequest Pedido(decimal valor = 40.00m, string chave = "tr-1") =>
        new(Money.Of(valor, Currency.Brl), Fato, chave, Guid.NewGuid());

    [Fact]
    public void Transferencia_debita_a_origem_e_credita_o_destino_pelo_mesmo_valor()
    {
        var origem = Conta(Origem, 100.00m, ultimaSequencia: 3);
        var destino = Conta(Destino, 5.00m, ultimaSequencia: 8);

        var pernas = Transfer.Between(origem, destino, Pedido(40.00m));

        Assert.Equal(Origem, pernas.Debit.AccountId);
        Assert.Equal(EntryDirection.Debit, pernas.Debit.Direction);
        Assert.Equal(40.00m, pernas.Debit.Amount.Amount);
        Assert.Equal(4L, pernas.Debit.Sequence);
        Assert.Equal(60.00m, pernas.Debit.BalanceAfter.Amount);

        Assert.Equal(Destino, pernas.Credit.AccountId);
        Assert.Equal(EntryDirection.Credit, pernas.Credit.Direction);
        Assert.Equal(40.00m, pernas.Credit.Amount.Amount);
        Assert.Equal(9L, pernas.Credit.Sequence);
        Assert.Equal(45.00m, pernas.Credit.BalanceAfter.Amount);

        Assert.Equal(60.00m, origem.CurrentBalance.Amount);
        Assert.Equal(45.00m, destino.CurrentBalance.Amount);
    }

    [Fact]
    public void As_duas_pernas_tem_o_mesmo_fato_e_a_mesma_correlacao()
    {
        var pedido = Pedido();

        var pernas = Transfer.Between(Conta(Origem, 100.00m), Conta(Destino, 0m), pedido);

        Assert.Equal(Fato, pernas.Debit.OccurredAt);
        Assert.Equal(Fato, pernas.Credit.OccurredAt);
        Assert.Equal(pedido.CorrelationId, pernas.Debit.CorrelationId);
        Assert.Equal(pedido.CorrelationId, pernas.Credit.CorrelationId);
        Assert.False(pernas.Debit.IsReversal);
        Assert.False(pernas.Credit.IsReversal);
    }

    [Fact]
    public void A_chave_do_chamador_fica_na_perna_de_debito_e_a_de_credito_deriva_da_transferencia()
    {
        // A chave do chamador e da conta de origem. Repeti-la no destino
        // colidiria com as chaves do titular do destino, que sao dele.
        var pernas = Transfer.Between(Conta(Origem, 100.00m), Conta(Destino, 0m), Pedido(chave: "minha-chave"));

        Assert.Equal("minha-chave", pernas.Debit.IdempotencyKey);
        Assert.Equal("transfer:" + pernas.TransferId.ToString("D"), pernas.Credit.IdempotencyKey);
        Assert.Equal(Transfer.CreditLegKey(pernas.TransferId), pernas.Credit.IdempotencyKey);
    }

    [Fact]
    public void Cada_transferencia_e_cada_perna_tem_identificador_proprio()
    {
        var a = Transfer.Between(Conta(Origem, 100.00m), Conta(Destino, 0m), Pedido(10.00m, "a"));
        var b = Transfer.Between(Conta(Origem, 100.00m), Conta(Destino, 0m), Pedido(10.00m, "b"));

        Assert.NotEqual(Guid.Empty, a.TransferId);
        Assert.NotEqual(a.TransferId, b.TransferId);
        Assert.NotEqual(a.Debit.EntryId, a.Credit.EntryId);
    }

    [Fact]
    public void Transferencia_para_a_propria_conta_e_recusada()
    {
        var erro = Assert.Throws<SameAccountTransferException>(
            () => Transfer.Between(Conta(Origem, 100.00m), Conta(Origem, 100.00m), Pedido()));

        Assert.Equal(Origem, erro.AccountId);
    }

    [Fact]
    public void Saldo_insuficiente_na_origem_recusa_a_transferencia()
    {
        var erro = Assert.Throws<InsufficientFundsException>(
            () => Transfer.Between(Conta(Origem, 39.99m), Conta(Destino, 0m), Pedido(40.00m)));

        Assert.Equal(39.99m, erro.AvailableBalance);
        Assert.Equal(40.00m, erro.RequestedAmount);
    }

    [Fact]
    public void Saldo_exato_na_origem_e_aceito_e_zera_a_origem()
    {
        var pernas = Transfer.Between(Conta(Origem, 40.00m), Conta(Destino, 0m), Pedido(40.00m));

        Assert.True(pernas.Debit.BalanceAfter.IsZero);
    }

    [Theory]
    [InlineData(AccountStatus.Blocked)]
    [InlineData(AccountStatus.Closed)]
    public void Destino_que_nao_aceita_lancamentos_recusa_a_transferencia(AccountStatus status)
    {
        var erro = Assert.Throws<AccountInactiveException>(
            () => Transfer.Between(Conta(Origem, 100.00m), Conta(Destino, 0m, status: status), Pedido()));

        Assert.Equal(Destino, erro.AccountId);
    }

    [Fact]
    public void Origem_que_nao_aceita_lancamentos_recusa_a_transferencia()
    {
        var erro = Assert.Throws<AccountInactiveException>(
            () => Transfer.Between(Conta(Origem, 100.00m, status: AccountStatus.Blocked), Conta(Destino, 0m), Pedido()));

        Assert.Equal(Origem, erro.AccountId);
    }

    [Fact]
    public void Valor_nao_positivo_e_recusado()
    {
        Assert.Throws<InvalidEntryAmountException>(
            () => Transfer.Between(Conta(Origem, 100.00m), Conta(Destino, 0m), Pedido(0m)));
    }

    [Fact]
    public void Chave_de_idempotencia_ausente_e_recusada()
    {
        Assert.Throws<MissingIdempotencyKeyException>(
            () => Transfer.Between(Conta(Origem, 100.00m), Conta(Destino, 0m), Pedido(chave: " ")));
    }
}
