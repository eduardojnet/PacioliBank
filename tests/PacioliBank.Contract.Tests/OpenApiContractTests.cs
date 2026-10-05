using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PacioliBank.Api.Events;

namespace PacioliBank.Contract.Tests;

/// <summary>
/// Teste de contrato por instantaneo (ADR-0010, RNF-039).
/// </summary>
/// <remarks>
/// Compara o documento OpenAPI gerado a partir do codigo com o instantaneo
/// aprovado e versionado ao lado deste arquivo. Mudanca de contrato, acidental
/// ou nao, reprova o teste e grava o documento recebido em
/// <c>openapi.v1.received.json</c>: quem mudou o contrato de proposito revisa o
/// diff e promove o recebido a aprovado; quem mudou sem querer descobre aqui, e
/// nao no consumidor.
/// <para>
/// A comparacao e feita no proprio teste, e nao com a biblioteca Verify citada
/// no ADR-0010: um unico arquivo nao justifica a dependencia. A decisao, que e
/// comparar por instantaneo, nao muda.
/// </para>
/// </remarks>
public class OpenApiContractTests : IClassFixture<OpenApiContractTests.ApiEmMemoria>
{
    private const string Aprovado = "openapi.v1.approved.json";
    private const string Recebido = "openapi.v1.received.json";

    private static readonly JsonSerializerOptions Indentado = new() { WriteIndented = true };

    private readonly ApiEmMemoria _api;

    public OpenApiContractTests(ApiEmMemoria api)
    {
        _api = api;
    }

    [Fact]
    public async Task O_contrato_publicado_e_igual_ao_instantaneo_aprovado()
    {
        using var cliente = _api.CreateClient();
        var gerado = Normalizar(await cliente.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken));

        var pasta = PastaDoTeste();
        var aprovado = Path.Combine(pasta, Aprovado);
        var recebido = Path.Combine(pasta, Recebido);

        if (File.Exists(aprovado) && await File.ReadAllTextAsync(aprovado, TestContext.Current.CancellationToken) == gerado)
        {
            File.Delete(recebido);
            return;
        }

        await File.WriteAllTextAsync(recebido, gerado, TestContext.Current.CancellationToken);
        Assert.Fail(File.Exists(aprovado)
            ? $"O contrato mudou. Revise o diff entre {Aprovado} e {Recebido}; se a mudança é intencional, promova o recebido a aprovado."
            : $"Não há instantâneo aprovado. Revise {Recebido} e renomeie-o para {Aprovado}.");
    }

    [Fact]
    public async Task O_contrato_descreve_as_cinco_operacoes_de_negocio()
    {
        using var cliente = _api.CreateClient();
        var documento = JsonNode.Parse(await cliente.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken))!;
        var caminhos = documento["paths"]!.AsObject();

        Assert.NotNull(caminhos["/api/v1/accounts/{accountId}/credits"]?["post"]);
        Assert.NotNull(caminhos["/api/v1/accounts/{accountId}/debits"]?["post"]);
        Assert.NotNull(caminhos["/api/v1/accounts/{accountId}/entries/{entryId}/reversals"]?["post"]);
        Assert.NotNull(caminhos["/api/v1/accounts/{accountId}/balance"]?["get"]);
        Assert.NotNull(caminhos["/api/v1/accounts/{accountId}/entries"]?["get"]);

        // EF secao 8.2: valor monetario e string no contrato, nunca numero.
        var resposta = documento["components"]!["schemas"]!["PostingResponse"]!["properties"]!;
        Assert.Equal("string", resposta["amount"]!["type"]!.ToString());
        Assert.Equal("string", resposta["balanceAfter"]!["type"]!.ToString());
    }

    /// <summary>
    /// Formatacao estavel, e sem <c>servers</c>, que depende do endereco de quem
    /// chamou e nao faz parte do contrato.
    /// </summary>
    private static string Normalizar(string json)
    {
        var documento = JsonNode.Parse(json)!.AsObject();
        documento.Remove("servers");
        return documento.ToJsonString(Indentado).ReplaceLineEndings("\n") + "\n";
    }

    private static string PastaDoTeste([CallerFilePath] string arquivo = "") =>
        Path.GetDirectoryName(arquivo)!;

    /// <summary>
    /// A API em memoria. O documento OpenAPI nao toca no banco; o despachante de
    /// outbox, que tentaria conectar, e retirado.
    /// </summary>
    public sealed class ApiEmMemoria : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseSetting("ConnectionStrings:Ledger", "Host=localhost;Database=contrato;Username=contrato;Password=contrato");
            builder.ConfigureTestServices(services =>
            {
                var despachante = services.Single(s => s.ImplementationType == typeof(OutboxDispatcherService));
                services.Remove(despachante);
            });
        }
    }
}
