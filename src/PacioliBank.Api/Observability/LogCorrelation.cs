using PacioliBank.Api.Endpoints;
using Serilog.Context;

namespace PacioliBank.Api.Observability;

/// <summary>
/// Poe a correlacao da requisicao em toda entrada de log emitida durante ela
/// (RNF-031).
/// </summary>
/// <remarks>
/// Separado do middleware de correlacao de proposito: o adaptador HTTP resolve
/// e devolve o identificador sem conhecer a biblioteca de log; quem liga o
/// identificador ao log e esta camada (ADR-0012).
/// </remarks>
public static class LogCorrelation
{
    /// <summary>Registrar depois de <c>UseCorrelation</c>, que resolve o identificador.</summary>
    public static IApplicationBuilder UseLogCorrelation(this IApplicationBuilder app) =>
        app.Use(async (http, next) =>
        {
            using (LogContext.PushProperty("CorrelationId", Correlation.Of(http)))
            {
                await next(http);
            }
        });
}
