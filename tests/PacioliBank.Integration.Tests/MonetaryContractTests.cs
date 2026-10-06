using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Valor monetario no contrato HTTP (EF secao 8.2): numero JSON na entrada e
/// na saida, com exatamente as casas da moeda. API e PostgreSQL reais.
/// </summary>
/// <remarks>
/// O corpo e escrito como texto, e nao serializado de um objeto, para que o
/// teste controle o que vai no fio: <c>"150.00"</c> entre aspas e
/// <c>150.00</c> sem aspas sao requisicoes diferentes.
/// </remarks>
[Collection(LedgerCollectionDefinition.Name)]
public class MonetaryContractTests
{
    private readonly LedgerFixture _fixture;

    public MonetaryContractTests(LedgerFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<HttpResponseMessage> Escrever(HttpClient cliente, Guid conta, string operacao, string valorNoFio, string chave)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/accounts/{conta}/{operacao}")
        {
            Content = new StringContent(
                $$"""{"amount":{{valorNoFio}},"currency":"BRL","occurredAt":"2026-10-01T12:00:00Z"}""",
                Encoding.UTF8,
                "application/json"),
        };
        pedido.Headers.Add("Idempotency-Key", chave);
        return await cliente.SendAsync(pedido, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonNode> Corpo(HttpResponseMessage resposta) =>
        JsonNode.Parse(await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

    [Theory]
    [InlineData("\"150.00\"")]
    [InlineData("\"150\"")]
    public async Task Valor_enviado_como_texto_e_recusado_sem_gravar(string valorNoFio)
    {
        var conta = await _fixture.CreateAccountAsync();
        await using var api = new Api(_fixture.RuntimeConnectionString);
        using var cliente = api.CreateClient();

        using var resposta = await Escrever(cliente, conta, "credits", valorNoFio, "texto-1");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("INVALID_REQUEST", (await Corpo(resposta))["code"]!.ToString());

        using var posicao = await cliente.GetAsync($"/api/v1/accounts/{conta}/balance", TestContext.Current.CancellationToken);
        Assert.Equal("0.00", (await Corpo(posicao))["balance"]!.ToJsonString());
    }

    [Fact]
    public async Task Transferencia_com_valor_em_texto_e_recusada()
    {
        var origem = await _fixture.CreateAccountAsync(100.00m);
        var destino = await _fixture.CreateAccountAsync();
        await using var api = new Api(_fixture.RuntimeConnectionString);
        using var cliente = api.CreateClient();

        using var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transfers")
        {
            Content = new StringContent(
                $$"""{"sourceAccountId":"{{origem}}","destinationAccountId":"{{destino}}","amount":"40.00","currency":"BRL","occurredAt":"2026-10-05T12:00:00Z"}""",
                Encoding.UTF8,
                "application/json"),
        };
        pedido.Headers.Add("Idempotency-Key", "texto-tr");
        using var resposta = await cliente.SendAsync(pedido, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("INVALID_REQUEST", (await Corpo(resposta))["code"]!.ToString());
    }

    [Fact]
    public async Task Numero_com_mais_casas_que_a_moeda_e_recusado_como_valor_invalido()
    {
        var conta = await _fixture.CreateAccountAsync();
        await using var api = new Api(_fixture.RuntimeConnectionString);
        using var cliente = api.CreateClient();

        using var resposta = await Escrever(cliente, conta, "credits", "10.001", "escala-1");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("INVALID_AMOUNT", (await Corpo(resposta))["code"]!.ToString());
    }

    [Fact]
    public async Task Valores_saem_como_numero_com_as_casas_da_moeda_em_todas_as_respostas()
    {
        var conta = await _fixture.CreateAccountAsync();
        await using var api = new Api(_fixture.RuntimeConnectionString);
        using var cliente = api.CreateClient();

        // 150 sem casas na entrada: a saida tem as duas casas do BRL.
        using var credito = await Escrever(cliente, conta, "credits", "150", "num-1");
        using var reenvio = await Escrever(cliente, conta, "credits", "150.00", "num-1");
        using var semSaldo = await Escrever(cliente, conta, "debits", "999.5", "num-2");
        using var posicao = await cliente.GetAsync($"/api/v1/accounts/{conta}/balance", TestContext.Current.CancellationToken);
        using var extrato = await cliente.GetAsync($"/api/v1/accounts/{conta}/entries", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, credito.StatusCode);
        var escrita = await Corpo(credito);
        Assert.Equal("150.00", escrita["amount"]!.ToJsonString());
        Assert.Equal("150.00", escrita["balanceAfter"]!.ToJsonString());

        // ADR-0006: 150 e 150.00 sao o mesmo comando, e nao conflito de chave.
        Assert.Equal(HttpStatusCode.OK, reenvio.StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, semSaldo.StatusCode);
        var problema = await Corpo(semSaldo);
        Assert.Equal("150.00", problema["availableBalance"]!.ToJsonString());
        Assert.Equal("999.50", problema["requestedAmount"]!.ToJsonString());

        Assert.Equal("150.00", (await Corpo(posicao))["balance"]!.ToJsonString());

        var linha = (await Corpo(extrato))["entries"]![0]!;
        Assert.Equal("150.00", linha["amount"]!.ToJsonString());
        Assert.Equal("150.00", linha["balanceAfter"]!.ToJsonString());
    }

    private sealed class Api(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("ConnectionStrings:Ledger", connectionString);
    }
}
