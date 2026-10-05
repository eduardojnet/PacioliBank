using Dapper;
using Npgsql;
using PacioliBank.Migrations;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Migracoes versionadas: banco vazio fica completo, reexecucao nao faz nada,
/// migracao nova evolui o banco sem perder dado (RNF-038, ADR-0002, card 27).
/// </summary>
/// <remarks>
/// Cada teste cria o proprio banco vazio no servidor da fixture. O banco
/// compartilhado ja foi migrado na inicializacao e nao serve para provar o
/// ponto de partida.
/// </remarks>
[Collection(LedgerCollectionDefinition.Name)]
public class MigrationTests
{
    private const string InitialSchema = "0001_esquema_inicial.sql";
    private const string DailyBalances = "0002_saldo_diario.sql";

    private readonly LedgerFixture _fixture;

    public MigrationTests(LedgerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Banco_vazio_recebe_o_esquema_completo_pelas_migracoes()
    {
        var banco = await _fixture.CreateEmptyDatabaseAsync();

        var resultado = SchemaMigrator.ApplySchema(banco);

        Assert.True(resultado.Successful, resultado.Error?.ToString());
        Assert.Contains(InitialSchema, resultado.Applied);

        var tabelas = await TabelasDoLedgerAsync(banco);
        Assert.Equal(
            ["accounts", "balance_snapshots", "daily_balances", "idempotency_records", "ledger_entries", "outbox_messages"],
            tabelas);
    }

    [Fact]
    public async Task Segunda_execucao_nao_reaplica_nenhuma_migracao()
    {
        var banco = await _fixture.CreateEmptyDatabaseAsync();
        Assert.True(SchemaMigrator.ApplySchema(banco).Successful);

        var segunda = SchemaMigrator.ApplySchema(banco);

        Assert.True(segunda.Successful, segunda.Error?.ToString());
        Assert.Empty(segunda.Applied);
        Assert.Equal(
            Directory.GetFiles(SchemaMigrator.DefaultMigrationsDirectory, "*.sql").Length,
            await ContarAsync(banco, "SELECT count(*) FROM public.schema_versions"));
    }

    [Fact]
    public async Task Migracao_nova_e_aplicada_sozinha_e_preserva_os_dados()
    {
        var banco = await _fixture.CreateEmptyDatabaseAsync();
        using var pasta = PastaDeMigracoes.CopiaDasOficiais();
        Assert.True(SchemaMigrator.ApplySchema(banco, pasta.Caminho).Successful);

        var conta = Guid.NewGuid();
        await ExecutarAsync(banco,
            "INSERT INTO ledger.accounts (account_id, customer_id, currency, status, last_sequence) VALUES (@conta, @conta, 'BRL', 1, 0)",
            new { conta });

        pasta.Acrescentar("0002_evolucao.sql", "ALTER TABLE ledger.accounts ADD COLUMN evolucao_teste text NULL;");
        var evolucao = SchemaMigrator.ApplySchema(banco, pasta.Caminho);

        Assert.True(evolucao.Successful, evolucao.Error?.ToString());
        Assert.Equal(["0002_evolucao.sql"], evolucao.Applied);
        Assert.Equal(1, await ContarAsync(banco, "SELECT count(*) FROM ledger.accounts WHERE account_id = @conta", new { conta }));
        Assert.Equal(1, await ContarAsync(banco,
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'ledger' AND table_name = 'accounts' AND column_name = 'evolucao_teste'"));
    }

    [Fact]
    public async Task Migracao_que_falha_nao_deixa_alteracao_parcial_nem_registro()
    {
        var banco = await _fixture.CreateEmptyDatabaseAsync();
        using var pasta = PastaDeMigracoes.CopiaDasOficiais();
        Assert.True(SchemaMigrator.ApplySchema(banco, pasta.Caminho).Successful);

        // A primeira instrucao funcionaria sozinha; a segunda falha. Com uma
        // transacao por script, nenhuma das duas pode ficar.
        pasta.Acrescentar("0002_quebrada.sql",
            "CREATE TABLE ledger.parcial (id int); SELECT * FROM ledger.tabela_que_nao_existe;");
        var falha = SchemaMigrator.ApplySchema(banco, pasta.Caminho);

        Assert.False(falha.Successful);
        Assert.NotNull(falha.Error);
        Assert.Equal(0, await ContarAsync(banco,
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'ledger' AND table_name = 'parcial'"));
        Assert.Equal(0, await ContarAsync(banco,
            "SELECT count(*) FROM public.schema_versions WHERE scriptname LIKE '%0002_quebrada.sql'"));
    }

    [Fact]
    public async Task Bloco_com_delimitador_nomeado_chega_intacto_ao_banco()
    {
        var banco = await _fixture.CreateEmptyDatabaseAsync();
        using var pasta = PastaDeMigracoes.CopiaDasOficiais();

        // $corpo$ e delimitador de bloco do PostgreSQL. Com a substituicao de
        // variaveis do DbUp ligada, ele seria lido como variavel sem valor.
        pasta.Acrescentar("0002_funcao.sql",
            "CREATE FUNCTION ledger.sonda() RETURNS text LANGUAGE sql AS $corpo$ SELECT 'intacto'::text $corpo$;");
        var resultado = SchemaMigrator.ApplySchema(banco, pasta.Caminho);

        Assert.True(resultado.Successful, resultado.Error?.ToString());
        Assert.Equal(1, await ContarAsync(banco,
            "SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'ledger' AND p.proname = 'sonda'"));
    }

    [Fact]
    public async Task Esquema_nao_traz_massa_local_e_a_massa_e_aplicada_so_quando_pedida()
    {
        var banco = await _fixture.CreateEmptyDatabaseAsync();
        Assert.True(SchemaMigrator.ApplySchema(banco).Successful);
        Assert.Equal(0, await ContarAsync(banco, "SELECT count(*) FROM ledger.accounts"));

        var massa = SchemaMigrator.ApplyLocalSeed(banco);
        var repeticao = SchemaMigrator.ApplyLocalSeed(banco);

        Assert.True(massa.Successful, massa.Error?.ToString());
        Assert.True(repeticao.Successful, repeticao.Error?.ToString());
        Assert.Empty(repeticao.Applied);
        Assert.Equal(5, await ContarAsync(banco, "SELECT count(*) FROM ledger.accounts"));
    }

    [Fact]
    public async Task Diario_de_migracoes_fica_fora_do_alcance_do_papel_da_aplicacao()
    {
        var banco = await _fixture.CreateEmptyDatabaseAsync();
        Assert.True(SchemaMigrator.ApplySchema(banco).Successful);

        var runtime = new NpgsqlConnectionStringBuilder(banco)
        {
            Username = "pacioli_runtime",
            Password = "pacioli_local_dev",
        }.ConnectionString;

        var erro = await Assert.ThrowsAsync<PostgresException>(
            () => ExecutarAsync(runtime, "DELETE FROM public.schema_versions"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
    }

    [Fact]
    public async Task Migracao_do_saldo_diario_preenche_os_fechamentos_a_partir_do_ledger_existente()
    {
        // Banco com o esquema anterior e lancamentos gravados, inclusive
        // retroativo e debito, como estaria um ambiente ja em uso.
        var banco = await _fixture.CreateEmptyDatabaseAsync();
        using var pasta = PastaDeMigracoes.CopiaDasOficiais(InitialSchema);
        Assert.True(SchemaMigrator.ApplySchema(banco, pasta.Caminho).Successful);

        var conta = Guid.NewGuid();
        await ExecutarAsync(banco,
            "INSERT INTO ledger.accounts (account_id, customer_id, currency, status, last_sequence) VALUES (@conta, @conta, 'BRL', 1, 4)",
            new { conta });
        var lancamentos = new (int Sequencia, short Sentido, decimal Valor, string Fato)[]
        {
            (1, 1, 100.00m, "2026-03-01T10:00:00Z"),
            (2, -1, 30.00m, "2026-03-03T09:00:00Z"),
            (3, 1, 20.00m, "2026-03-02T12:00:00Z"),
            (4, 1, 5.00m, "2026-03-03T00:00:00Z"),
        };
        foreach (var l in lancamentos)
        {
            await ExecutarAsync(banco,
                """
                INSERT INTO ledger.ledger_entries
                    (entry_id, account_id, sequence, direction, amount, currency, occurred_at, recorded_at,
                     idempotency_key, correlation_id, balance_after)
                VALUES (@id, @conta, @seq, @sentido, @valor, 'BRL', @fato::timestamptz, now(), @chave, @id, 0)
                """,
                new { id = Guid.NewGuid(), conta, seq = l.Sequencia, sentido = l.Sentido, valor = l.Valor, fato = l.Fato, chave = "m-" + l.Sequencia });
        }

        pasta.AcrescentarOficial(DailyBalances);
        var resultado = SchemaMigrator.ApplySchema(banco, pasta.Caminho);

        Assert.True(resultado.Successful, resultado.Error?.ToString());
        Assert.Equal([DailyBalances], resultado.Applied);

        await using var connection = new NpgsqlConnection(banco);
        var fechamentos = (await connection.QueryAsync<(string Dia, decimal Saldo, long Sequencia)>(
            "SELECT day::text, closing_balance, last_sequence FROM ledger.daily_balances WHERE account_id = @conta ORDER BY day",
            new { conta })).ToList();

        Assert.Equal(
            [("2026-03-01", 100.00m, 1L), ("2026-03-02", 120.00m, 3L), ("2026-03-03", 95.00m, 4L)],
            fechamentos);
    }

    private static async Task<string[]> TabelasDoLedgerAsync(string conexao)
    {
        await using var connection = new NpgsqlConnection(conexao);
        var tabelas = await connection.QueryAsync<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'ledger' ORDER BY table_name");
        return tabelas.ToArray();
    }

    private static async Task<long> ContarAsync(string conexao, string sql, object? parametros = null)
    {
        await using var connection = new NpgsqlConnection(conexao);
        return await connection.ExecuteScalarAsync<long>(sql, parametros);
    }

    private static async Task ExecutarAsync(string conexao, string sql, object? parametros = null)
    {
        await using var connection = new NpgsqlConnection(conexao);
        await connection.ExecuteAsync(sql, parametros);
    }

    /// <summary>Copia temporaria das migracoes oficiais, para acrescentar uma nova sem tocar no repositorio.</summary>
    private sealed class PastaDeMigracoes : IDisposable
    {
        private PastaDeMigracoes(string caminho)
        {
            Caminho = caminho;
        }

        public string Caminho { get; }

        public static PastaDeMigracoes CopiaDasOficiais(params string[] somente)
        {
            var caminho = Path.Combine(Path.GetTempPath(), "pacioli-migracoes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(caminho);

            foreach (var script in Directory.GetFiles(SchemaMigrator.DefaultMigrationsDirectory, "*.sql"))
            {
                if (somente.Length == 0 || somente.Contains(Path.GetFileName(script)))
                {
                    File.Copy(script, Path.Combine(caminho, Path.GetFileName(script)));
                }
            }

            return new PastaDeMigracoes(caminho);
        }

        public void Acrescentar(string nome, string sql) => File.WriteAllText(Path.Combine(Caminho, nome), sql);

        public void AcrescentarOficial(string nome) =>
            File.Copy(Path.Combine(SchemaMigrator.DefaultMigrationsDirectory, nome), Path.Combine(Caminho, nome));

        public void Dispose() => Directory.Delete(Caminho, recursive: true);
    }
}
