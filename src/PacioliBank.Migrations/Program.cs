using Npgsql;
using PacioliBank.Migrations;

// Passo de migracao do docker compose: aplica o que falta e termina. O codigo
// de saida decide se a API sobe (service_completed_successfully).
//
//   ConnectionStrings__Migrator  conexao com o papel de migracao (obrigatoria)
//   PACIOLI_SEED_LOCAL=true      aplica tambem a massa do ambiente local

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Migrator");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("ConnectionStrings__Migrator nao informada.");
    return 2;
}

var schema = SchemaMigrator.ApplySchema(connectionString, logToConsole: true);
if (!schema.Successful)
{
    return 1;
}

// Particoes mensais do ledger a frente do tempo (ADR-0013). Falha aqui
// impede a API de subir: melhor do que descobrir a particao faltando depois.
try
{
    foreach (var partition in SchemaMigrator.EnsurePartitions(connectionString))
    {
        Console.WriteLine($"Particao criada: ledger.{partition}");
    }
}
catch (PostgresException ex)
{
    Console.Error.WriteLine($"Falha ao criar particoes do ledger: {ex.MessageText}");
    return 1;
}

var seedRequested = string.Equals(
    Environment.GetEnvironmentVariable("PACIOLI_SEED_LOCAL"), "true", StringComparison.OrdinalIgnoreCase);

if (seedRequested && !SchemaMigrator.ApplyLocalSeed(connectionString, logToConsole: true).Successful)
{
    return 1;
}

return 0;
