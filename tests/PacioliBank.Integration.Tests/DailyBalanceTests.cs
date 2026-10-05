using Dapper;
using Npgsql;
using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Domain;
using PacioliBank.Ledger.Persistence;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Fechamento diario para a posicao em instante passado (RF-004, ADR-0007,
/// card 32). PostgreSQL real.
/// </summary>
/// <remarks>
/// O fechamento e derivado: o ledger continua sendo a unica fonte da verdade
/// (ADR-0003). Por isso todo valor e conferido contra a soma do ledger
/// calculada por fora, nunca contra o proprio fechamento. Os lancamentos
/// retroativos chegam DEPOIS dos posteriores, que e o caso que obriga a
/// corrigir os fechamentos seguintes.
/// </remarks>
[Collection(LedgerCollectionDefinition.Name)]
public class DailyBalanceTests
{
    private static readonly DateTimeOffset Dia1 = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly LedgerFixture _fixture;
    private readonly PostgresLedgerStore _store;

    public DailyBalanceTests(LedgerFixture fixture)
    {
        _fixture = fixture;
        _store = new PostgresLedgerStore(fixture.RuntimeDataSource);
    }

    private static DateTimeOffset Dia(int n, int hora = 0, int minuto = 0, int segundo = 0) =>
        Dia1.AddDays(n - 1).AddHours(hora).AddMinutes(minuto).AddSeconds(segundo);

    private async Task<PostEntryResult> LancarAsync(Guid conta, EntryDirection sentido, decimal valor, DateTimeOffset fato, string chave)
    {
        var comando = new PostingRequest(sentido, Money.Of(valor, Currency.Brl), fato, chave, Guid.NewGuid());
        return await _store.PostAsync(conta, comando, RequestFingerprint.Of(conta, comando), CancellationToken.None);
    }

    /// <summary>Sequencia com retroativos fora de ordem, debitos, estorno, meia-noite e ultimo microssegundo.</summary>
    private async Task<Guid> ContaComHistoricoAsync()
    {
        var conta = await _fixture.CreateAccountAsync();

        await LancarAsync(conta, EntryDirection.Credit, 100.00m, Dia(1, 10), "d-1");
        await LancarAsync(conta, EntryDirection.Debit, 30.00m, Dia(2).AddTicks(-10), "d-2");      // ultimo microssegundo do dia 1
        var meiaNoite = await LancarAsync(conta, EntryDirection.Credit, 50.00m, Dia(3), "d-3");      // meia-noite exata do dia 3
        await LancarAsync(conta, EntryDirection.Credit, 20.00m, Dia(2, 12), "d-4");                 // retroativo
        await LancarAsync(conta, EntryDirection.Debit, 10.00m, Dia(5, 9), "d-5");
        var estorno = new ReversalRequest(Dia(6, 15), "d-6", Guid.NewGuid());
        await _store.ReverseAsync(conta, meiaNoite.EntryId, estorno,
            RequestFingerprint.OfReversal(conta, meiaNoite.EntryId, estorno), CancellationToken.None);
        await LancarAsync(conta, EntryDirection.Debit, 5.00m, Dia(1, 8), "d-7");                   // retroativo ao primeiro dia
        await LancarAsync(conta, EntryDirection.Credit, 7.00m, Dia(4, 23, 59, 59), "d-8");         // retroativo, fim do dia 4

        return conta;
    }

    private async Task<(decimal Saldo, long Sequencia)> LedgerAteAsync(Guid conta, DateTimeOffset instante)
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        return await connection.QuerySingleAsync<(decimal, long)>(
            """
            SELECT COALESCE(SUM(direction * amount), 0), COALESCE(MAX(sequence), 0)
              FROM ledger.ledger_entries
             WHERE account_id = @conta AND occurred_at <= @instante
            """,
            new { conta, instante = instante.UtcDateTime });
    }

    [Fact]
    public async Task Posicao_historica_confere_com_a_soma_do_ledger_em_todos_os_instantes()
    {
        var conta = await ContaComHistoricoAsync();

        var instantes = Enumerable.Range(0, 9).SelectMany(d => new[]
        {
            Dia(d), Dia(d, 8), Dia(d, 9, 59), Dia(d, 10), Dia(d, 12), Dia(d, 15), Dia(d + 1).AddTicks(-10),
        });

        foreach (var instante in instantes)
        {
            var posicao = await _store.GetBalanceAsync(conta, instante, CancellationToken.None);
            var (saldo, sequencia) = await LedgerAteAsync(conta, instante);

            Assert.True(saldo == posicao.Balance.Amount, $"Posicao em {instante:O}: {posicao.Balance.Amount}, ledger: {saldo}");
            Assert.True(sequencia == posicao.ComputedAtSequence, $"Sequencia em {instante:O}: {posicao.ComputedAtSequence}, ledger: {sequencia}");
        }
    }

    [Fact]
    public async Task Fechamentos_conferem_com_o_ledger_depois_de_lancamentos_retroativos()
    {
        var conta = await ContaComHistoricoAsync();

        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        var divergentes = (await connection.QueryAsync<string>(
            """
            SELECT d.day::text
              FROM ledger.daily_balances d,
                   LATERAL (SELECT COALESCE(SUM(e.direction * e.amount), 0) AS saldo,
                                   COALESCE(MAX(e.sequence), 0)             AS sequencia
                              FROM ledger.ledger_entries e
                             WHERE e.account_id = d.account_id
                               AND e.occurred_at < (d.day + 1)::timestamp AT TIME ZONE 'UTC') l
             WHERE d.account_id = @conta
               AND (d.closing_balance <> l.saldo OR d.last_sequence <> l.sequencia)
            """,
            new { conta })).ToList();

        var dias = await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM ledger.daily_balances WHERE account_id = @conta", new { conta });

        Assert.Empty(divergentes);
        Assert.Equal(6, dias); // dias 1, 2, 3, 4, 5 e 6 tem lancamento
    }

    [Fact]
    public async Task Consulta_historica_parte_do_fechamento_e_soma_so_o_dia_consultado()
    {
        var conta = await ContaComHistoricoAsync();

        // Dia 5 ate 12h: fechamento do dia 4 mais o debito das 9h.
        var meioDoDia5 = await _store.GetBalanceAsync(conta, Dia(5, 12), CancellationToken.None);
        Assert.Equal(BalanceSource.DailyBalance, meioDoDia5.ComputedFrom);
        Assert.Equal(1, meioDoDia5.EntriesReplayed);

        // Dia 1: nao ha fechamento anterior; a posicao sai so do ledger do dia.
        var dia1 = await _store.GetBalanceAsync(conta, Dia(1, 12), CancellationToken.None);
        Assert.Equal(BalanceSource.Ledger, dia1.ComputedFrom);
        Assert.Equal(2, dia1.EntriesReplayed);
    }

    [Fact]
    public async Task Papel_da_aplicacao_nao_apaga_fechamento()
    {
        var conta = await ContaComHistoricoAsync();

        await using var connection = await _fixture.RuntimeDataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var erro = await Assert.ThrowsAsync<PostgresException>(() =>
            connection.ExecuteAsync("DELETE FROM ledger.daily_balances WHERE account_id = @conta", new { conta }));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
    }
}
