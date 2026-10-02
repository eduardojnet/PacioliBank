namespace PacioliBank.Api.Endpoints;

/// <summary>
/// Identificador de correlacao da requisicao (EF secao 8.1): aceito do
/// chamador quando legivel, gerado quando ausente, sempre devolvido.
/// </summary>
public static class Correlation
{
    private static readonly object ItemKey = new();

    /// <summary>Middleware: resolve o identificador e garante o cabecalho na resposta.</summary>
    public static IApplicationBuilder UseCorrelation(this IApplicationBuilder app) =>
        app.Use(async (http, next) =>
        {
            var id = Guid.TryParse(http.Request.Headers[LedgerHeaders.CorrelationId], out var received)
                ? received
                : Guid.NewGuid();

            http.Items[ItemKey] = id;

            // OnStarting, e nao escrita direta: o tratador de excecao limpa os
            // cabecalhos da resposta, e a correlacao precisa sobreviver ao erro,
            // que e justamente quando ela e mais procurada.
            http.Response.OnStarting(() =>
            {
                http.Response.Headers[LedgerHeaders.CorrelationId] = id.ToString();
                return Task.CompletedTask;
            });

            await next(http);
        });

    /// <summary>Identificador da requisicao corrente.</summary>
    public static Guid Of(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);

        return http.Items.TryGetValue(ItemKey, out var value) && value is Guid id ? id : Guid.Empty;
    }
}
