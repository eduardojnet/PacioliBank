using System.Globalization;
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
        var account = app.MapGroup("/api/v1/accounts/{accountId:guid}");

        account.MapPost("/credits", (
                Guid accountId,
                PostingBody? body,
                [FromHeader(Name = LedgerHeaders.IdempotencyKey)] string? idempotencyKey,
                HttpContext http,
                ILedgerService ledger,
                CancellationToken cancellationToken) =>
            PostAsync(EntryDirection.Credit, accountId, body, idempotencyKey, http, ledger, cancellationToken));

        account.MapPost("/debits", (
                Guid accountId,
                PostingBody? body,
                [FromHeader(Name = LedgerHeaders.IdempotencyKey)] string? idempotencyKey,
                HttpContext http,
                ILedgerService ledger,
                CancellationToken cancellationToken) =>
            PostAsync(EntryDirection.Debit, accountId, body, idempotencyKey, http, ledger, cancellationToken));

        account.MapPost("/entries/{entryId:guid}/reversals", ReverseAsync);
        account.MapGet("/balance", GetBalanceAsync);
        account.MapGet("/entries", GetStatementAsync);

        return app;
    }

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

        return Written(result, reversalOf: null, http);
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

        return Written(result, reversalOf: entryId, http);
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
    /// repeticao, com corpo identico ao original (EF secao 8.4).
    /// </summary>
    private static IResult Written(PostEntryResult result, Guid? reversalOf, HttpContext http)
    {
        var body = EntryResponse.From(result, reversalOf);

        if (result.Replayed)
        {
            http.Response.Headers[LedgerHeaders.IdempotencyReplayed] = "true";
            return Results.Ok(body);
        }

        return Results.Created((string?)null, body);
    }
}
