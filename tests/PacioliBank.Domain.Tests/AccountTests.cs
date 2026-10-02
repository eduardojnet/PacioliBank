using PacioliBank.Ledger.Domain;

namespace PacioliBank.Domain.Tests;

/// <summary>
/// Cobre RN-001, RN-002, RN-004, RN-005, RN-006, RN-007 e RN-008.
/// Equivale as funcionalidades F01, F02 e F06 do BDD, na camada de unidade:
/// aqui se verifica a decisao do agregado, nao a serializacao sob concorrencia,
/// que exige banco real (ADR-0010).
/// </summary>
public class AccountTests
{
    private static readonly Guid ContaA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ContaB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Titular = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly DateTimeOffset Agora = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static Account Conta(
        decimal saldo = 0m,
        AccountStatus status = AccountStatus.Active,
        long ultimaSequencia = 0,
        Guid? accountId = null) =>
        Account.Rehydrate(
            accountId ?? ContaA,
            Titular,
            Currency.Brl,
            status,
            ultimaSequencia,
            Money.Of(saldo, Currency.Brl));

    private static PostingRequest Comando(
        EntryDirection sentido,
        decimal valor,
        string chave = "chave-001",
        DateTimeOffset? quando = null) =>
        new(sentido, Money.Of(valor, Currency.Brl), quando ?? Agora, chave, Guid.NewGuid());

    // ---------------------------------------------------------------- F01

    [Fact]
    public void Credito_aumenta_a_posicao_e_recebe_a_primeira_sequencia()
    {
        var conta = Conta(saldo: 0m);

        var lancamento = conta.Post(Comando(EntryDirection.Credit, 150.00m));

        Assert.Equal(1L, lancamento.Sequence);
        Assert.Equal(150.00m, lancamento.BalanceAfter.Amount);
        Assert.Equal(150.00m, conta.CurrentBalance.Amount);
        Assert.False(lancamento.IsReversal);
    }

    [Fact]
    public void Creditos_sucessivos_recebem_sequencia_monotonica_sem_lacunas()
    {
        var conta = Conta(saldo: 0m);

        var a = conta.Post(Comando(EntryDirection.Credit, 100.00m, "k1"));
        var b = conta.Post(Comando(EntryDirection.Credit, 250.50m, "k2"));
        var c = conta.Post(Comando(EntryDirection.Credit, 0.01m, "k3"));

        Assert.Equal(new long[] { 1, 2, 3 }, new[] { a.Sequence, b.Sequence, c.Sequence });
        Assert.Equal(350.51m, conta.CurrentBalance.Amount);
    }

    [Fact]
    public void Lancamento_em_conta_reidratada_continua_a_sequencia_existente()
    {
        var conta = Conta(saldo: 500.00m, ultimaSequencia: 1041);

        var lancamento = conta.Post(Comando(EntryDirection.Credit, 10.00m));

        Assert.Equal(1042L, lancamento.Sequence);
    }

    [Theory]
    [InlineData(AccountStatus.Blocked)]
    [InlineData(AccountStatus.Closed)]
    public void Conta_nao_ativa_recusa_lancamento(AccountStatus status)
    {
        var conta = Conta(saldo: 1000.00m, status: status);

        var erro = Assert.Throws<AccountInactiveException>(
            () => conta.Post(Comando(EntryDirection.Credit, 10.00m)));

        Assert.Equal(ContaA, erro.AccountId);
    }

    [Fact]
    public void Valor_zero_e_recusado()
    {
        var conta = Conta(saldo: 100.00m);

        Assert.Throws<InvalidEntryAmountException>(
            () => conta.Post(Comando(EntryDirection.Credit, 0m)));
    }

    [Fact]
    public void Comando_sem_chave_de_idempotencia_e_recusado()
    {
        var conta = Conta(saldo: 100.00m);

        Assert.Throws<MissingIdempotencyKeyException>(
            () => conta.Post(Comando(EntryDirection.Credit, 10.00m, chave: "   ")));
    }

    // ---------------------------------------------------------------- F02

    [Fact]
    public void Debito_dentro_do_saldo_e_aceito()
    {
        var conta = Conta(saldo: 1000.00m);

        var lancamento = conta.Post(Comando(EntryDirection.Debit, 300.00m));

        Assert.Equal(700.00m, lancamento.BalanceAfter.Amount);
        Assert.Equal(700.00m, conta.CurrentBalance.Amount);
    }

    [Fact]
    public void Debito_igual_ao_saldo_zera_a_conta_e_e_aceito()
    {
        var conta = Conta(saldo: 1000.00m);

        var lancamento = conta.Post(Comando(EntryDirection.Debit, 1000.00m));

        Assert.True(lancamento.BalanceAfter.IsZero);
    }

    [Fact]
    public void Debito_de_um_centavo_acima_do_saldo_e_recusado()
    {
        var conta = Conta(saldo: 1000.00m);

        var erro = Assert.Throws<InsufficientFundsException>(
            () => conta.Post(Comando(EntryDirection.Debit, 1000.01m)));

        Assert.Equal(1000.00m, erro.AvailableBalance);
        Assert.Equal(1000.01m, erro.RequestedAmount);
    }

    [Fact]
    public void Rejeicao_por_saldo_insuficiente_nao_consome_sequencia()
    {
        // RN-006: lacuna na serie inviabilizaria detectar perda de registro em auditoria.
        var conta = Conta(saldo: 1000.00m);

        Assert.Throws<InsufficientFundsException>(
            () => conta.Post(Comando(EntryDirection.Debit, 5000.00m, "k1")));

        var seguinte = conta.Post(Comando(EntryDirection.Debit, 100.00m, "k2"));

        Assert.Equal(1L, seguinte.Sequence);
        Assert.Equal(900.00m, conta.CurrentBalance.Amount);
    }

    [Fact]
    public void Rejeicao_nao_altera_a_posicao_da_conta()
    {
        var conta = Conta(saldo: 1000.00m);

        Assert.Throws<InsufficientFundsException>(
            () => conta.Post(Comando(EntryDirection.Debit, 5000.00m)));

        Assert.Equal(1000.00m, conta.CurrentBalance.Amount);
        Assert.Equal(0L, conta.LastSequence);
    }

    // ---------------------------------------------------------------- F06

    [Fact]
    public void Estorno_gera_lancamento_compensatorio_que_referencia_o_original()
    {
        var conta = Conta(saldo: 0m);
        var original = conta.Post(Comando(EntryDirection.Credit, 500.00m, "k1"));

        var estorno = conta.Reverse(original, Agora, "k2", Guid.NewGuid());

        Assert.Equal(EntryDirection.Debit, estorno.Direction);
        Assert.Equal(500.00m, estorno.Amount.Amount);
        Assert.True(estorno.IsReversal);
        Assert.Equal(original.EntryId, estorno.ReversalOf!.Value);
        Assert.Equal(2L, estorno.Sequence);
        Assert.True(conta.CurrentBalance.IsZero);
    }

    [Fact]
    public void Estorno_de_estorno_e_recusado()
    {
        var conta = Conta(saldo: 0m);
        var original = conta.Post(Comando(EntryDirection.Credit, 500.00m, "k1"));
        var estorno = conta.Reverse(original, Agora, "k2", Guid.NewGuid());

        var erro = Assert.Throws<CannotReverseReversalException>(
            () => conta.Reverse(estorno, Agora, "k3", Guid.NewGuid()));

        Assert.Equal(estorno.EntryId, erro.EntryId);
    }

    [Fact]
    public void Estorno_de_lancamento_de_outra_conta_e_recusado()
    {
        var contaA = Conta(saldo: 0m, accountId: ContaA);
        var contaB = Conta(saldo: 0m, accountId: ContaB);
        var lancamentoDeA = contaA.Post(Comando(EntryDirection.Credit, 100.00m, "k1"));

        Assert.Throws<EntryNotFromThisAccountException>(
            () => contaB.Reverse(lancamentoDeA, Agora, "k2", Guid.NewGuid()));
    }

    [Fact]
    public void Estorno_de_credito_ja_gasto_e_recusado_por_saldo_insuficiente()
    {
        // QA-003 decidido: o estorno respeita RN-001 e nao negativa a conta.
        var conta = Conta(saldo: 0m);
        var credito = conta.Post(Comando(EntryDirection.Credit, 500.00m, "k1"));
        conta.Post(Comando(EntryDirection.Debit, 400.00m, "k2"));

        Assert.Throws<InsufficientFundsException>(
            () => conta.Reverse(credito, Agora, "k3", Guid.NewGuid()));

        Assert.Equal(100.00m, conta.CurrentBalance.Amount);
    }

    [Fact]
    public void Estorno_de_debito_devolve_o_valor_a_conta()
    {
        var conta = Conta(saldo: 1000.00m);
        var debito = conta.Post(Comando(EntryDirection.Debit, 250.00m, "k1"));

        var estorno = conta.Reverse(debito, Agora, "k2", Guid.NewGuid());

        Assert.Equal(EntryDirection.Credit, estorno.Direction);
        Assert.Equal(1000.00m, conta.CurrentBalance.Amount);
    }

    // ------------------------------------------------------- Moeda e sinal

    [Fact]
    public void Posicao_em_moeda_divergente_impede_a_reidratacao()
    {
        Assert.Throws<CurrencyMismatchException>(() => Account.Rehydrate(
            ContaA, Titular, Currency.Brl, AccountStatus.Active, 0, default));
    }

    [Fact]
    public void Valor_com_sinal_reflete_o_sentido_do_lancamento()
    {
        var conta = Conta(saldo: 1000.00m);

        var credito = conta.Post(Comando(EntryDirection.Credit, 100.00m, "k1"));
        var debito = conta.Post(Comando(EntryDirection.Debit, 100.00m, "k2"));

        Assert.Equal(100.00m, credito.SignedAmount.Amount);
        Assert.Equal(-100.00m, debito.SignedAmount.Amount);
    }

    [Fact]
    public void Sequencia_negativa_na_reidratacao_e_recusada()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Account.Rehydrate(
            ContaA, Titular, Currency.Brl, AccountStatus.Active, -1, Money.Zero(Currency.Brl)));
    }

    [Fact]
    public void Instante_do_fato_e_preservado_no_lancamento()
    {
        // RN-012: occurred_at pode ser anterior ao registro, e o dominio aceita.
        var conta = Conta(saldo: 0m);
        var retroativo = new DateTimeOffset(2026, 1, 12, 8, 0, 0, TimeSpan.Zero);

        var lancamento = conta.Post(Comando(EntryDirection.Credit, 300.00m, quando: retroativo));

        Assert.Equal(retroativo, lancamento.OccurredAt);
    }
}
