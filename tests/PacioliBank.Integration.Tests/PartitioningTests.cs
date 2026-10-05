using System.Globalization;
using Dapper;
using Npgsql;
using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Domain;
using PacioliBank.Ledger.Persistence;
using PacioliBank.Migrations;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Ledger particionado por mes de registro, com as garantias de unicidade
/// mantidas no banco por uma tabela de chaves nao particionada (ADR-0013,
/// card 37). PostgreSQL real.
/// </summary>
/// <remarks>
/// Restricao unica de tabela particionada so vale dentro de cada particao. Os
/// testes de duplicidade gravam a segunda ocorrencia de proposito em OUTRO
/// periodo, que e exatamente o caso que a particao sozinha deixaria passar.
/// </remarks>
[Collection(LedgerCollectionDefinition.Name)]
public class PartitioningTests
{
    private readonly LedgerFixture _fixture;
    private readonly PostgresLedgerStore _store;

    public PartitioningTests(LedgerFixture fixture)
    {
        _fixture = fixture;
        _store = new PostgresLedgerStore(fixture.RuntimeDataSource);
    }

    private static readonly DateTimeOffset TresMesesAtras = DateTimeOffset.UtcNow.AddMonths(-3);

    private async Task<PostEntryResult> CreditarAsync(Guid conta, string chave, decimal valor = 10.00m)
    {
        var comando = new PostingRequest(EntryDirection.Credit, Money.Of(valor, Currency.Brl), DateTimeOffset.UtcNow, chave, Guid.NewGuid());
        return await _store.PostAsync(conta, comando, RequestFingerprint.Of(conta, comando), CancellationToken.None);
    }

    private async Task<PostgresException> RecusaDoBancoAsync(string sql, object parametros)
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        return await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(sql, parametros));
    }

    private const string ChaveEmOutroPeriodo = """
        INSERT INTO ledger.entry_keys (entry_id, account_id, sequence, idempotency_key, reversal_of, recorded_at)
        VALUES (@id, @conta, @sequencia, @chave, @estornoDe, @registro)
        """;

    [Fact]
    public async Task Ledger_e_particionado_por_mes_de_registro_com_particao_padrao()
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);

        var chave = await connection.ExecuteScalarAsync<string>(
            "SELECT pg_get_partkeydef('ledger.ledger_entries'::regclass)");
        var particoes = (await connection.QueryAsync<string>(
            """
            SELECT c.relname
              FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid
             WHERE i.inhparent = 'ledger.ledger_entries'::regclass
            """)).ToList();

        Assert.Equal("RANGE (recorded_at)", chave);
        Assert.Contains("ledger_entries_default", particoes);
        Assert.Contains("ledger_entries_" + DateTime.UtcNow.ToString("yyyy_MM", CultureInfo.InvariantCulture), particoes);
    }

    [Fact]
    public async Task Lancamento_cai_na_particao_do_mes_de_registro_e_nao_na_padrao()
    {
        var conta = await _fixture.CreateAccountAsync();
        var lancamento = await CreditarAsync(conta, "p-1");

        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        var particao = await connection.ExecuteScalarAsync<string>(
            "SELECT tableoid::regclass::text FROM ledger.ledger_entries WHERE entry_id = @id", new { id = lancamento.EntryId });

        Assert.Equal("ledger.ledger_entries_" + lancamento.RecordedAt.UtcDateTime.ToString("yyyy_MM", CultureInfo.InvariantCulture), particao);
    }

    [Fact]
    public async Task Mesma_sequencia_em_outro_periodo_e_recusada_pelo_banco()
    {
        var conta = await _fixture.CreateAccountAsync();
        await CreditarAsync(conta, "p-2");

        var erro = await RecusaDoBancoAsync(ChaveEmOutroPeriodo,
            new { id = Guid.NewGuid(), conta, sequencia = 1L, chave = "outra", estornoDe = (Guid?)null, registro = TresMesesAtras });

        Assert.Equal("uq_entries_sequence", erro.ConstraintName);
    }

    [Fact]
    public async Task Mesma_chave_de_idempotencia_em_outro_periodo_e_recusada_pelo_banco()
    {
        var conta = await _fixture.CreateAccountAsync();
        await CreditarAsync(conta, "p-3");

        var erro = await RecusaDoBancoAsync(ChaveEmOutroPeriodo,
            new { id = Guid.NewGuid(), conta, sequencia = 99L, chave = "p-3", estornoDe = (Guid?)null, registro = TresMesesAtras });

        Assert.Equal("uq_entries_idempotency", erro.ConstraintName);
    }

    [Fact]
    public async Task Segundo_estorno_em_outro_periodo_e_recusado_pelo_banco()
    {
        var conta = await _fixture.CreateAccountAsync();
        var credito = await CreditarAsync(conta, "p-4");
        var estorno = new ReversalRequest(DateTimeOffset.UtcNow, "p-5", Guid.NewGuid());
        await _store.ReverseAsync(conta, credito.EntryId, estorno,
            RequestFingerprint.OfReversal(conta, credito.EntryId, estorno), CancellationToken.None);

        var erro = await RecusaDoBancoAsync(ChaveEmOutroPeriodo,
            new { id = Guid.NewGuid(), conta, sequencia = 99L, chave = "p-6", estornoDe = (Guid?)credito.EntryId, registro = TresMesesAtras });

        Assert.Equal("uq_entries_reversal", erro.ConstraintName);
    }

    [Fact]
    public async Task Linha_do_ledger_sem_chave_correspondente_e_recusada_pelo_banco()
    {
        // A unicidade so vale se toda linha do ledger tiver a sua chave, com os
        // mesmos valores. Uma linha com sequencia diferente da chave registrada
        // escaparia da restricao.
        var conta = await _fixture.CreateAccountAsync();
        var id = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(_fixture.MigratorConnectionString))
        {
            await connection.ExecuteAsync(ChaveEmOutroPeriodo,
                new { id, conta, sequencia = 1L, chave = "p-7", estornoDe = (Guid?)null, registro = TresMesesAtras });
        }

        var erro = await RecusaDoBancoAsync(
            """
            INSERT INTO ledger.ledger_entries
                (entry_id, account_id, sequence, direction, amount, currency, occurred_at, recorded_at,
                 idempotency_key, correlation_id, balance_after)
            VALUES (@id, @conta, 2, 1, 10, 'BRL', @registro, @registro, 'p-7', @id, 10)
            """,
            new { id, conta, registro = TresMesesAtras });

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, erro.SqlState);
        Assert.Equal("fk_entries_keys", erro.ConstraintName);
    }

    [Fact]
    public async Task Aplicacao_nao_altera_nem_apaga_chave()
    {
        var conta = await _fixture.CreateAccountAsync();
        var lancamento = await CreditarAsync(conta, "p-8");

        await using var connection = await _fixture.RuntimeDataSource.OpenConnectionAsync();
        var alterar = await Assert.ThrowsAsync<PostgresException>(() =>
            connection.ExecuteAsync("UPDATE ledger.entry_keys SET sequence = 7 WHERE entry_id = @id", new { id = lancamento.EntryId }));
        var apagar = await Assert.ThrowsAsync<PostgresException>(() =>
            connection.ExecuteAsync("DELETE FROM ledger.entry_keys WHERE entry_id = @id", new { id = lancamento.EntryId }));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, alterar.SqlState);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, apagar.SqlState);
    }

    [Fact]
    public async Task Migrador_cria_as_particoes_dos_proximos_meses_sem_duplicar()
    {
        SchemaMigrator.EnsurePartitions(_fixture.MigratorConnectionString, monthsAhead: 14);
        var segunda = SchemaMigrator.EnsurePartitions(_fixture.MigratorConnectionString, monthsAhead: 14);

        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        var particoes = (await connection.QueryAsync<string>(
            "SELECT c.relname FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid WHERE i.inhparent = 'ledger.ledger_entries'::regclass")).ToHashSet();

        Assert.Empty(segunda);
        var hoje = DateTime.UtcNow;
        for (var mes = 0; mes <= 14; mes++)
        {
            Assert.Contains("ledger_entries_" + hoje.AddMonths(mes).ToString("yyyy_MM", CultureInfo.InvariantCulture), particoes);
        }
    }
}
