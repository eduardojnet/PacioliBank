using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Metrics;
using PacioliBank.Api.Events;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Metricas de negocio da RNF-032 (ADR-0012, card 33.2), lidas depois de uma
/// sequencia de operacoes conhecida. API e PostgreSQL reais, exportador em memoria.
/// </summary>
[Collection(LedgerCollectionDefinition.Name)]
public class MetricsTests
{
    private readonly LedgerFixture _fixture;

    public MetricsTests(LedgerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Operacoes_conhecidas_produzem_as_metricas_de_negocio_esperadas()
    {
        var conta = await _fixture.CreateAccountAsync();
        var metricas = new List<Metric>();

        await using var api = new ApiMedida(_fixture.RuntimeConnectionString, metricas);
        using var cliente = api.CreateClient();

        Assert.Equal(HttpStatusCode.Created, await Escrever(cliente, conta, "credits", "100.00", "m-1"));
        Assert.Equal(HttpStatusCode.OK, await Escrever(cliente, conta, "credits", "100.00", "m-1"));            // reenvio
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Escrever(cliente, conta, "debits", "999.00", "m-2")); // saldo
        Assert.Equal(HttpStatusCode.Created, await Escrever(cliente, conta, "debits", "10.00", "m-3"));
        (await cliente.GetAsync(new Uri($"/api/v1/accounts/{conta}/balance", UriKind.Relative))).EnsureSuccessStatusCode();
        var historica = await cliente.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>(
            new Uri($"/api/v1/accounts/{conta}/balance?asOf=2026-10-02T00:00:00Z", UriKind.Relative));
        Assert.Equal("dailyBalance", historica!["computedFrom"]!.ToString());

        api.Services.GetRequiredService<MeterProvider>().ForceFlush();
        var pendentesNoBanco = await PendentesAsync();

        // Lancamentos por tipo: so o que gravou lancamento novo
        Assert.Equal(1, Soma(metricas, "ledger.entries.recorded", ("direction", "Credit"), ("operation", "post")));
        Assert.Equal(1, Soma(metricas, "ledger.entries.recorded", ("direction", "Debit"), ("operation", "post")));

        // Reenvio idempotente: contado a parte, nao como lancamento
        Assert.Equal(1, Soma(metricas, "ledger.replays", ("operation", "post")));

        // Rejeicao por motivo, com o codigo da EF secao 8.6
        Assert.Equal(1, Soma(metricas, "ledger.rejections", ("code", "INSUFFICIENT_FUNDS"), ("operation", "post")));

        // Origem do calculo: a taxa de computedFrom=ledger sai daqui
        Assert.Equal(1, Soma(metricas, "ledger.balance.queries", ("computed_from", "Ledger"), ("point_in_time", "False")));
        Assert.Equal(1, Soma(metricas, "ledger.balance.queries", ("computed_from", "DailyBalance"), ("point_in_time", "True")));

        // Lancamentos somados alem do ponto de partida: 2 na corrente (sem
        // snapshot ainda) e 0 na historica, que parte do fechamento de 01/10
        var (quantas, somados) = Histograma(metricas, "ledger.balance.entries_replayed");
        Assert.Equal(2, quantas);
        Assert.Equal(2, somados);

        // Profundidade da fila de eventos, igual a contagem no banco
        Assert.Equal(pendentesNoBanco, Soma(metricas, "ledger.outbox.messages", ("state", "pending")));
        Assert.True(pendentesNoBanco >= 2, "Os dois lancamentos deste teste continuam na fila: o despachante foi retirado.");
    }

    private static async Task<HttpStatusCode> Escrever(HttpClient cliente, Guid conta, string operacao, string valor, string chave)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/accounts/{conta}/{operacao}")
        {
            Content = JsonContent.Create(new { amount = valor, currency = "BRL", occurredAt = "2026-10-01T12:00:00Z" }),
        };
        pedido.Headers.Add("Idempotency-Key", chave);
        using var resposta = await cliente.SendAsync(pedido);
        return resposta.StatusCode;
    }

    private async Task<long> PendentesAsync()
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        return await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM ledger.outbox_messages WHERE published_at IS NULL AND attempts < 10");
    }

    // O exportador guarda uma copia por coleta; vale a ultima de cada metrica.
    private static Metric? Ultima(List<Metric> metricas, string nome) => metricas.LastOrDefault(m => m.Name == nome);

    private static bool Tem(MetricPoint ponto, (string Chave, string Valor)[] tags)
    {
        var lidas = new Dictionary<string, string?>();
        foreach (var tag in ponto.Tags)
        {
            lidas[tag.Key] = tag.Value?.ToString();
        }

        return tags.All(t => lidas.TryGetValue(t.Chave, out var v) && v == t.Valor);
    }

    private static long Soma(List<Metric> metricas, string nome, params (string, string)[] tags)
    {
        var metrica = Ultima(metricas, nome);
        Assert.True(metrica is not null, $"Metrica ausente: {nome}");

        long total = 0;
        foreach (ref readonly var ponto in metrica!.GetMetricPoints())
        {
            if (Tem(ponto, tags))
            {
                total += metrica.MetricType is MetricType.LongGauge ? ponto.GetGaugeLastValueLong() : ponto.GetSumLong();
            }
        }

        return total;
    }

    private static (long Quantas, double Soma) Histograma(List<Metric> metricas, string nome)
    {
        var metrica = Ultima(metricas, nome);
        Assert.True(metrica is not null, $"Metrica ausente: {nome}");

        long quantas = 0;
        double soma = 0;
        foreach (ref readonly var ponto in metrica!.GetMetricPoints())
        {
            quantas += ponto.GetHistogramCount();
            soma += ponto.GetHistogramSum();
        }

        return (quantas, soma);
    }

    /// <summary>Despachante retirado: a fila fica parada e a contagem, estavel.</summary>
    private sealed class ApiMedida(string connectionString, List<Metric> metricas) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Ledger", connectionString);
            builder.ConfigureTestServices(services =>
            {
                services.Remove(services.Single(s => s.ImplementationType == typeof(OutboxDispatcherService)));
                services.ConfigureOpenTelemetryMeterProvider(m => m.AddInMemoryExporter(metricas));
            });
        }
    }
}
