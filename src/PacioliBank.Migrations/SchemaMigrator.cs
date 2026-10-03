using DbUp;
using DbUp.Builder;

namespace PacioliBank.Migrations;

/// <summary>Resultado de uma execucao: o que foi aplicado agora, ou o erro.</summary>
public sealed record MigrationResult(bool Successful, IReadOnlyList<string> Applied, Exception? Error);

/// <summary>
/// Aplica os scripts SQL versionados que ainda nao rodaram neste banco
/// (ADR-0002, RNF-038).
/// </summary>
/// <remarks>
/// O DbUp registra cada script aplicado num diario, e a execucao seguinte so
/// aplica o que falta: rodar de novo nao faz nada, e mudanca de esquema e um
/// script novo, sem recriar o banco.
/// <para>
/// Roda com o papel de migracao, num passo separado que termina antes de a API
/// subir. A API continua com o papel que so le e insere: a credencial capaz de
/// alterar o ledger nunca fica com o processo em execucao (ADR-0009).
/// </para>
/// </remarks>
public static class SchemaMigrator
{
    private const string JournalSchema = "public";

    // Diarios separados: a massa local e opcional e nao pode ocupar a ordem
    // nem o nome das migracoes de esquema.
    private const string SchemaJournal = "schema_versions";
    private const string SeedJournal = "seed_versions";

    public static string DefaultMigrationsDirectory => Path.Combine(AppContext.BaseDirectory, "migrations");

    public static string DefaultSeedDirectory => Path.Combine(AppContext.BaseDirectory, "seed");

    /// <summary>Aplica as migracoes de esquema pendentes.</summary>
    public static MigrationResult ApplySchema(string connectionString, string? directory = null, bool logToConsole = false) =>
        Apply(connectionString, directory ?? DefaultMigrationsDirectory, SchemaJournal, logToConsole);

    /// <summary>Aplica a massa do ambiente local. Nunca usada fora dele.</summary>
    public static MigrationResult ApplyLocalSeed(string connectionString, string? directory = null, bool logToConsole = false) =>
        Apply(connectionString, directory ?? DefaultSeedDirectory, SeedJournal, logToConsole);

    private static MigrationResult Apply(string connectionString, string directory, string journal, bool logToConsole)
    {
        var builder = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsFromFileSystem(directory)
            .JournalToPostgresqlTable(JournalSchema, journal)
            // Script com erro desfaz tudo o que fez e nao entra no diario.
            .WithTransactionPerScript()
            // O DbUp leria $nome$ como variavel e recusaria o script; no
            // PostgreSQL, $nome$ e delimitador de bloco e deve chegar intacto.
            .WithVariablesDisabled();

        builder = logToConsole ? builder.LogToConsole() : builder.LogToNowhere();

        var result = builder.Build().PerformUpgrade();

        return new MigrationResult(
            result.Successful,
            result.Scripts.Select(script => script.Name).ToArray(),
            result.Error);
    }
}
