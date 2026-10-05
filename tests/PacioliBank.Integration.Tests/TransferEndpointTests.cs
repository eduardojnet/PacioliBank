using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Contrato HTTP da transferencia (EF secao 8, RF-012): codigos de status,
/// repeticao idempotente e codigos de rejeicao. API e PostgreSQL reais.
/// </summary>
[Collection(LedgerCollectionDefinition.Name)]
public class TransferEndpointTests
{
    private readonly LedgerFixture _fixture;

    public TransferEndpointTests(LedgerFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<HttpResponseMessage> Transferir(HttpClient cliente, object corpo, string chave)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transfers") { Content = JsonContent.Create(corpo) };
        pedido.Headers.Add("Idempotency-Key", chave);
        return await cliente.SendAsync(pedido, TestContext.Current.CancellationToken);
    }

    private static object Corpo(Guid origem, Guid destino, string valor = "40.00") =>
        new { sourceAccountId = origem, destinationAccountId = destino, amount = valor, currency = "BRL", occurredAt = "2026-10-05T12:00:00Z" };

    private static async Task<string?> CodigoAsync(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken))?["code"]?.ToString();

    [Fact]
    public async Task Transferencia_responde_201_e_o_reenvio_200_com_o_mesmo_corpo()
    {
        var origem = await _fixture.CreateAccountAsync(100.00m);
        var destino = await _fixture.CreateAccountAsync();
        await using var api = new Api(_fixture.RuntimeConnectionString);
        using var cliente = api.CreateClient();

        using var primeira = await Transferir(cliente, Corpo(origem, destino), "h-1");
        var corpo = await primeira.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var segunda = await Transferir(cliente, Corpo(origem, destino), "h-1");

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        var json = JsonNode.Parse(corpo)!;
        Assert.Equal(origem.ToString(), json["sourceAccountId"]!.ToString());
        Assert.Equal(destino.ToString(), json["destinationAccountId"]!.ToString());
        Assert.Equal("40.00", json["amount"]!.ToString());
        Assert.Equal("60.00", json["debit"]!["balanceAfter"]!.ToString());
        Assert.Null(json["credit"]!["balanceAfter"]);

        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.Equal("true", segunda.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(corpo, await segunda.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejeicoes_saem_com_os_codigos_do_contrato()
    {
        var origem = await _fixture.CreateAccountAsync(10.00m);
        var destino = await _fixture.CreateAccountAsync();
        await using var api = new Api(_fixture.RuntimeConnectionString);
        using var cliente = api.CreateClient();

        using var mesmaConta = await Transferir(cliente, Corpo(origem, origem), "h-2");
        using var semSaldo = await Transferir(cliente, Corpo(origem, destino, "10.01"), "h-3");
        using var semDestino = await Transferir(cliente, Corpo(origem, Guid.NewGuid()), "h-4");
        using var semCampo = await Transferir(cliente, new { sourceAccountId = origem, amount = "1.00", currency = "BRL" }, "h-5");

        Assert.Equal(HttpStatusCode.BadRequest, mesmaConta.StatusCode);
        Assert.Equal("SAME_ACCOUNT_TRANSFER", await CodigoAsync(mesmaConta));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, semSaldo.StatusCode);
        Assert.Equal("INSUFFICIENT_FUNDS", await CodigoAsync(semSaldo));
        Assert.Equal(HttpStatusCode.NotFound, semDestino.StatusCode);
        Assert.Equal("ACCOUNT_NOT_FOUND", await CodigoAsync(semDestino));
        Assert.Equal(HttpStatusCode.BadRequest, semCampo.StatusCode);
        Assert.Equal("INVALID_REQUEST", await CodigoAsync(semCampo));
    }

    private sealed class Api(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("ConnectionStrings:Ledger", connectionString);
    }
}
