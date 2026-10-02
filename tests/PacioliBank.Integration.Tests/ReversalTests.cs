using Dapper;
using Npgsql;
using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Domain;
using PacioliBank.Ledger.Persistence;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Caminho de persistencia do estorno (lacuna L-08). Equivale a funcionalidade
/// F06 do BDD.
/// </summary>
/// <remarks>
/// Antes deste caminho, um estorno so era validado nos testes de dominio: a
/// persistencia gravava qualquer comando com <c>ReversalOf</c> preenchido sem
/// conferir titularidade nem estorno de estorno, e o estorno em duplicidade
/// saia como excecao crua do banco. Os testes abaixo fixam o comportamento
/// contra o banco real, onde a unicidade do estorno de fato reside.
/// </remarks>
[Collection(LedgerCollection.Name)]
public class ReversalTests
{
    private readonly LedgerFixture _fixture;
    private readonly PostgresLedgerStore _store;

    public ReversalTests(LedgerFixture fixture)
    {
        _fixture = fixture;
        _store = new PostgresLedgerStore(fixture.RuntimeDataSource);
    }

    private async Task<PostEntryResult> CreditarAsync(Guid conta, decimal valor, string chave)
    {
        var comando = new PostingRequest(
            EntryDirection.Credit, Money.Of(valor, Currency.Brl), DateTimeOffset.UtcNow, chave, Guid.NewGuid());

        return await _store.PostAsync(conta, comando, RequestFingerprint.Of(conta, comando), CancellationToken.None);
    }

    private async Task<PostEntryResult> DebitarAsync(Guid conta, decimal valor, string chave)
    {
        var comando = new PostingRequest(
            EntryDirection.Debit, Money.Of(valor, Currency.Brl), DateTimeOffset.UtcNow, chave, Guid.NewGuid());

        return await _store.PostAsync(conta, comando, RequestFingerprint.Of(conta, comando), CancellationToken.None);
    }

    private Task<PostEntryResult> EstornarAsync(Guid conta, Guid lancamento, string chave, DateTimeOffset? quando = null)
    {
        var comando = new ReversalRequest(quando ?? DateTimeOffset.UtcNow, chave, Guid.NewGuid());

        return _store.ReverseAsync(
            conta, lancamento, comando, RequestFingerprint.OfReversal(conta, lancamento, comando), CancellationToken.None);
    }

    private async Task<int> ContarLancamentosAsync(Guid conta)
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        return await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM ledger.ledger_entries WHERE account_id = @conta", new { conta });
    }

    [Fact]
    public async Task Estorno_gera_lancamento_compensatorio_e_preserva_o_original()
    {
        var conta = await _fixture.CreateAccountAsync();
        var original = await CreditarAsync(conta, 500.00m, "lcto-1");

        var estorno = await EstornarAsync(conta, original.EntryId, "estorno-1");

        Assert.Equal(EntryDirection.Debit, estorno.Direction);
        Assert.Equal(500.00m, estorno.Amount.Amount);
        Assert.Equal(2L, estorno.Sequence);
        Assert.Equal(0.00m, estorno.BalanceAfter.Amount);
        Assert.False(estorno.Replayed);

        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        var origem = await connection.ExecuteScalarAsync<Guid?>(
            "SELECT reversal_of FROM ledger.ledger_entries WHERE entry_id = @id", new { id = estorno.EntryId });
        var valorOriginal = await connection.ExecuteScalarAsync<decimal>(
            "SELECT amount FROM ledger.ledger_entries WHERE entry_id = @id", new { id = original.EntryId });

        Assert.Equal(original.EntryId, origem);
        Assert.Equal(500.00m, valorOriginal);
        Assert.Equal(2, await ContarLancamentosAsync(conta));

        var posicao = await _store.GetBalanceAsync(conta, null, CancellationToken.None);
        Assert.Equal(0.00m, posicao.Balance.Amount);
    }

    [Fact]
    public async Task Estorno_em_duplicidade_e_recusado_como_duplicidade_mesmo_com_a_conta_zerada()
    {
        // Contexto literal do BDD F06: o primeiro estorno zera a conta. Sem a
        // ordem correta das rejeicoes, o segundo sairia como saldo insuficiente.
        var conta = await _fixture.CreateAccountAsync();
        var original = await CreditarAsync(conta, 500.00m, "lcto-1");
        await EstornarAsync(conta, original.EntryId, "estorno-1");

        // Chave diferente: nao e repeticao, e um segundo estorno de verdade.
        await Assert.ThrowsAsync<EntryAlreadyReversedException>(
            () => EstornarAsync(conta, original.EntryId, "estorno-2"));

        Assert.Equal(2, await ContarLancamentosAsync(conta));
    }

    [Fact]
    public async Task Estorno_em_duplicidade_com_saldo_sobrando_tambem_e_recusado()
    {
        var conta = await _fixture.CreateAccountAsync();
        var original = await CreditarAsync(conta, 500.00m, "lcto-1");
        await CreditarAsync(conta, 1000.00m, "lcto-2");
        await EstornarAsync(conta, original.EntryId, "estorno-1");

        await Assert.ThrowsAsync<EntryAlreadyReversedException>(
            () => EstornarAsync(conta, original.EntryId, "estorno-2"));

        Assert.Equal(3, await ContarLancamentosAsync(conta));
    }

    [Fact]
    public async Task Reenvio_do_estorno_que_zerou_a_conta_devolve_o_resultado_original()
    {
        // A repeticao tem precedencia sobre a rejeicao por duplicidade: e o
        // mesmo comando, nao um segundo estorno.
        var conta = await _fixture.CreateAccountAsync();
        var original = await CreditarAsync(conta, 500.00m, "lcto-1");
        var quando = DateTimeOffset.UtcNow;

        var primeiro = await EstornarAsync(conta, original.EntryId, "estorno-1", quando);
        var segundo = await EstornarAsync(conta, original.EntryId, "estorno-1", quando);

        Assert.True(segundo.Replayed);
        Assert.Equal(primeiro with { Replayed = true }, segundo);
    }

    [Fact]
    public async Task Estorno_de_estorno_e_recusado()
    {
        var conta = await _fixture.CreateAccountAsync();
        var original = await CreditarAsync(conta, 500.00m, "lcto-1");
        var estorno = await EstornarAsync(conta, original.EntryId, "estorno-1");

        await Assert.ThrowsAsync<CannotReverseReversalException>(
            () => EstornarAsync(conta, estorno.EntryId, "estorno-2"));

        Assert.Equal(2, await ContarLancamentosAsync(conta));
    }

    [Fact]
    public async Task Estorno_de_lancamento_de_outra_conta_e_recusado()
    {
        var contaA = await _fixture.CreateAccountAsync();
        var contaB = await _fixture.CreateAccountAsync(openingCredit: 1000.00m);
        var deOutraConta = await CreditarAsync(contaA, 500.00m, "lcto-1");

        await Assert.ThrowsAsync<EntryNotFromThisAccountException>(
            () => EstornarAsync(contaB, deOutraConta.EntryId, "estorno-1"));

        Assert.Equal(1, await ContarLancamentosAsync(contaB));
    }

    [Fact]
    public async Task Estorno_de_lancamento_inexistente_e_recusado()
    {
        var conta = await _fixture.CreateAccountAsync(openingCredit: 100.00m);

        await Assert.ThrowsAsync<EntryNotFoundException>(
            () => EstornarAsync(conta, Guid.NewGuid(), "estorno-1"));
    }

    [Fact]
    public async Task Estorno_de_credito_ja_gasto_e_recusado_por_saldo_insuficiente()
    {
        var conta = await _fixture.CreateAccountAsync();
        var original = await CreditarAsync(conta, 500.00m, "lcto-1");
        await DebitarAsync(conta, 400.00m, "lcto-2");

        // QA-003 decidido: o estorno respeita RN-001.
        await Assert.ThrowsAsync<InsufficientFundsException>(
            () => EstornarAsync(conta, original.EntryId, "estorno-1"));

        Assert.Equal(2, await ContarLancamentosAsync(conta));
    }

    [Fact]
    public async Task Reenvio_do_mesmo_estorno_devolve_o_resultado_original()
    {
        var conta = await _fixture.CreateAccountAsync(openingCredit: 1000.00m);
        var original = await CreditarAsync(conta, 500.00m, "lcto-1");
        var quando = DateTimeOffset.UtcNow;

        var primeiro = await EstornarAsync(conta, original.EntryId, "estorno-1", quando);
        var segundo = await EstornarAsync(conta, original.EntryId, "estorno-1", quando);

        Assert.False(primeiro.Replayed);
        Assert.True(segundo.Replayed);
        Assert.Equal(primeiro.EntryId, segundo.EntryId);
        Assert.Equal(3, await ContarLancamentosAsync(conta));
    }

    [Fact]
    public async Task Mesma_chave_em_estorno_com_conteudo_diferente_e_recusada()
    {
        var conta = await _fixture.CreateAccountAsync(openingCredit: 1000.00m);
        var original = await CreditarAsync(conta, 500.00m, "lcto-1");

        await EstornarAsync(conta, original.EntryId, "chave-x", DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => EstornarAsync(conta, original.EntryId, "chave-x", DateTimeOffset.UtcNow.AddMinutes(-5)));
    }

    [Fact]
    public async Task Estornos_simultaneos_do_mesmo_lancamento_produzem_exatamente_um()
    {
        const int Tentativas = 10;

        var conta = await _fixture.CreateAccountAsync(openingCredit: 10_000.00m);
        var original = await CreditarAsync(conta, 500.00m, "lcto-1");

        using var largada = new Barrier(Tentativas);

        var tarefas = Enumerable.Range(0, Tentativas).Select(i => Task.Run(async () =>
        {
            largada.SignalAndWait();
            try
            {
                await EstornarAsync(conta, original.EntryId, $"estorno-{i}");
                return true;
            }
            catch (EntryAlreadyReversedException)
            {
                return false;
            }
        }));

        var resultados = await Task.WhenAll(tarefas);

        Assert.Equal(1, resultados.Count(aceito => aceito));
        Assert.Equal(3, await ContarLancamentosAsync(conta));

        var posicao = await _store.GetBalanceAsync(conta, null, CancellationToken.None);
        Assert.Equal(10_000.00m, posicao.Balance.Amount);
    }
}
