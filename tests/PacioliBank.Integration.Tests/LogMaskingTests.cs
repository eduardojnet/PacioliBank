using System.Text.Json.Nodes;
using PacioliBank.Api.Observability;
using Serilog.Events;
using Serilog.Parsing;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Mascaramento do log (ADR-0009 secao 5, RNF-021), no formatador que escreve
/// a saida. Sem banco: o que se verifica e o texto produzido.
/// </summary>
public class LogMaskingTests
{
    private static readonly Guid Conta = Guid.Parse("c0a1c0a1-0000-4000-8000-00000000abcd");
    private static readonly Guid Correlacao = Guid.Parse("c0e1c0e1-1111-4111-8111-111111112222");

    private static string Formatar(string template, Exception? excecao = null, params (string Nome, object? Valor)[] propriedades)
    {
        var evento = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            excecao,
            new MessageTemplateParser().Parse(template),
            propriedades.Select(p => new LogEventProperty(p.Nome, new ScalarValue(p.Valor))));

        using var saida = new StringWriter();
        new MaskingJsonFormatter().Format(evento, saida);
        return saida.ToString();
    }

    [Fact]
    public void Identificador_de_conta_e_truncado_na_propriedade_e_dentro_de_texto()
    {
        var linha = Formatar(
            "Lancamento na conta {AccountId} por {RequestPath}",
            null,
            ("AccountId", Conta),
            ("RequestPath", $"/api/v1/accounts/{Conta}/credits"));

        Assert.DoesNotContain(Conta.ToString(), linha, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("****abcd", linha, StringComparison.Ordinal);
    }

    [Fact]
    public void Correlacao_e_identificador_de_mensagem_ficam_integros()
    {
        // Nao identificam pessoa e sao o que liga o log a requisicao e ao evento.
        var mensagem = Guid.NewGuid();
        var linha = Formatar(
            "Evento {MessageId} publicado",
            null,
            ("MessageId", mensagem),
            ("CorrelationId", Correlacao));

        Assert.Contains(mensagem.ToString(), linha, StringComparison.Ordinal);
        Assert.Contains(Correlacao.ToString(), linha, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("123.456.789-09", "[CPF]")]
    [InlineData("12345678909", "[CPF]")]
    [InlineData("Bearer abc.DEF-123_xyz", "Bearer [TOKEN]")]
    public void Cpf_e_token_sao_substituidos_por_marcador(string sensivel, string marcador)
    {
        var linha = Formatar("Recebido {Valor}", null, ("Valor", $"antes {sensivel} depois"));

        Assert.DoesNotContain(sensivel, linha, StringComparison.Ordinal);
        Assert.Contains(marcador, linha, StringComparison.Ordinal);
    }

    [Fact]
    public void Jwt_e_substituido_por_marcador()
    {
        // Token sintetico, montado em tempo de execucao: escrito inteiro no
        // fonte, a varredura de segredos do CI o toma por credencial real.
        // Cabecalho {"alg":"HS256"}, corpo {"sub":"1234"}, assinatura ficticia.
        var jwt = string.Join('.', "eyJhbGciOiJIUzI1NiJ9", "eyJzdWIiOiIxMjM0In0", "c2lnbmF0dXJh");

        var linha = Formatar("Recebido {Valor}", null, ("Valor", $"antes {jwt} depois"));

        Assert.DoesNotContain(jwt, linha, StringComparison.Ordinal);
        Assert.Contains("[TOKEN]", linha, StringComparison.Ordinal);
    }

    [Fact]
    public void Texto_de_excecao_tambem_e_mascarado()
    {
        // Um enriquecedor so alcanca as propriedades; o texto da excecao vai
        // para a saida por outro caminho, e e o formatador que o ve.
        var linha = Formatar("Falha", new InvalidOperationException($"A conta '{Conta}' falhou"));

        Assert.DoesNotContain(Conta.ToString(), linha, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("****abcd", linha, StringComparison.Ordinal);
    }

    [Fact]
    public void Cada_entrada_e_uma_linha_de_JSON()
    {
        var linha = Formatar("Conta {AccountId}", null, ("AccountId", Conta));

        Assert.EndsWith("\n", linha, StringComparison.Ordinal);
        Assert.Single(linha.TrimEnd('\n').Split('\n'));
        Assert.IsType<JsonObject>(JsonNode.Parse(linha));
    }
}
