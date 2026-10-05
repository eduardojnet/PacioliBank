using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PacioliBank.Ledger.Domain;

namespace PacioliBank.Api.Endpoints;

/// <summary>
/// Traduz rejeicoes em <c>application/problem+json</c> (RFC 9457) com o campo
/// <c>code</c> estavel do catalogo da EF secao 8.6.
/// </summary>
/// <remarks>
/// Um unico mapeamento para todos os endpoints. Espalhar <c>try/catch</c> pelos
/// endpoints faria o mesmo erro sair com codigos diferentes conforme a rota, e
/// o codigo e contratual: define o que o chamador pode reenviar.
/// <para>
/// Excecao fora do mapa nao e tratada aqui: segue para o 500 generico, sem
/// mensagem interna no corpo.
/// </para>
/// </remarks>
public sealed class LedgerProblems : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public LedgerProblems(IProblemDetailsService problemDetails)
    {
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var problem = Map(exception);
        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;

        if (problem.Status == StatusCodes.Status503ServiceUnavailable)
        {
            httpContext.Response.Headers.RetryAfter = "1";
        }

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    /// <summary>Acrescenta a correlacao a todo problema, inclusive aos gerados pelo framework.</summary>
    public static void Customize(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ProblemDetails.Extensions["correlationId"] = Correlation.Of(context.HttpContext);
    }

    /// <summary>
    /// Codigo da EF secao 8.6 para a excecao, ou nulo se ela nao esta no mapa.
    /// Sai do mesmo mapa que produz a resposta: a metrica de rejeicao e o
    /// codigo devolvido ao chamador nunca divergem (ADR-0012).
    /// </summary>
    internal static string? CodeOf(Exception exception) =>
        Map(exception)?.Extensions.TryGetValue("code", out var code) == true ? code as string : null;

    private static ProblemDetails? Map(Exception exception) => exception switch
    {
        InvalidEntryAmountException or InvalidMoneyScaleException =>
            Problem(400, "INVALID_AMOUNT", "Valor invalido", exception.Message),

        // Moeda fora do catalogo (QA-005: so BRL) e, para o chamador, moeda
        // divergente da conta. Codigo proprio nao existe na EF secao 8.6.
        CurrencyMismatchException or UnsupportedCurrencyException =>
            Problem(400, "CURRENCY_MISMATCH", "Moeda divergente da conta", exception.Message),

        InvalidPointInTimeException =>
            Problem(400, "INVALID_POINT_IN_TIME", "Instante de consulta invalido", exception.Message),

        PageSizeExceededException =>
            Problem(400, "PAGE_SIZE_EXCEEDED", "Limite de pagina excedido", exception.Message),

        MissingIdempotencyKeyException =>
            Problem(400, "IDEMPOTENCY_KEY_REQUIRED", "Chave de idempotencia obrigatoria",
                $"Envie o cabecalho {LedgerHeaders.IdempotencyKey} em todo comando de escrita."),

        // Erro de protocolo, nao de regra. Codigo ausente da EF secao 8.6:
        // acrescentado na implementacao e registrado como lacuna.
        InvalidRequestException or BadHttpRequestException =>
            Problem(400, "INVALID_REQUEST", "Requisicao invalida",
                exception is InvalidRequestException ? exception.Message : "Corpo ou parametro malformado."),

        AccountNotFoundException =>
            Problem(404, "ACCOUNT_NOT_FOUND", "Conta nao encontrada", "A conta informada nao foi encontrada."),

        // Lancamento de outra conta responde igual a lancamento inexistente:
        // distinguir revelaria a existencia do lancamento (principio do ADR-0009).
        EntryNotFoundException or EntryNotFromThisAccountException =>
            Problem(404, "ENTRY_NOT_FOUND", "Lancamento nao encontrado",
                "O lancamento informado nao foi encontrado nesta conta."),

        IdempotencyConflictException =>
            Problem(409, "IDEMPOTENCY_KEY_CONFLICT", "Chave de idempotencia reutilizada", exception.Message),

        EntryAlreadyReversedException =>
            Problem(409, "ENTRY_ALREADY_REVERSED", "Lancamento ja estornado", exception.Message),

        CannotReverseReversalException =>
            Problem(409, "CANNOT_REVERSE_REVERSAL", "Estorno de estorno", exception.Message),

        AccountInactiveException =>
            Problem(422, "ACCOUNT_INACTIVE", "Conta nao aceita lancamentos", exception.Message),

        InsufficientFundsException funds => InsufficientFunds(funds),

        // Repetivel: a chave de idempotencia protege o reenvio (RF-008). Inclui
        // o banco fora do ar: o adaptador de dados traduz a falha transitoria do
        // driver para esta excecao, e a borda HTTP nao conhece o driver.
        LedgerUnavailableException =>
            Problem(503, "SERVICE_UNAVAILABLE", "Servico temporariamente indisponivel", exception.Message),

        _ => null,
    };

    private static ProblemDetails InsufficientFunds(InsufficientFundsException exception)
    {
        var problem = Problem(422, "INSUFFICIENT_FUNDS", "Saldo insuficiente",
            "O debito solicitado excede a posicao disponivel.");

        problem.Extensions["availableBalance"] = Amount(exception.AvailableBalance);
        problem.Extensions["requestedAmount"] = Amount(exception.RequestedAmount);

        return problem;
    }

    private static ProblemDetails Problem(int status, string code, string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Type = "urn:pacioli:problem:" + code.ToLowerInvariant().Replace('_', '-'),
            Title = title,
            Status = status,
            Detail = detail,
        };

        problem.Extensions["code"] = code;

        return problem;
    }

    // A excecao carrega o decimal sem a moeda; a escala de 2 casas e a do BRL,
    // unica moeda do catalogo (QA-005).
    private static string Amount(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);
}
