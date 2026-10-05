using Dapper;
using Npgsql;
using PacioliBank.Migrations;
using Testcontainers.PostgreSql;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Sobe um PostgreSQL real por execucao e aplica as MESMAS migracoes que o
/// ambiente local usa, pelo mesmo migrador (ADR-0002).
/// </summary>
/// <remarks>
/// Banco real nao e preciosismo (ADR-0010). As invariantes que importam neste
/// sistema nao residem no codigo isolado: residem na interacao entre o codigo e
/// o banco. Posicao nao negativa depende de bloqueio de linha; sequencia sem
/// lacunas depende de constraint; idempotencia sob envio simultaneo depende da
/// violacao de chave primaria; imutabilidade depende de privilegio. Nenhuma
/// delas existe em um repositorio em memoria, e um teste de concorrencia
/// executado contra memoria PASSA na implementacao ingenua.
/// </remarks>
public sealed class LedgerFixture : IAsyncLifetime
{
    private const string Database = "pacioli";
    private const string MigratorUser = "pacioli_migrator";
    private const string RuntimeUser = "pacioli_runtime";
    private const string LocalPassword = "pacioli_local_dev";

    // A imagem vai no construtor: o construtor sem parametros foi marcado
    // obsoleto no Testcontainers 4.15, e TreatWarningsAsErrors transforma isso
    // em erro de build antes que vire divida.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase(Database)
        .WithUsername(MigratorUser)
        .WithPassword(LocalPassword)
        .Build();

    private NpgsqlDataSource? _runtimeDataSource;
    private string? _runtimeConnectionString;

    /// <summary>Conexao com o papel da aplicacao, sem UPDATE nem DELETE no ledger.</summary>
    public NpgsqlDataSource RuntimeDataSource =>
        _runtimeDataSource ?? throw new InvalidOperationException("A fixture nao foi inicializada.");

    /// <summary>Connection string do papel da aplicacao, para subir a API real nos testes.</summary>
    public string RuntimeConnectionString =>
        _runtimeConnectionString ?? throw new InvalidOperationException("A fixture nao foi inicializada.");

    /// <summary>Conexao administrativa, usada apenas para preparar massa de teste.</summary>
    public string MigratorConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);

        var migration = SchemaMigrator.ApplySchema(MigratorConnectionString);
        if (!migration.Successful)
        {
            throw new InvalidOperationException("As migracoes nao foram aplicadas ao banco de teste.", migration.Error);
        }

        var runtime = new NpgsqlConnectionStringBuilder(MigratorConnectionString)
        {
            Username = RuntimeUser,
            Password = LocalPassword,
        };

        _runtimeConnectionString = runtime.ConnectionString;
        _runtimeDataSource = NpgsqlDataSource.Create(runtime.ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        if (_runtimeDataSource is not null)
        {
            await _runtimeDataSource.DisposeAsync().ConfigureAwait(false);
        }

        await _container.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Cria uma conta ativa e devolve o identificador.</summary>
    public async Task<Guid> CreateAccountAsync(decimal openingCredit = 0m)
    {
        var accountId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await connection.ExecuteAsync(
            """
            INSERT INTO ledger.accounts (account_id, customer_id, currency, status, last_sequence)
            VALUES (@accountId, @customerId, 'BRL', 1, 0)
            """,
            new { accountId, customerId = Guid.NewGuid() }).ConfigureAwait(false);

        if (openingCredit > 0m)
        {
            // Abertura gravada pelo proprio caminho de escrita, para que a massa
            // de teste nasca consistente com as invariantes do sistema.
            await connection.ExecuteAsync(
                """
                INSERT INTO ledger.entry_keys
                    (entry_id, account_id, sequence, idempotency_key, reversal_of, recorded_at)
                VALUES
                    (@entryId, @accountId, 1, @key, NULL, now());

                INSERT INTO ledger.ledger_entries
                    (entry_id, account_id, sequence, direction, amount, currency,
                     occurred_at, recorded_at, idempotency_key, correlation_id, balance_after)
                VALUES
                    (@entryId, @accountId, 1, 1, @amount, 'BRL',
                     now(), now(), @key, @correlationId, @amount);

                INSERT INTO ledger.daily_balances (account_id, day, closing_balance, last_sequence)
                VALUES (@accountId, (now() AT TIME ZONE 'UTC')::date, @amount, 1);

                UPDATE ledger.accounts SET last_sequence = 1 WHERE account_id = @accountId;
                """,
                new
                {
                    entryId = Guid.NewGuid(),
                    accountId,
                    amount = openingCredit,
                    key = "abertura-" + accountId.ToString("N"),
                    correlationId = Guid.NewGuid(),
                }).ConfigureAwait(false);
        }

        return accountId;
    }

    /// <summary>
    /// Cria um banco vazio no mesmo servidor e devolve a conexao de migrador
    /// para ele. Os papeis sao do servidor, nao do banco: a migracao os cria
    /// so se ainda nao existirem.
    /// </summary>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = "vazio_" + Guid.NewGuid().ToString("N");

        await using (var admin = new NpgsqlConnection(MigratorConnectionString))
        {
            await admin.OpenAsync().ConfigureAwait(false);
            await admin.ExecuteAsync($"CREATE DATABASE {name}").ConfigureAwait(false);
        }

        return new NpgsqlConnectionStringBuilder(MigratorConnectionString) { Database = name }.ConnectionString;
    }
}

/// <summary>Compartilha um unico container entre todas as classes de teste.</summary>
[CollectionDefinition(Name)]
public sealed class LedgerCollectionDefinition : ICollectionFixture<LedgerFixture>
{
    public const string Name = "ledger";
}
