using Npgsql;
using PacioliBank.Api.Endpoints;
using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Raiz de composicao: unico lugar que conhece todas as camadas. O dominio nao
// referencia Npgsql e o adaptador HTTP nao referencia o store; a ligacao entre
// eles acontece aqui e so aqui (ADR-0001).
var connectionString = builder.Configuration.GetConnectionString("Ledger")
    ?? throw new InvalidOperationException(
        "Connection string 'Ledger' ausente. Defina ConnectionStrings__Ledger ou appsettings.");

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ILedgerStore, PostgresLedgerStore>();
builder.Services.AddSingleton<ILedgerService, LedgerService>();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull);

// Falha de leitura de corpo ou parametro vira excecao, e a excecao vira o
// mesmo problem+json dos demais erros. Sem isso, o framework responde 400
// sem corpo em producao, e o chamador fica sem codigo estavel.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = LedgerProblems.Customize);
builder.Services.AddExceptionHandler<LedgerProblems>();

var app = builder.Build();

app.UseCorrelation();
app.UseExceptionHandler();
app.UseStatusCodePages();

// RNF-013: vivo e pronto sao verificacoes distintas.
// Vivo nao depende de nenhuma dependencia externa.
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

// Pronto reflete o armazenamento primario: sem ele, nenhuma operacao do ledger
// e possivel, e o orquestrador deve tirar a instancia do balanceamento.
app.MapGet("/health/ready", async (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
{
    try
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        await using var command = dataSource.CreateCommand("SELECT 1");
        await command.ExecuteScalarAsync(timeout.Token);

        return Results.Ok(new { status = "ready" });
    }
    catch (Exception ex) when (ex is NpgsqlException or OperationCanceledException)
    {
        return Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapLedgerEndpoints();

app.Run();
