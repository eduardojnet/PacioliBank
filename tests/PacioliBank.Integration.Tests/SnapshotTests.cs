using Dapper;
using Npgsql;
using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Domain;
using PacioliBank.Ledger.Persistence;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Snapshot amortizado da posicao (ADR-0007, RNF-006, card 30.1).
/// </summary>
/// <remarks>
/// A cada 100 lancamentos, a propria escrita grava a posicao que acabou de
/// calcular; a posicao corrente parte do snapshot mais recente e soma so o que
/// veio depois, no maximo 99 lancamentos. Antes deste teste, a unica evidencia
/// era a demonstracao manual do painel (lacuna L-15).
/// <para>
/// O valor e sempre conferido contra a soma do ledger inteiro, calculada por
/// fora: snapshot que acelera e erra e pior que nenhum snapshot.
/// </para>
/// </remarks>
[Collection(LedgerCollectionDefinition.Name)]
public class SnapshotTests
{
    private const int SnapshotEvery = 100;

    private static readonly DateTimeOffset Inicio = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly LedgerFixture _fixture;
    private readonly PostgresLedgerStore _store;

    public SnapshotTests(LedgerFixture fixture)
    {
        _fixture = fixture;
        _store = new PostgresLedgerStore(fixture.RuntimeDataSource);
    }

    // Valores diferentes a cada lancamento, e um debito a cada sete: uma soma
    // errada, ou um sinal trocado no snapshot, muda o resultado.
    private async Task LancarAsync(Guid conta, int de, int ate)
    {
        for (var i = de; i <= ate; i++)
        {
            var sentido = i % 7 == 0 ? EntryDirection.Debit : EntryDirection.Credit;
            var valor = sentido == EntryDirection.Debit ? 0.50m : 1.00m + (i * 0.01m);
            var comando = new PostingRequest(
                sentido, Money.Of(valor, Currency.Brl), Inicio.AddDays(i), $"snap-{i}", Guid.NewGuid());

            await _store.PostAsync(conta, comando, RequestFingerprint.Of(conta, comando), CancellationToken.None);
        }
    }

    private async Task<decimal> SomaDoLedgerAsync(Guid conta, DateTimeOffset? ate = null)
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        return await connection.ExecuteScalarAsync<decimal>(
            """
            SELECT COALESCE(SUM(direction * amount), 0)
              FROM ledger.ledger_entries
             WHERE account_id = @conta
               AND (@ate::timestamptz IS NULL OR occurred_at <= @ate)
            """,
            new { conta, ate = ate?.UtcDateTime });
    }

    private async Task<(long Sequencia, decimal Saldo)[]> SnapshotsAsync(Guid conta)
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        var linhas = await connection.QueryAsync<(long, decimal)>(
            "SELECT up_to_sequence, balance FROM ledger.balance_snapshots WHERE account_id = @conta ORDER BY up_to_sequence",
            new { conta });
        return linhas.ToArray();
    }

    private async Task<decimal> SaldoAposAsync(Guid conta, long sequencia)
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        return await connection.ExecuteScalarAsync<decimal>(
            "SELECT balance_after FROM ledger.ledger_entries WHERE account_id = @conta AND sequence = @sequencia",
            new { conta, sequencia });
    }

    [Fact]
    public async Task Antes_da_centesima_escrita_a_posicao_vem_do_ledger_inteiro()
    {
        var conta = await _fixture.CreateAccountAsync();
        await LancarAsync(conta, 1, SnapshotEvery - 1);

        var posicao = await _store.GetBalanceAsync(conta, null, CancellationToken.None);

        Assert.Empty(await SnapshotsAsync(conta));
        Assert.Equal(BalanceSource.Ledger, posicao.ComputedFrom);
        Assert.Equal(SnapshotEvery - 1, posicao.EntriesReplayed);
        Assert.Equal(await SomaDoLedgerAsync(conta), posicao.Balance.Amount);
    }

    [Fact]
    public async Task A_centesima_escrita_grava_o_snapshot_e_a_posicao_passa_a_partir_dele()
    {
        var conta = await _fixture.CreateAccountAsync();
        await LancarAsync(conta, 1, SnapshotEvery);

        // Gravado pela propria escrita, com o saldo que ela calculou.
        var snapshot = Assert.Single(await SnapshotsAsync(conta));
        Assert.Equal(SnapshotEvery, snapshot.Sequencia);
        Assert.Equal(await SaldoAposAsync(conta, SnapshotEvery), snapshot.Saldo);

        var naAncora = await _store.GetBalanceAsync(conta, null, CancellationToken.None);
        Assert.Equal(BalanceSource.Snapshot, naAncora.ComputedFrom);
        Assert.Equal(0, naAncora.EntriesReplayed);
        Assert.Equal(await SomaDoLedgerAsync(conta), naAncora.Balance.Amount);

        // Mais 99: o maximo que a posicao corrente soma depois de um snapshot.
        await LancarAsync(conta, SnapshotEvery + 1, (2 * SnapshotEvery) - 1);

        var noLimite = await _store.GetBalanceAsync(conta, null, CancellationToken.None);
        Assert.Equal(BalanceSource.Snapshot, noLimite.ComputedFrom);
        Assert.Equal(SnapshotEvery - 1, noLimite.EntriesReplayed);
        Assert.Equal(await SomaDoLedgerAsync(conta), noLimite.Balance.Amount);
        Assert.Single(await SnapshotsAsync(conta));

        // A escrita tambem parte do snapshot para validar o saldo (RN-001) e
        // gravar balance_after. Snapshot errado contaminaria todo lancamento
        // seguinte, e o extrato com ele.
        Assert.Equal(await SomaDoLedgerAsync(conta), await SaldoAposAsync(conta, (2 * SnapshotEvery) - 1));
    }

    [Fact]
    public async Task Posicao_historica_nao_usa_snapshot_e_parte_do_fechamento_diario()
    {
        // O snapshot e ancorado em sequencia (ordem de registro) e a consulta
        // historica, na data do fato; com lancamento retroativo, as duas
        // ordens divergem. Por isso a historica parte do fechamento do dia
        // anterior (card 32), e nao do snapshot: soma so o proprio dia.
        var conta = await _fixture.CreateAccountAsync();
        await LancarAsync(conta, 1, SnapshotEvery + 20);

        var instante = Inicio.AddDays(SnapshotEvery + 10);
        var historica = await _store.GetBalanceAsync(conta, instante, CancellationToken.None);

        Assert.Equal(BalanceSource.DailyBalance, historica.ComputedFrom);
        Assert.Equal(1, historica.EntriesReplayed);
        Assert.Equal(await SomaDoLedgerAsync(conta, instante), historica.Balance.Amount);
    }
}
