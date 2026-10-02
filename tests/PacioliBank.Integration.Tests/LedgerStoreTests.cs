using Dapper;
using Npgsql;
using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Domain;
using PacioliBank.Ledger.Persistence;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Verifica as invariantes que so existem na interacao entre codigo e banco.
/// Equivale as funcionalidades F01, F02, F03, F04 e F09 do BDD.
/// </summary>
[Collection(LedgerCollection.Name)]
public class LedgerStoreTests
{
    private readonly LedgerFixture _fixture;
    private readonly PostgresLedgerStore _store;

    public LedgerStoreTests(LedgerFixture fixture)
    {
        _fixture = fixture;
        _store = new PostgresLedgerStore(fixture.RuntimeDataSource);
    }

    private static PostingRequest Comando(
        EntryDirection sentido,
        decimal valor,
        string chave,
        DateTimeOffset? quando = null) =>
        new(sentido,
            Money.Of(valor, Currency.Brl),
            quando ?? DateTimeOffset.UtcNow,
            chave,
            Guid.NewGuid());

    private Task<PostEntryResult> PostAsync(Guid conta, PostingRequest comando) =>
        _store.PostAsync(conta, comando, RequestFingerprint.Of(conta, comando), CancellationToken.None);

    // ---------------------------------------------------------------- F01

    [Fact]
    public async Task Credito_e_gravado_e_aparece_na_posicao()
    {
        var conta = await _fixture.CreateAccountAsync();

        var resultado = await PostAsync(conta, Comando(EntryDirection.Credit, 150.00m, "k1"));

        Assert.Equal(1L, resultado.Sequence);
        Assert.Equal(150.00m, resultado.BalanceAfter.Amount);
        Assert.False(resultado.Replayed);

        var posicao = await _store.GetBalanceAsync(conta, null, CancellationToken.None);
        Assert.Equal(150.00m, posicao.Balance.Amount);
        Assert.Equal(1L, posicao.ComputedAtSequence);
    }

    [Fact]
    public async Task Conta_inexistente_e_recusada()
    {
        await Assert.ThrowsAsync<AccountNotFoundException>(
            () => PostAsync(Guid.NewGuid(), Comando(EntryDirection.Credit, 10.00m, "k1")));
    }

    // ---------------------------------------------------------------- F02

    [Fact]
    public async Task Debito_acima_do_saldo_e_recusado_sem_gravar_nada()
    {
        var conta = await _fixture.CreateAccountAsync(openingCredit: 1000.00m);

        await Assert.ThrowsAsync<InsufficientFundsException>(
            () => PostAsync(conta, Comando(EntryDirection.Debit, 1000.01m, "k1")));

        var posicao = await _store.GetBalanceAsync(conta, null, CancellationToken.None);
        Assert.Equal(1000.00m, posicao.Balance.Amount);
        Assert.Equal(1, await ContarLancamentosAsync(conta));
    }

    [Fact]
    public async Task Rejeicao_nao_consome_sequencia()
    {
        var conta = await _fixture.CreateAccountAsync(openingCredit: 100.00m);

        await Assert.ThrowsAsync<InsufficientFundsException>(
            () => PostAsync(conta, Comando(EntryDirection.Debit, 500.00m, "k1")));

        var seguinte = await PostAsync(conta, Comando(EntryDirection.Debit, 50.00m, "k2"));

        Assert.Equal(2L, seguinte.Sequence);
    }

    // ---------------------------------------------------------------- F03

    [Fact]
    public async Task Repeticao_do_mesmo_comando_devolve_o_resultado_original()
    {
        var conta = await _fixture.CreateAccountAsync(openingCredit: 500.00m);
        var comando = Comando(EntryDirection.Debit, 100.00m, "k-repeticao");

        var primeira = await PostAsync(conta, comando);
        var segunda = await PostAsync(conta, comando);

        Assert.False(primeira.Replayed);
        Assert.True(segunda.Replayed);
        Assert.Equal(primeira.EntryId, segunda.EntryId);
        Assert.Equal(primeira.Sequence, segunda.Sequence);
        Assert.Equal(primeira.BalanceAfter.Amount, segunda.BalanceAfter.Amount);
        Assert.Equal(2, await ContarLancamentosAsync(conta));
    }

    [Fact]
    public async Task Repeticao_devolve_corpo_identico_inclusive_os_instantes()
    {
        // EF 8.4: corpo identico ao da primeira execucao. O instante com fracao
        // abaixo de microssegundo forca a diferenca entre a precisao do .NET
        // (100 ns) e a do timestamptz (1 us).
        var conta = await _fixture.CreateAccountAsync(openingCredit: 500.00m);
        var quando = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero).AddTicks(1_234_567);
        var comando = Comando(EntryDirection.Debit, 100.00m, "k-corpo", quando);

        var primeira = await PostAsync(conta, comando);
        var segunda = await PostAsync(conta, comando);

        Assert.Equal(primeira with { Replayed = true }, segunda);
    }

    [Fact]
    public async Task Chave_reutilizada_com_conteudo_diferente_e_recusada()
    {
        var conta = await _fixture.CreateAccountAsync(openingCredit: 500.00m);

        await PostAsync(conta, Comando(EntryDirection.Debit, 100.00m, "k-conflito"));

        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => PostAsync(conta, Comando(EntryDirection.Debit, 250.00m, "k-conflito")));

        var posicao = await _store.GetBalanceAsync(conta, null, CancellationToken.None);
        Assert.Equal(400.00m, posicao.Balance.Amount);
    }

    [Fact]
    public async Task Chave_de_idempotencia_e_escopada_por_conta()
    {
        var contaA = await _fixture.CreateAccountAsync(openingCredit: 500.00m);
        var contaB = await _fixture.CreateAccountAsync(openingCredit: 500.00m);

        await PostAsync(contaA, Comando(EntryDirection.Debit, 100.00m, "k-compartilhada"));
        var naContaB = await PostAsync(contaB, Comando(EntryDirection.Debit, 100.00m, "k-compartilhada"));

        Assert.False(naContaB.Replayed);
        Assert.Equal(400.00m, naContaB.BalanceAfter.Amount);
    }

    // ---------------------------------------------------------------- F04

    [Fact]
    public async Task Posicao_em_instante_passado_considera_a_data_do_fato()
    {
        var conta = await _fixture.CreateAccountAsync();
        var janeiro = new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.Zero);
        var fevereiro = new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);

        await PostAsync(conta, Comando(EntryDirection.Credit, 1000.00m, "k1", janeiro));
        await PostAsync(conta, Comando(EntryDirection.Credit, 500.00m, "k2", fevereiro));

        var emJaneiro = await _store.GetBalanceAsync(
            conta, new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
        var emMarco = await _store.GetBalanceAsync(
            conta, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(1000.00m, emJaneiro.Balance.Amount);
        Assert.Equal(1500.00m, emMarco.Balance.Amount);
    }

    [Fact]
    public async Task Limite_do_instante_e_inclusivo()
    {
        // RN-011: lancamento com occurred_at exatamente igual a T entra no calculo.
        var conta = await _fixture.CreateAccountAsync();
        var instante = new DateTimeOffset(2026, 1, 15, 14, 30, 0, TimeSpan.Zero);

        await PostAsync(conta, Comando(EntryDirection.Credit, 777.00m, "k1", instante));

        var posicao = await _store.GetBalanceAsync(conta, instante, CancellationToken.None);

        Assert.Equal(777.00m, posicao.Balance.Amount);
    }

    [Fact]
    public async Task Conta_sem_lancamentos_tem_posicao_zero()
    {
        var conta = await _fixture.CreateAccountAsync();

        var posicao = await _store.GetBalanceAsync(conta, null, CancellationToken.None);

        Assert.True(posicao.Balance.IsZero);
        Assert.Equal(BalanceSource.Ledger, posicao.ComputedFrom);
    }

    // ------------------------------------------------- F09 e RNF-025

    [Fact]
    public async Task Aplicacao_nao_consegue_alterar_lancamento_gravado()
    {
        // ADR-0009: a imutabilidade nao depende de o codigo evitar UPDATE.
        // Depende de o papel da aplicacao nao ter o privilegio.
        var conta = await _fixture.CreateAccountAsync(openingCredit: 100.00m);

        await using var connection = await _fixture.RuntimeDataSource.OpenConnectionAsync();

        var erro = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "UPDATE ledger.ledger_entries SET amount = 999999 WHERE account_id = @conta",
            new { conta }));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
    }

    [Fact]
    public async Task Aplicacao_nao_consegue_excluir_lancamento_gravado()
    {
        var conta = await _fixture.CreateAccountAsync(openingCredit: 100.00m);

        await using var connection = await _fixture.RuntimeDataSource.OpenConnectionAsync();

        var erro = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "DELETE FROM ledger.ledger_entries WHERE account_id = @conta",
            new { conta }));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
    }

    [Fact]
    public async Task Aplicacao_nao_consegue_alterar_o_status_da_conta()
    {
        // O privilegio reflete a fronteira de escopo: o ciclo de vida da conta
        // pertence a outro contexto delimitado (EF 3.2).
        var conta = await _fixture.CreateAccountAsync();

        await using var connection = await _fixture.RuntimeDataSource.OpenConnectionAsync();

        var erro = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "UPDATE ledger.accounts SET status = 3 WHERE account_id = @conta",
            new { conta }));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
    }

    // ------------------------------------------------------------- Outbox

    [Fact]
    public async Task Evento_de_integracao_e_gravado_na_mesma_transacao()
    {
        var conta = await _fixture.CreateAccountAsync();

        var resultado = await PostAsync(conta, Comando(EntryDirection.Credit, 42.00m, "k1"));

        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        var tipo = await connection.QuerySingleAsync<string>(
            """
            SELECT event_type
              FROM ledger.outbox_messages
             WHERE account_id = @conta AND sequence = @sequencia
            """,
            new { conta, sequencia = resultado.Sequence });

        Assert.Equal("pacioli.ledger.entry-recorded.v1", tipo);
    }

    private async Task<int> ContarLancamentosAsync(Guid conta)
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ledger.ledger_entries WHERE account_id = @conta",
            new { conta });
    }
}
