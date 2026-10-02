using Dapper;
using Npgsql;
using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Domain;
using PacioliBank.Ledger.Persistence;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Cenario F07 do BDD. O teste decisivo deste sistema.
/// </summary>
/// <remarks>
/// Criterio de qualidade do proprio teste: ele DEVE falhar contra uma
/// implementacao sem bloqueio. Um teste de concorrencia que passa nos dois
/// casos nao esta testando concorrencia, e e pior do que nao existir, porque
/// produz confianca injustificada.
/// <para>
/// Medido em 2026-10-02 (ADR-0005, "Validacao empirica do bloqueio"): sem
/// <c>FOR NO KEY UPDATE</c>, a suite reprova, mas NAO por posicao negativa. A
/// constraint de sequencia mais a nova tentativa preservam a invariante; o que
/// some e a disponibilidade, com a maior parte dos comandos esgotando as
/// tentativas. Por isso os testes afirmam a contagem exata de cada desfecho, e
/// nao apenas a posicao final: so a contagem enxerga a falta do bloqueio.
/// </para>
/// <para>
/// Por isso as tarefas sao liberadas por uma barreira comum, e nao disparadas
/// em laco: disparar em laco quase sempre serializa por acidente de
/// escalonamento, e o teste passa sem nunca ter havido concorrencia real.
/// </para>
/// </remarks>
[Collection(LedgerCollection.Name)]
public class ConcurrencyTests
{
    private readonly LedgerFixture _fixture;
    private readonly PostgresLedgerStore _store;

    public ConcurrencyTests(LedgerFixture fixture)
    {
        _fixture = fixture;
        _store = new PostgresLedgerStore(fixture.RuntimeDataSource);
    }

    [Fact]
    public async Task Debitos_simultaneos_jamais_tornam_a_posicao_negativa()
    {
        const int Tentativas = 50;
        const decimal ValorDoDebito = 10.00m;
        const decimal SaldoInicial = 100.00m;   // comporta exatamente 10 debitos

        var conta = await _fixture.CreateAccountAsync(openingCredit: SaldoInicial);

        var resultados = await DispararEmParaleloAsync(Tentativas, indice =>
            Postar(conta, EntryDirection.Debit, ValorDoDebito, $"debito-{indice}"));

        var aceitos = resultados.Count(r => r.Sucesso);
        var recusadosPorSaldo = resultados.Count(r => r.Erro is InsufficientFundsException);

        Assert.Equal(10, aceitos);
        Assert.Equal(Tentativas - 10, recusadosPorSaldo);

        var posicao = await _store.GetBalanceAsync(conta, null, CancellationToken.None);
        Assert.True(posicao.Balance.IsZero);

        // A posicao nunca pode ter sido negativa em nenhum instante: cada
        // lancamento gravou a posicao resultante no momento em que decidiu.
        var menorPosicaoRegistrada = await MenorBalanceAfterAsync(conta);
        Assert.True(menorPosicaoRegistrada >= 0m);
    }

    [Fact]
    public async Task Creditos_simultaneos_produzem_sequencia_continua_sem_perda()
    {
        // 50 e nao 100 por causa do lock_timeout de 3s: as escritas de uma mesma
        // conta serializam, e em maquina de integracao lenta uma fila de 100
        // faria a ultima estourar o tempo. O que o teste verifica (nenhuma perda,
        // nenhuma duplicata de sequencia) independe do numero.
        const int Tentativas = 50;

        var conta = await _fixture.CreateAccountAsync();

        var resultados = await DispararEmParaleloAsync(Tentativas, indice =>
            Postar(conta, EntryDirection.Credit, 1.00m, $"credito-{indice}"));

        Assert.All(resultados, r => Assert.True(r.Sucesso, r.Erro?.Message));

        var sequencias = await SequenciasAsync(conta);
        Assert.Equal(Enumerable.Range(1, Tentativas).Select(i => (long)i), sequencias);

        var posicao = await _store.GetBalanceAsync(conta, null, CancellationToken.None);
        Assert.Equal(Tentativas * 1.00m, posicao.Balance.Amount);
    }

    [Fact]
    public async Task Envios_simultaneos_com_a_mesma_chave_produzem_um_unico_lancamento()
    {
        const int Tentativas = 20;

        var conta = await _fixture.CreateAccountAsync(openingCredit: 500.00m);
        var comando = new PostingRequest(
            EntryDirection.Debit,
            Money.Of(10.00m, Currency.Brl),
            new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero),
            "chave-simultanea",
            Guid.NewGuid());

        var impressao = RequestFingerprint.Of(conta, comando);

        var resultados = await DispararEmParaleloAsync(Tentativas, async _ =>
        {
            try
            {
                var r = await _store.PostAsync(conta, comando, impressao, CancellationToken.None)
                    .ConfigureAwait(false);
                return new Resultado(true, null, r);
            }
            catch (LedgerDomainException ex)
            {
                return new Resultado(false, ex, null);
            }
        });

        Assert.All(resultados, r => Assert.True(r.Sucesso, r.Erro?.Message));

        var lancamentos = await ContarLancamentosAsync(conta);
        Assert.Equal(2, lancamentos);   // a abertura mais um unico debito

        var posicao = await _store.GetBalanceAsync(conta, null, CancellationToken.None);
        Assert.Equal(490.00m, posicao.Balance.Amount);
    }

    [Fact]
    public async Task Operacoes_em_contas_distintas_nao_se_bloqueiam()
    {
        const int Contas = 25;

        var contas = new Guid[Contas];
        for (var i = 0; i < Contas; i++)
        {
            contas[i] = await _fixture.CreateAccountAsync(openingCredit: 1000.00m);
        }

        var resultados = await DispararEmParaleloAsync(Contas, indice =>
            Postar(contas[indice], EntryDirection.Debit, 10.00m, $"paralelo-{indice}"));

        Assert.All(resultados, r => Assert.True(r.Sucesso, r.Erro?.Message));
    }

    private async Task<Resultado> Postar(Guid conta, EntryDirection sentido, decimal valor, string chave)
    {
        var comando = new PostingRequest(
            sentido,
            Money.Of(valor, Currency.Brl),
            DateTimeOffset.UtcNow,
            chave,
            Guid.NewGuid());

        try
        {
            var resultado = await _store
                .PostAsync(conta, comando, RequestFingerprint.Of(conta, comando), CancellationToken.None)
                .ConfigureAwait(false);

            return new Resultado(true, null, resultado);
        }
        catch (LedgerDomainException ex)
        {
            return new Resultado(false, ex, null);
        }
    }

    /// <summary>
    /// Libera todas as tarefas no mesmo instante. Sem a barreira, o laco de
    /// disparo serializa as chamadas e o teste perde o proposito.
    /// </summary>
    private static async Task<IReadOnlyList<Resultado>> DispararEmParaleloAsync(
        int quantidade,
        Func<int, Task<Resultado>> operacao)
    {
        using var largada = new SemaphoreSlim(0, quantidade);

        var tarefas = Enumerable.Range(0, quantidade).Select(async indice =>
        {
            await largada.WaitAsync().ConfigureAwait(false);
            return await operacao(indice).ConfigureAwait(false);
        }).ToArray();

        largada.Release(quantidade);

        return await Task.WhenAll(tarefas).ConfigureAwait(false);
    }

    private async Task<int> ContarLancamentosAsync(Guid conta)
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ledger.ledger_entries WHERE account_id = @conta",
            new { conta });
    }

    private async Task<IReadOnlyList<long>> SequenciasAsync(Guid conta)
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        var linhas = await connection.QueryAsync<long>(
            "SELECT sequence FROM ledger.ledger_entries WHERE account_id = @conta ORDER BY sequence",
            new { conta });

        return linhas.ToList();
    }

    private async Task<decimal> MenorBalanceAfterAsync(Guid conta)
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        return await connection.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(MIN(balance_after), 0) FROM ledger.ledger_entries WHERE account_id = @conta",
            new { conta });
    }

    private sealed record Resultado(bool Sucesso, LedgerDomainException? Erro, PostEntryResult? Valor);
}
