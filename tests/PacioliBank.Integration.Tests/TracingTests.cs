using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Rastreamento ponta a ponta: requisicao, caso de uso e banco no mesmo traco
/// (RNF-030, ADR-0012, card 33.1). API e PostgreSQL reais, exportador em memoria.
/// </summary>
[Collection(LedgerCollectionDefinition.Name)]
public class TracingTests
{
    private readonly LedgerFixture _fixture;

    public TracingTests(LedgerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Escrita_produz_span_da_requisicao_do_caso_de_uso_e_do_banco_no_mesmo_traco()
    {
        var conta = await _fixture.CreateAccountAsync();
        var spans = new List<Activity>();

        await using var api = new ApiRastreada(_fixture.RuntimeConnectionString, spans);
        using var cliente = api.CreateClient();

        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/accounts/{conta}/credits")
        {
            Content = JsonContent.Create(new { amount = "10.00", currency = "BRL", occurredAt = "2026-10-01T12:00:00Z" }),
        };
        pedido.Headers.Add("Idempotency-Key", "traco-1");
        using (var resposta = await cliente.SendAsync(pedido))
        {
            resposta.EnsureSuccessStatusCode();
        }

        // O despachante de outbox consulta o banco a cada segundo, fora de
        // qualquer requisicao. Espera ao menos uma consulta dele.
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        api.Services.GetRequiredService<TracerProvider>().ForceFlush();

        // Comando fora de um traco nao vira traco proprio: sem isso, cada
        // consulta do despachante seria um traco de um span so, por segundo.
        Assert.DoesNotContain(spans, s => s.Source.Name == "Npgsql" && s.ParentSpanId == default);

        // Requisicao HTTP
        var requisicao = Assert.Single(spans, s => s.Kind == ActivityKind.Server && s.DisplayName.Contains("credits", StringComparison.Ordinal));

        // Caso de uso, filho da requisicao
        var casoDeUso = Assert.Single(spans, s => s.Source.Name == "PacioliBank.Ledger" && s.OperationName == "ledger.post");
        Assert.Equal(requisicao.TraceId, casoDeUso.TraceId);
        Assert.Equal(requisicao.SpanId, casoDeUso.ParentSpanId);
        Assert.Equal("Credit", casoDeUso.GetTagItem("ledger.direction"));

        // Banco, filho do caso de uso
        var banco = spans.Where(s => s.Source.Name == "Npgsql" && s.TraceId == requisicao.TraceId).ToList();
        Assert.NotEmpty(banco);
        Assert.All(banco, s => Assert.Equal(casoDeUso.SpanId, s.ParentSpanId));

        // Nenhum identificador de conta integro no que seria exportado
        var doTraco = spans.Where(s => s.TraceId == requisicao.TraceId).ToList();
        var textos = doTraco.SelectMany(s => s.TagObjects.Select(t => t.Value?.ToString() ?? string.Empty).Append(s.DisplayName));
        Assert.All(textos, t => Assert.DoesNotContain(conta.ToString(), t, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(doTraco, s => (s.GetTagItem("url.path") as string)?.Contains("****" + conta.ToString()[^4..], StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Rejeicao_marca_o_span_do_caso_de_uso_com_o_tipo_da_rejeicao()
    {
        var conta = await _fixture.CreateAccountAsync();
        var spans = new List<Activity>();

        await using var api = new ApiRastreada(_fixture.RuntimeConnectionString, spans);
        using var cliente = api.CreateClient();

        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/accounts/{conta}/debits")
        {
            Content = JsonContent.Create(new { amount = "10.00", currency = "BRL", occurredAt = "2026-10-01T12:00:00Z" }),
        };
        pedido.Headers.Add("Idempotency-Key", "traco-2");
        using (await cliente.SendAsync(pedido))
        {
        }

        api.Services.GetRequiredService<TracerProvider>().ForceFlush();

        var casoDeUso = Assert.Single(spans, s => s.Source.Name == "PacioliBank.Ledger" && s.OperationName == "ledger.post");
        Assert.Equal(ActivityStatusCode.Error, casoDeUso.Status);
        Assert.Equal("InsufficientFundsException", casoDeUso.GetTagItem("ledger.rejection"));
    }

    private sealed class ApiRastreada(string connectionString, List<Activity> spans) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Ledger", connectionString);
            builder.ConfigureTestServices(services =>
                services.ConfigureOpenTelemetryTracerProvider(traces => traces.AddInMemoryExporter(spans)));
        }
    }
}
