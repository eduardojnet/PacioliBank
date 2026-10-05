using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Domain;

namespace PacioliBank.Domain.Tests;

/// <summary>
/// Validacoes da porta de entrada que nao dependem de estado (lacuna L-03).
/// </summary>
/// <remarks>
/// O store aqui e um registrador, nao um repositorio em memoria: ele nao
/// simula nenhuma invariante de persistencia, so registra se foi chamado. As
/// invariantes que dependem do banco sao verificadas contra PostgreSQL real
/// (ADR-0010). O que se verifica aqui e que a rejeicao acontece ANTES de
/// qualquer I/O, e com a excecao que a borda traduz no codigo contratual.
/// </remarks>
public class LedgerServiceTests
{
    private static readonly Guid Conta = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Agora = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly RecordingStore _store = new();
    private readonly LedgerService _service;

    public LedgerServiceTests()
    {
        _service = new LedgerService(_store, new FixedClock(Agora));
    }

    private static PostingCommand Credito(string valor = "100.00", string moeda = "BRL", string? chave = "k1") =>
        new(Conta, EntryDirection.Credit, valor, moeda, Agora, chave, Guid.NewGuid());

    // ---------------------------------------------------------------- RN-005

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Comando_sem_chave_de_idempotencia_e_recusado_antes_de_qualquer_IO(string? chave)
    {
        await Assert.ThrowsAsync<MissingIdempotencyKeyException>(
            () => _service.PostAsync(Credito(chave: chave), CancellationToken.None));

        Assert.False(_store.Called);
    }

    [Fact]
    public async Task Estorno_sem_chave_de_idempotencia_e_recusado_antes_de_qualquer_IO()
    {
        await Assert.ThrowsAsync<MissingIdempotencyKeyException>(
            () => _service.ReverseAsync(new ReversalCommand(Conta, Guid.NewGuid(), Agora, null, Guid.NewGuid()), CancellationToken.None));

        Assert.False(_store.Called);
    }

    // ---------------------------------------------------------------- RN-002, RN-007

    [Theory]
    [InlineData("abc")]
    [InlineData("1,50")]
    [InlineData("")]
    [InlineData("99999999999999999999999999999999")]
    public async Task Valor_em_formato_invalido_e_recusado_como_valor_invalido(string valor)
    {
        await Assert.ThrowsAsync<InvalidEntryAmountException>(
            () => _service.PostAsync(Credito(valor: valor), CancellationToken.None));

        Assert.False(_store.Called);
    }

    [Fact]
    public async Task Valor_com_mais_casas_que_a_moeda_e_recusado()
    {
        await Assert.ThrowsAsync<InvalidMoneyScaleException>(
            () => _service.PostAsync(Credito(valor: "10.001"), CancellationToken.None));
    }

    [Fact]
    public async Task Moeda_fora_do_catalogo_e_recusada()
    {
        await Assert.ThrowsAsync<UnsupportedCurrencyException>(
            () => _service.PostAsync(Credito(moeda: "USD"), CancellationToken.None));
    }

    [Fact]
    public async Task Mesma_quantia_escrita_de_formas_diferentes_produz_a_mesma_impressao()
    {
        await _service.PostAsync(Credito(valor: "100"), CancellationToken.None);
        var primeira = _store.LastHash;

        await _service.PostAsync(Credito(valor: "100.00"), CancellationToken.None);

        // ADR-0006: diferenca de serializacao nao pode virar conflito de chave.
        Assert.Equal(primeira, _store.LastHash);
    }

    // ---------------------------------------------------------------- RF-004

    [Fact]
    public async Task Posicao_em_instante_futuro_e_recusada()
    {
        await Assert.ThrowsAsync<InvalidPointInTimeException>(
            () => _service.GetBalanceAsync(Conta, Agora.AddSeconds(1), CancellationToken.None));

        Assert.False(_store.Called);
    }

    [Fact]
    public async Task Posicao_no_instante_presente_e_aceita()
    {
        await _service.GetBalanceAsync(Conta, Agora, CancellationToken.None);

        Assert.True(_store.Called);
    }

    // ---------------------------------------------------------------- RF-005

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_000)]
    public async Task Limite_de_pagina_fora_da_faixa_e_recusado(int limite)
    {
        await Assert.ThrowsAsync<PageSizeExceededException>(
            () => _service.GetStatementAsync(new StatementQuery(Conta, null, null, null, limite), CancellationToken.None));

        Assert.False(_store.Called);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(LedgerService.MaxPageSize)]
    public async Task Limites_da_faixa_de_pagina_sao_aceitos(int limite)
    {
        await _service.GetStatementAsync(new StatementQuery(Conta, null, null, null, limite), CancellationToken.None);

        Assert.Equal(limite, _store.LastLimit);
    }

    [Fact]
    public async Task Limite_acima_do_maximo_informa_o_pedido_e_o_maximo()
    {
        var erro = await Assert.ThrowsAsync<PageSizeExceededException>(
            () => _service.GetStatementAsync(new StatementQuery(Conta, null, null, null, LedgerService.MaxPageSize + 1), CancellationToken.None));

        Assert.Equal(LedgerService.MaxPageSize + 1, erro.Requested);
        Assert.Equal(LedgerService.MaxPageSize, erro.Maximum);
    }

    [Fact]
    public async Task Cursor_chega_ao_armazenamento_e_a_primeira_pagina_comeca_do_zero()
    {
        await _service.GetStatementAsync(new StatementQuery(Conta, null, null, 42, null), CancellationToken.None);
        Assert.Equal(42, _store.LastAfterSequence);

        await _service.GetStatementAsync(new StatementQuery(Conta, null, null, null, null), CancellationToken.None);
        Assert.Equal(0, _store.LastAfterSequence);
    }

    [Fact]
    public async Task Periodo_de_um_unico_instante_e_aceito()
    {
        // Limites inclusivos (RN-011): inicio igual ao fim e um periodo valido.
        await _service.GetStatementAsync(new StatementQuery(Conta, Agora, Agora, null, null), CancellationToken.None);

        Assert.True(_store.Called);
    }

    [Fact]
    public async Task Limite_ausente_usa_o_padrao()
    {
        await _service.GetStatementAsync(new StatementQuery(Conta, null, null, null, null), CancellationToken.None);

        Assert.Equal(LedgerService.DefaultPageSize, _store.LastLimit);
    }

    [Fact]
    public async Task Periodo_invertido_e_recusado()
    {
        await Assert.ThrowsAsync<InvalidPointInTimeException>(
            () => _service.GetStatementAsync(
                new StatementQuery(Conta, Agora, Agora.AddDays(-1), null, null), CancellationToken.None));
    }

    // ---------------------------------------------------------------- RF-007

    [Fact]
    public async Task Estorno_chega_ao_armazenamento_com_a_impressao_de_estorno()
    {
        var lancamento = Guid.NewGuid();
        var comando = new ReversalCommand(Conta, lancamento, Agora, "estorno-1", Guid.NewGuid());

        await _service.ReverseAsync(comando, CancellationToken.None);

        Assert.Equal(new ReversalRequest(Agora, "estorno-1", comando.CorrelationId), _store.LastReversal);
        Assert.Equal(RequestFingerprint.OfReversal(Conta, lancamento, _store.LastReversal!), _store.LastHash);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Registra a chamada e devolve o minimo. Nao simula persistencia.</summary>
    private sealed class RecordingStore : ILedgerStore
    {
        public bool Called { get; private set; }

        public byte[]? LastHash { get; private set; }

        public int LastLimit { get; private set; }

        public long LastAfterSequence { get; private set; } = -1;

        public Task<PostEntryResult> PostAsync(Guid accountId, PostingRequest request, ReadOnlyMemory<byte> requestHash, CancellationToken cancellationToken)
        {
            Called = true;
            LastHash = requestHash.ToArray();
            return Task.FromResult(new PostEntryResult(
                Guid.NewGuid(), accountId, 1, request.Direction, request.Amount, request.OccurredAt, Agora, request.Amount, false));
        }

        public ReversalRequest? LastReversal { get; private set; }

        public Task<PostEntryResult> ReverseAsync(Guid accountId, Guid entryId, ReversalRequest request, ReadOnlyMemory<byte> requestHash, CancellationToken cancellationToken)
        {
            Called = true;
            LastHash = requestHash.ToArray();
            LastReversal = request;
            var valor = Money.Of(10.00m, Currency.Brl);
            return Task.FromResult(new PostEntryResult(
                Guid.NewGuid(), accountId, 2, EntryDirection.Debit, valor, request.OccurredAt, Agora, Money.Zero(Currency.Brl), false));
        }

        public Task<BalanceResult> GetBalanceAsync(Guid accountId, DateTimeOffset? asOf, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(new BalanceResult(accountId, Money.Zero(Currency.Brl), asOf ?? Agora, 0, BalanceSource.Ledger, 0));
        }

        public Task<StatementPage> GetStatementAsync(Guid accountId, DateTimeOffset? from, DateTimeOffset? until, long afterSequence, int limit, CancellationToken cancellationToken)
        {
            Called = true;
            LastLimit = limit;
            LastAfterSequence = afterSequence;
            return Task.FromResult(new StatementPage(accountId, [], null));
        }
    }
}
