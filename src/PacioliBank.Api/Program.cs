using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using Npgsql;
using PacioliBank.Api.Endpoints;
using PacioliBank.Api.Events;
using PacioliBank.Api.Observability;
using PacioliBank.Events;
using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Raiz de composicao: unico lugar que conhece todas as camadas. O dominio nao
// referencia Npgsql e o adaptador HTTP nao referencia o store; a ligacao entre
// eles acontece aqui e so aqui (ADR-0001).
var connectionString = builder.Configuration.GetConnectionString("Ledger")
    ?? throw new InvalidOperationException(
        "Connection string 'Ledger' ausente. Defina ConnectionStrings__Ledger ou appsettings.");

// Log estruturado em JSON na saida padrao, uma linha por entrada, com a
// correlacao da requisicao e mascarado (RNF-031, ADR-0009 secao 5, ADR-0012).
// Os niveis vem da secao Serilog da configuracao: com o Serilog no lugar da
// fabrica de log, a secao Logging deixa de valer. O hosting fica em Warning:
// suas linhas de inicio e fim sairiam antes da correlacao existir.
// ReadFrom.Services deixa os testes acrescentarem um destino.
builder.Services.AddSerilog((services, log) => log
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new MaskingJsonFormatter()));

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ILedgerStore, PostgresLedgerStore>();
builder.Services.AddSingleton<ILedgerService, LedgerService>();

// Despachante de outbox (ADR-0008), no mesmo processo por decisao operacional.
// O publicador registra em log ate a plataforma de mensageria ser definida.
builder.Services.AddSingleton(builder.Configuration.GetSection("Outbox").Get<OutboxDispatcherOptions>() ?? new OutboxDispatcherOptions());
builder.Services.AddSingleton<IEventPublisher, LoggingEventPublisher>();
builder.Services.AddSingleton<OutboxDispatcher>();
builder.Services.AddHostedService<OutboxDispatcherService>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

    // Numero e numero, nunca texto. O padrao da web aceita ler numero escrito
    // como string, e o OpenAPI passaria a declarar "inteiro ou string". Nenhum
    // corpo de requisicao deste contrato tem campo numerico: valor monetario
    // ja e string por decisao (EF secao 8.2).
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

// Falha de leitura de corpo ou parametro vira excecao, e a excecao vira o
// mesmo problem+json dos demais erros. Sem isso, o framework responde 400
// sem corpo em producao, e o chamador fica sem codigo estavel.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

// Contrato publicado em /openapi/v1.json, gerado a partir do codigo. E o que o
// teste de contrato compara com o instantaneo aprovado (ADR-0010, RNF-039).
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "PacioliBank Ledger API";
        document.Info.Description = "Livro-razão de contas correntes. Contrato descrito na EF §8; valores monetários como string, instantes em ISO 8601 UTC.";
        return Task.CompletedTask;
    });

    // Campo anulavel e omitido quando nulo (WhenWritingNull, acima), entao nao
    // e obrigatorio. Sem esta regra, o documento declararia reversalOf como
    // sempre presente, e o credito o omite: contrato que a API nao cumpre.
    options.AddSchemaTransformer((schema, _, _) =>
    {
        if (schema.Properties is null || schema.Required is null)
        {
            return Task.CompletedTask;
        }

        foreach (var (nome, propriedade) in schema.Properties)
        {
            if (propriedade.Type is { } tipo && tipo.HasFlag(JsonSchemaType.Null))
            {
                schema.Required.Remove(nome);
            }
        }

        return Task.CompletedTask;
    });
});

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = LedgerProblems.Customize);
builder.Services.AddExceptionHandler<LedgerProblems>();

var app = builder.Build();

app.UseCorrelation();
app.UseLogCorrelation();

// Uma linha por requisicao: metodo, caminho, status e duracao, ja com a
// correlacao. Substitui as linhas de inicio e fim do hosting, que sairiam
// antes da correlacao existir.
app.UseSerilogRequestLogging();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Painel de evidencia (ADR-0011): pagina estatica em wwwroot, servida na raiz.
// Consome a mesma API publica que qualquer integrador, sem endpoint proprio.
app.UseDefaultFiles();
app.UseStaticFiles();

// RNF-013: vivo e pronto sao verificacoes distintas.
// Vivo nao depende de nenhuma dependencia externa.
app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).WithName("Vivo");

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
}).WithName("Pronto");

app.MapLedgerEndpoints();
app.MapOpenApi();

app.Run();

/// <summary>Exposto para o teste de contrato subir a API em memoria.</summary>
public partial class Program;
