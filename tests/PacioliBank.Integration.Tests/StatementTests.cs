using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Domain;
using PacioliBank.Ledger.Persistence;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Extrato paginado por cursor (RF-005). Equivale a funcionalidade F05 do BDD.
/// </summary>
[Collection(LedgerCollectionDefinition.Name)]
public class StatementTests
{
    private static readonly DateTimeOffset Janeiro = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly LedgerFixture _fixture;
    private readonly PostgresLedgerStore _store;

    public StatementTests(LedgerFixture fixture)
    {
        _fixture = fixture;
        _store = new PostgresLedgerStore(fixture.RuntimeDataSource);
    }

    /// <summary>Um credito de 1,00 por dia a partir de 1 de janeiro.</summary>
    private async Task<Guid> ContaComLancamentosDiariosAsync(int quantidade)
    {
        var conta = await _fixture.CreateAccountAsync();

        for (var dia = 0; dia < quantidade; dia++)
        {
            var comando = new PostingRequest(
                EntryDirection.Credit, Money.Of(1.00m, Currency.Brl), Janeiro.AddDays(dia), $"k-{dia}", Guid.NewGuid());

            await _store.PostAsync(conta, comando, RequestFingerprint.Of(conta, comando), CancellationToken.None);
        }

        return conta;
    }

    [Fact]
    public async Task Extrato_vem_em_ordem_crescente_de_sequencia_com_saldo_progressivo()
    {
        var conta = await ContaComLancamentosDiariosAsync(5);

        var pagina = await _store.GetStatementAsync(conta, null, null, 0, 50, CancellationToken.None);

        Assert.Equal([1L, 2L, 3L, 4L, 5L], pagina.Entries.Select(e => e.Sequence));
        Assert.Equal([1.00m, 2.00m, 3.00m, 4.00m, 5.00m], pagina.Entries.Select(e => e.BalanceAfter.Amount));
        Assert.Null(pagina.NextAfterSequence);
    }

    [Fact]
    public async Task Paginacao_por_cursor_percorre_tudo_sem_repetir_nem_omitir()
    {
        var conta = await ContaComLancamentosDiariosAsync(12);

        var vistos = new List<long>();
        long cursor = 0;
        int paginas = 0;

        while (true)
        {
            var pagina = await _store.GetStatementAsync(conta, null, null, cursor, 5, CancellationToken.None);
            vistos.AddRange(pagina.Entries.Select(e => e.Sequence));
            paginas++;

            if (pagina.NextAfterSequence is null)
            {
                break;
            }

            cursor = pagina.NextAfterSequence.Value;
        }

        Assert.Equal(3, paginas);
        Assert.Equal(Enumerable.Range(1, 12).Select(i => (long)i), vistos);
    }

    [Fact]
    public async Task Filtro_por_periodo_usa_a_data_do_fato_com_limites_inclusivos()
    {
        var conta = await ContaComLancamentosDiariosAsync(10);

        var pagina = await _store.GetStatementAsync(
            conta, Janeiro.AddDays(2), Janeiro.AddDays(4), 0, 50, CancellationToken.None);

        Assert.Equal([3L, 4L, 5L], pagina.Entries.Select(e => e.Sequence));
        Assert.All(pagina.Entries, e => Assert.InRange(e.OccurredAt, Janeiro.AddDays(2), Janeiro.AddDays(4)));
    }

    [Fact]
    public async Task Estorno_aparece_no_extrato_referenciando_o_original()
    {
        var conta = await ContaComLancamentosDiariosAsync(1);
        var original = (await _store.GetStatementAsync(conta, null, null, 0, 50, CancellationToken.None)).Entries[0];

        var estorno = new ReversalRequest(DateTimeOffset.UtcNow, "estorno-1", Guid.NewGuid());
        await _store.ReverseAsync(
            conta, original.EntryId, estorno, RequestFingerprint.OfReversal(conta, original.EntryId, estorno), CancellationToken.None);

        var pagina = await _store.GetStatementAsync(conta, null, null, 0, 50, CancellationToken.None);

        Assert.Equal(2, pagina.Entries.Count);
        Assert.Equal(original.EntryId, pagina.Entries[1].ReversalOf);
        Assert.Equal(EntryDirection.Debit, pagina.Entries[1].Direction);
    }

    [Fact]
    public async Task Extrato_de_conta_inexistente_e_recusado()
    {
        await Assert.ThrowsAsync<AccountNotFoundException>(
            () => _store.GetStatementAsync(Guid.NewGuid(), null, null, 0, 50, CancellationToken.None));
    }
}
