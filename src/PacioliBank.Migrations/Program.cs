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

var seedRequested = string.Equals(
    Environment.GetEnvironmentVariable("PACIOLI_SEED_LOCAL"), "true", StringComparison.OrdinalIgnoreCase);

if (seedRequested && !SchemaMigrator.ApplyLocalSeed(connectionString, logToConsole: true).Successful)
{
    return 1;
}

return 0;
