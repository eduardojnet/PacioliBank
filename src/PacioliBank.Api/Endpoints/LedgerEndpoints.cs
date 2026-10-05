using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Domain;

namespace PacioliBank.Api.Endpoints;

/// <summary>
/// Adaptador HTTP fino sobre a porta de entrada (EF secao 8.3).
/// </summary>
/// <remarks>
/// Aqui so ha traducao de protocolo: rota, cabecalho, corpo e codigo de
/// status. Nenhuma regra de negocio, nenhum calculo de impressao, nenhum
/// acesso ao <see cref="ILedgerStore"/>. Rejeicoes saem como excecao e sao
/// traduzidas para <c>application/problem+json</c> em um unico lugar.
/// </remarks>
public static class LedgerEndpoints
{
    public static IEndpointRouteBuilder MapLedgerEndpoints(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/api/v1/accounts/{accountId:guid}").WithTags("Ledger");

        account.MapPost("/credits", (
                Guid accountId,
                PostingBody? body,
                [FromHeader(Name = LedgerHeaders.IdempotencyKey)] string? idempotencyKey,
                HttpContext http,
                ILedgerService ledger,
                CancellationToken cancellationToken) =>
            PostAsync(EntryDirection.Credit, accountId, body, idempotencyKey, http, ledger, cancellationToken))
            .WithName("RegistrarCredito")
            .WithSummary("Registra crédito (RF-001)")
            .ProducesWrite();

        account.MapPost("/debits", (
                Guid accountId,
                PostingBody? body,
                [FromHeader(Name = LedgerHeaders.IdempotencyKey)] string? idempotencyKey,
                HttpContext http,
                ILedgerService ledger,
                CancellationToken cancellationToken) =>
            PostAsync(EntryDirection.Debit, accountId, body, idempotencyKey, http, ledger, cancellationToken))
            .WithName("RegistrarDebito")
            .WithSummary("Registra débito; recusa se a posição ficaria negativa (RF-002)")
            .ProducesWrite()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        account.MapPost("/entries/{entryId:guid}/reversals", ReverseAsync)
            .WithName("EstornarLancamento")
            .WithSummary("Estorna um lançamento por compensação (RF-007)")
            .ProducesWrite()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        account.MapGet("/balance", GetBalanceAsync)
            .WithName("ConsultarPosicao")
            .WithSummary("Posição corrente ou em instante passado (RF-003, RF-004)")
            .Produces<BalanceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        account.MapGet("/entries", GetStatementAsync)
            .WithName("ConsultarExtrato")
            .WithSummary("Extrato paginado por cursor (RF-005)")
            .Produces<StatementResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapPost("/api/v1/transfers", TransferAsync)
            .WithTags("Ledger")
            .WithName("Transferir")
            .WithSummary("Transfere entre duas contas, numa única transação (RF-012)")
            .Produces<TransferResponse>(StatusCodes.Status201Created)
            .Produces<TransferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    /// <summary>
    /// Respostas comuns a toda escrita (EF secao 8.4): 201 para lancamento novo,
    /// 200 para repeticao, ambos com o corpo de <see cref="PostingResponse"/>. O
    /// corpo da requisicao nao e declarado aqui: e inferido do parametro de cada
    /// endpoint (credito e debito recebem PostingBody; estorno, ReversalBody).
    /// </summary>
    private static RouteHandlerBuilder ProducesWrite(this RouteHandlerBuilder builder) =>
        builder
            .Produces<PostingResponse>(StatusCodes.Status201Created)
            .Produces<PostingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    private static async Task<IResult> PostAsync(
        EntryDirection direction,
        Guid accountId,
        PostingBody? body,
        string? idempotencyKey,
        HttpContext http,
        ILedgerService ledger,
        CancellationToken cancellationToken)
    {
        if (body?.Amount is null || body.Currency is null || body.OccurredAt is null)
        {
            throw new InvalidRequestException("Os campos amount, currency e occurredAt sao obrigatorios.");
        }

        var result = await ledger.PostAsync(
            new PostingCommand(
                accountId,
                direction,
                body.Amount,
                body.Currency,
                body.OccurredAt.Value,
                idempotencyKey,
                Correlation.Of(http)),
            cancellationToken);

        return Written(result, http);
    }

    private static async Task<IResult> ReverseAsync(
        Guid accountId,
        Guid entryId,
        ReversalBody? body,
        [FromHeader(Name = LedgerHeaders.IdempotencyKey)] string? idempotencyKey,
        HttpContext http,
        ILedgerService ledger,
        CancellationToken cancellationToken)
    {
        if (body?.OccurredAt is null)
        {
            throw new InvalidRequestException("O campo occurredAt e obrigatorio.");
        }

        var result = await ledger.ReverseAsync(
            new ReversalCommand(accountId, entryId, body.OccurredAt.Value, idempotencyKey, Correlation.Of(http)),
            cancellationToken);

        return Written(result, http);
    }

    private static async Task<IResult> TransferAsync(
        TransferBody? body,
        [FromHeader(Name = LedgerHeaders.IdempotencyKey)] string? idempotencyKey,
        HttpContext http,
        ILedgerService ledger,
        CancellationToken cancellationToken)
    {
        if (body?.SourceAccountId is null || body.DestinationAccountId is null
            || body.Amount is null || body.Currency is null || body.OccurredAt is null)
        {
            throw new InvalidRequestException(
                "Os campos sourceAccountId, destinationAccountId, amount, currency e occurredAt sao obrigatorios.");
        }

        var result = await ledger.TransferAsync(
            new TransferCommand(
                body.SourceAccountId.Value,
                body.DestinationAccountId.Value,
                body.Amount,
                body.Currency,
                body.OccurredAt.Value,
                idempotencyKey,
                Correlation.Of(http)),
            cancellationToken);

        return Written(result.ResponseBody, result.Replayed, http);
    }

    private static async Task<IResult> GetBalanceAsync(
        Guid accountId,
        DateTimeOffset? asOf,
        ILedgerService ledger,
        CancellationToken cancellationToken)
    {
        var result = await ledger.GetBalanceAsync(accountId, asOf, cancellationToken);
        return Results.Ok(BalanceResponse.From(result));
    }

    private static async Task<IResult> GetStatementAsync(
        Guid accountId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? limit,
        string? cursor,
        ILedgerService ledger,
        CancellationToken cancellationToken)
    {
        long? afterSequence = null;

        if (cursor is not null)
        {
            if (!long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new InvalidRequestException("Cursor invalido.");
            }

            afterSequence = parsed;
        }

        var page = await ledger.GetStatementAsync(
            new StatementQuery(accountId, from, to, afterSequence, limit),
            cancellationToken);

        return Results.Ok(StatementResponse.From(page));
    }

    /// <summary>
    /// 201 para lancamento novo; 200 com <c>Idempotency-Replayed: true</c> para
    /// repeticao (EF secao 8.4). Nos dois casos o corpo e o texto gravado no
    /// registro de idempotencia, escrito sem reserializacao: a repeticao devolve
    /// a resposta original byte a byte, e nao uma reconstrucao (ADR-0006).
    /// </summary>
    private static IResult Written(PostEntryResult result, HttpContext http) =>
        Written(result.ResponseBody, result.Replayed, http);

    private static IResult Written(string responseBody, bool replayed, HttpContext http)
    {
        if (replayed)
        {
            http.Response.Headers[LedgerHeaders.IdempotencyReplayed] = "true";
        }

        return Results.Content(
            responseBody,
            "application/json",
            Encoding.UTF8,
            replayed ? StatusCodes.Status200OK : StatusCodes.Status201Created);
    }
}
