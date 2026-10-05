using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

namespace PacioliBank.Api.Observability;

/// <summary>
/// Escreve cada entrada de log como uma linha de JSON estruturado (RNF-031),
/// mascarando identificadores e dados sensiveis (ADR-0009 secao 5, RNF-021).
/// </summary>
/// <remarks>
/// O mascaramento acontece aqui, sobre a linha ja produzida, e nao num
/// enriquecedor de propriedades, como o ADR-0009 sugeria: o enriquecedor nao
/// alcanca a mensagem renderizada nem o texto de uma excecao, e um
/// identificador vaza por qualquer um dos dois (ADR-0012).
/// <list type="bullet">
///   <item>Todo GUID, em qualquer campo de texto, vira os quatro ultimos
///   caracteres. Dentro de um caminho de requisicao nao ha como saber qual
///   GUID e conta e qual e lancamento; trunca-se todos.</item>
///   <item>CPF e token viram marcador.</item>
///   <item>Ficam integros so os campos que nao identificam pessoa e que ligam
///   o log ao resto: correlacao, mensagem da outbox, traco.</item>
/// </list>
/// </remarks>
public sealed partial class MaskingJsonFormatter : ITextFormatter
{
    private static readonly HashSet<string> Preserved = new(StringComparer.Ordinal)
    {
        "@t", "@l", "@i", "CorrelationId", "MessageId", "TraceId", "SpanId", "ParentId",
    };

    private readonly RenderedCompactJsonFormatter _inner = new();

    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        using var buffer = new StringWriter();
        _inner.Format(logEvent, buffer);

        var line = JsonNode.Parse(buffer.ToString())!.AsObject();
        Mask(line);

        output.Write(line.ToJsonString(JsonOptions));
        output.Write('\n');
    }

    /// <summary>Mascara um texto livre. Exposto para quem precisar do mesmo criterio.</summary>
    public static string MaskText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var masked = GuidPattern().Replace(value, m => "****" + m.Value[^4..]);
        masked = BearerPattern().Replace(masked, "Bearer [TOKEN]");
        masked = JwtPattern().Replace(masked, "[TOKEN]");
        return CpfPattern().Replace(masked, "[CPF]");
    }

    private static void Mask(JsonObject node)
    {
        foreach (var (key, value) in node.ToList())
        {
            if (Preserved.Contains(key))
            {
                continue;
            }

            node[key] = MaskNode(value);
        }
    }

    private static JsonNode? MaskNode(JsonNode? value)
    {
        switch (value)
        {
            case JsonObject obj:
                Mask(obj);
                return obj;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    array[i] = MaskNode(array[i]?.DeepClone());
                }

                return array;

            case JsonValue scalar when scalar.TryGetValue<string>(out var text):
                return JsonValue.Create(MaskText(text));

            default:
                return value?.DeepClone();
        }
    }

    // Sem escapar acentos nem aspas tipograficas: o log e lido por gente.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*")]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*")]
    private static partial Regex JwtPattern();

    [GeneratedRegex(@"(?<!\d)\d{3}\.?\d{3}\.?\d{3}-?\d{2}(?!\d)")]
    private static partial Regex CpfPattern();
}
