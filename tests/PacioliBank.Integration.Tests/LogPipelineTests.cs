using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PacioliBank.Api.Observability;
using Serilog.Core;
using Serilog.Events;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Log estruturado, com correlacao e mascarado, pelo caminho completo: API e
/// PostgreSQL reais (RNF-031, RNF-021, ADR-0009 secao 5, card 33).
/// </summary>
/// <remarks>
/// A conta e um dado sintetico reconhecivel; o teste varre toda a saida de log
/// produzida por varias requisicoes, inclusive a linha que o despachante de
/// outbox escreve com o identificador da conta, e reprova se ele aparecer
/// integro. As entradas sao formatadas pelo mesmo formatador que escreve na
/// saida padrao.
/// </remarks>
[Collection(LedgerCollectionDefinition.Name)]
public class LogPipelineTests
{
    private const string CorrelationHeader = "X-Correlation-Id";

    private readonly LedgerFixture _fixture;

    public LogPipelineTests(LedgerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Requisicoes_geram_log_estruturado_correlacionado_e_sem_identificador_integro()
    {
        var conta = await _fixture.CreateAccountAsync();
        var captura = new Captura();

        await using (var api = new ApiDoLedger(_fixture.RuntimeConnectionString, captura))
        {
            using var cliente = api.CreateClient();
            var correlacoes = new List<string>();

            async Task Enviar(HttpRequestMessage pedido)
            {
                using var resposta = await cliente.SendAsync(pedido);
                correlacoes.Add(resposta.Headers.GetValues(CorrelationHeader).Single());
            }

            await Enviar(Escrita(conta, "credits", 100.00m, "log-1"));
            await Enviar(Escrita(conta, "debits", 999.00m, "log-2"));   // 422, saldo insuficiente
            await Enviar(new HttpRequestMessage(HttpMethod.Get, $"/api/v1/accounts/{conta}/balance"));
            await Enviar(new HttpRequestMessage(HttpMethod.Get, $"/api/v1/accounts/{conta}/entries"));

            // O despachante publica o evento do credito e registra a publicacao
            // com o identificador da conta. Espera essa linha antes de varrer.
            await captura.EsperarAsync(e => e.MessageTemplate.Text.Contains("publicado", StringComparison.OrdinalIgnoreCase)
                && e.Properties.TryGetValue("AccountId", out var id) && id.ToString().Contains(conta.ToString(), StringComparison.OrdinalIgnoreCase));

            var linhas = captura.Eventos.Select(Formatar).ToList();
            var saida = string.Concat(linhas);

            // Estruturado: cada entrada e um objeto JSON.
            Assert.NotEmpty(linhas);
            Assert.All(linhas, l => Assert.IsType<JsonObject>(JsonNode.Parse(l)));

            // Mascarado: a conta nao aparece integra em lugar nenhum da saida.
            Assert.DoesNotContain(conta.ToString(), saida, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(conta.ToString("N"), saida, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("****" + conta.ToString()[^4..], saida, StringComparison.Ordinal);

            // Correlacionado: toda entrada emitida dentro de uma requisicao traz
            // a correlacao que a requisicao devolveu no cabecalho.
            var daRequisicao = captura.Eventos.Where(e => e.Properties.ContainsKey("RequestPath")).ToList();
            Assert.NotEmpty(daRequisicao);
            Assert.All(daRequisicao, e =>
            {
                Assert.True(e.Properties.TryGetValue("CorrelationId", out var valor), $"Entrada sem correlacao: {e.MessageTemplate.Text}");
                Assert.Contains(valor!.ToString().Trim('"'), correlacoes);
            });

            // Uma linha de resumo por requisicao, com o status.
            Assert.Equal(4, daRequisicao.Count(e => e.Properties.ContainsKey("StatusCode")));
        }
    }

    private static HttpRequestMessage Escrita(Guid conta, string operacao, decimal valor, string chave)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/accounts/{conta}/{operacao}")
        {
            Content = JsonContent.Create(new { amount = valor, currency = "BRL", occurredAt = "2026-10-01T12:00:00Z" }),
        };
        pedido.Headers.Add("Idempotency-Key", chave);
        return pedido;
    }

    private static string Formatar(LogEvent evento)
    {
        using var saida = new StringWriter();
        new MaskingJsonFormatter().Format(evento, saida);
        return saida.ToString();
    }

    /// <summary>Recebe as entradas de log do pipeline real, antes da formatacao.</summary>
    private sealed class Captura : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _eventos = new();

        public IReadOnlyList<LogEvent> Eventos => _eventos.ToArray();

        public void Emit(LogEvent logEvent) => _eventos.Enqueue(logEvent);

        public async Task EsperarAsync(Func<LogEvent, bool> condicao)
        {
            for (var i = 0; i < 100 && !_eventos.Any(condicao); i++)
            {
                await Task.Delay(100);
            }

            Assert.True(_eventos.Any(condicao), "A entrada esperada nao apareceu no log em 10 s.");
        }
    }

    private sealed class ApiDoLedger(string connectionString, ILogEventSink captura) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Ledger", connectionString);
            builder.ConfigureTestServices(services => services.AddSingleton(captura));
        }
    }
}
