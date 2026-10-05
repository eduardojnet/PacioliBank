using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Domain;

namespace PacioliBank.Domain.Tests;

/// <summary>
/// Formatos de fio montados na aplicacao: o corpo da resposta de escrita
/// (EF secao 8.4), o payload dos eventos (EF secao 9) e a impressao digital do
/// comando (ADR-0006). Sem I/O: o que se verifica e o texto produzido.
/// </summary>
/// <remarks>
/// Os testes de integracao verificam que esse texto chega intacto ao banco e
/// ao evento. Aqui se verifica o proprio texto, campo a campo, para que uma
/// mudanca de formato reprove em milissegundos, e nao so com PostgreSQL no ar.
/// </remarks>
public class ContractTests
{
    private static readonly Guid Lancamento = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Original = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Conta = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Fato = new(2026, 9, 30, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Registro = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);

    private static LedgerEntry Credito(Guid? estornoDe = null) =>
        LedgerEntry.Rehydrate(
            Lancamento,
            Conta,
            sequence: 7,
            estornoDe is null ? EntryDirection.Credit : EntryDirection.Debit,
            Money.Of(150.00m, Currency.Brl),
            Fato,
            "chave-007",
            Guid.NewGuid(),
            estornoDe,
            Money.Of(250.00m, Currency.Brl));

    // ------------------------------------------------- corpo de escrita (8.4)

    [Fact]
    public void Corpo_de_escrita_tem_os_campos_e_formatos_do_contrato()
    {
        var corpo = PostingResponse.From(Credito(), Registro).ToJson();

        Assert.Equal(
            """{"entryId":"aaaaaaaa-0000-0000-0000-000000000001","accountId":"11111111-1111-1111-1111-111111111111","sequence":7,"direction":"Credit","amount":"150.00","currency":"BRL","occurredAt":"2026-09-30T13:00:00Z","recordedAt":"2026-10-02T12:00:00.1234567Z","balanceAfter":"250.00"}""",
            corpo);
    }

    [Fact]
    public void Corpo_de_estorno_informa_o_lancamento_estornado()
    {
        var corpo = PostingResponse.From(Credito(estornoDe: Original), Registro).ToJson();

        Assert.EndsWith(""","reversalOf":"aaaaaaaa-0000-0000-0000-000000000002"}""", corpo, StringComparison.Ordinal);
        Assert.Contains(""""direction":"Debit"""", corpo, StringComparison.Ordinal);
    }

    // --------------------------------------------------- eventos (EF secao 9)

    [Fact]
    public void Evento_de_lancamento_leva_os_mesmos_valores_e_formatos_da_API()
    {
        var evento = LedgerEntryEvent.From(Credito(), Registro);

        Assert.Equal(LedgerEntryEvent.RecordedType, LedgerEntryEvent.TypeOf(Credito()));
        Assert.Equal(
            new LedgerEntryEvent(
                "aaaaaaaa-0000-0000-0000-000000000001",
                "11111111-1111-1111-1111-111111111111",
                7,
                "Credit",
                "150.00",
                "BRL",
                "2026-09-30T13:00:00Z",
                "2026-10-02T12:00:00.1234567Z",
                "250.00",
                null),
            evento);
    }

    [Fact]
    public void Evento_de_estorno_tem_tipo_proprio_e_referencia_o_original()
    {
        var estorno = Credito(estornoDe: Original);

        Assert.Equal("pacioli.ledger.entry-reversed.v1", LedgerEntryEvent.TypeOf(estorno));
        Assert.Equal("aaaaaaaa-0000-0000-0000-000000000002", LedgerEntryEvent.From(estorno, Registro).ReversalOf);
    }

    // ------------------------------------------------------ instante (8.1)

    [Fact]
    public void Instante_sai_em_UTC_com_sufixo_Z_qualquer_que_seja_o_fuso_de_entrada()
    {
        var emBrasilia = new DateTimeOffset(2026, 10, 2, 9, 30, 0, TimeSpan.FromHours(-3));

        Assert.Equal("2026-10-02T12:30:00Z", WireFormat.Instant(emBrasilia));
    }

    // ----------------------------------------- impressao do comando (ADR-0006)

    private static PostingRequest Comando(decimal valor = 10.00m, EntryDirection sentido = EntryDirection.Credit) =>
        new(sentido, Money.Of(valor, Currency.Brl), Fato, "k", Guid.NewGuid());

    [Fact]
    public void Mesmo_comando_produz_a_mesma_impressao_mesmo_com_outra_correlacao()
    {
        // A correlacao muda a cada reenvio de cliente; o conteudo, nao. Se a
        // correlacao entrasse na impressao, todo reenvio viraria conflito.
        Assert.Equal(RequestFingerprint.Of(Conta, Comando()), RequestFingerprint.Of(Conta, Comando()));
    }

    [Fact]
    public void Valor_sentido_ou_conta_diferentes_produzem_impressao_diferente()
    {
        var base_ = RequestFingerprint.Of(Conta, Comando());

        Assert.NotEqual(base_, RequestFingerprint.Of(Conta, Comando(valor: 10.01m)));
        Assert.NotEqual(base_, RequestFingerprint.Of(Conta, Comando(sentido: EntryDirection.Debit)));
        Assert.NotEqual(base_, RequestFingerprint.Of(Guid.NewGuid(), Comando()));
    }

    [Fact]
    public void Impressao_de_estorno_depende_do_lancamento_estornado_e_nunca_coincide_com_a_de_lancamento()
    {
        var pedido = new ReversalRequest(Fato, "k", Guid.NewGuid());

        var estorno = RequestFingerprint.OfReversal(Conta, Original, pedido);

        Assert.Equal(estorno, RequestFingerprint.OfReversal(Conta, Original, pedido with { CorrelationId = Guid.NewGuid() }));
        Assert.NotEqual(estorno, RequestFingerprint.OfReversal(Conta, Lancamento, pedido));
        Assert.NotEqual(estorno, RequestFingerprint.Of(Conta, Comando()));
    }

    // -------------------------------------------- reidratacao do lancamento

    [Fact]
    public void Lancamento_reidratado_preserva_o_que_foi_gravado()
    {
        var correlacao = Guid.NewGuid();
        var lido = LedgerEntry.Rehydrate(
            Lancamento, Conta, 7, EntryDirection.Debit, Money.Of(150.00m, Currency.Brl),
            Fato, "chave-007", correlacao, Original, Money.Of(100.00m, Currency.Brl));

        Assert.Equal(7, lido.Sequence);
        Assert.Equal("chave-007", lido.IdempotencyKey);
        Assert.Equal(correlacao, lido.CorrelationId);
        Assert.Equal(Original, lido.ReversalOf);
        Assert.True(lido.IsReversal);
        Assert.Equal(Money.Of(100.00m, Currency.Brl), lido.BalanceAfter);
    }

    [Fact]
    public void Lancamento_gravado_sem_identificador_e_recusado_na_reidratacao()
    {
        var erro = Assert.Throws<ArgumentException>(() => LedgerEntry.Rehydrate(
            Guid.Empty, Conta, 1, EntryDirection.Credit, Money.Of(1m, Currency.Brl),
            Fato, "k", Guid.NewGuid(), null, Money.Of(1m, Currency.Brl)));

        Assert.Equal("entryId", erro.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Lancamento_gravado_com_sequencia_menor_que_um_e_recusado_na_reidratacao(long sequencia)
    {
        var erro = Assert.Throws<ArgumentOutOfRangeException>(() => LedgerEntry.Rehydrate(
            Lancamento, Conta, sequencia, EntryDirection.Credit, Money.Of(1m, Currency.Brl),
            Fato, "k", Guid.NewGuid(), null, Money.Of(1m, Currency.Brl)));

        Assert.Equal("sequence", erro.ParamName);
    }
}
