using System.Text.Json;
using Dapper;
using Npgsql;
using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Domain;
using PacioliBank.Ledger.Persistence;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Contrato do payload dos eventos de integracao (EF secao 9, card 24.2).
/// </summary>
/// <remarks>
/// Le o payload exatamente como foi gravado na outbox, e nao como o codigo o
/// monta em memoria: o consumidor recebe o que esta na tabela. Confere o
/// conjunto de campos, para que propriedade interna nao vaze, e o formato de
/// cada um.
/// </remarks>
[Collection(LedgerCollectionDefinition.Name)]
public class EventPayloadTests
{
    private static readonly string[] CamposDoLancamento =
        ["accountId", "amount", "balanceAfter", "currency", "direction", "entryId", "occurredAt", "recordedAt", "sequence"];

    private readonly LedgerFixture _fixture;
    private readonly PostgresLedgerStore _store;

    public EventPayloadTests(LedgerFixture fixture)
    {
        _fixture = fixture;
        _store = new PostgresLedgerStore(fixture.RuntimeDataSource);
    }

    private async Task<(string Tipo, JsonElement Payload)> EventoAsync(Guid conta, long sequencia)
    {
        await using var admin = new NpgsqlConnection(_fixture.MigratorConnectionString);
        var linha = await admin.QuerySingleAsync<(string Tipo, string Payload)>(
            "SELECT event_type, payload::text FROM ledger.outbox_messages WHERE account_id = @conta AND sequence = @sequencia",
            new { conta, sequencia });

        return (linha.Tipo, JsonDocument.Parse(linha.Payload).RootElement.Clone());
    }

    private static string[] Campos(JsonElement payload) =>
        payload.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task Evento_de_lancamento_tem_exatamente_os_campos_do_contrato_no_formato_do_contrato()
    {
        var conta = await _fixture.CreateAccountAsync();
        var comando = new PostingRequest(
            EntryDirection.Credit, Money.Of(150m, Currency.Brl),
            new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero), "k-evento", Guid.NewGuid());
        var lancamento = await _store.PostAsync(conta, comando, RequestFingerprint.Of(conta, comando), CancellationToken.None);

        var (tipo, payload) = await EventoAsync(conta, 1);

        Assert.Equal("pacioli.ledger.entry-recorded.v1", tipo);
        Assert.Equal(CamposDoLancamento, Campos(payload));
        Assert.Equal(lancamento.EntryId.ToString(), payload.GetProperty("entryId").GetString());
        Assert.Equal(conta.ToString(), payload.GetProperty("accountId").GetString());
        Assert.Equal(1, payload.GetProperty("sequence").GetInt64());
        Assert.Equal("Credit", payload.GetProperty("direction").GetString());

        // EF secao 8.2: valor monetario e string, com a escala da moeda.
        Assert.Equal(JsonValueKind.String, payload.GetProperty("amount").ValueKind);
        Assert.Equal("150.00", payload.GetProperty("amount").GetString());
        Assert.Equal("150.00", payload.GetProperty("balanceAfter").GetString());
        Assert.Equal("BRL", payload.GetProperty("currency").GetString());

        // EF secao 8.1: instante ISO 8601 em UTC com sufixo Z.
        Assert.Equal("2026-10-02T10:00:00Z", payload.GetProperty("occurredAt").GetString());
        Assert.EndsWith("Z", payload.GetProperty("recordedAt").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evento_de_estorno_informa_o_lancamento_estornado()
    {
        var conta = await _fixture.CreateAccountAsync();
        var credito = new PostingRequest(
            EntryDirection.Credit, Money.Of(80m, Currency.Brl), DateTimeOffset.UtcNow, "k-original", Guid.NewGuid());
        var original = await _store.PostAsync(conta, credito, RequestFingerprint.Of(conta, credito), CancellationToken.None);

        var estorno = new ReversalRequest(DateTimeOffset.UtcNow, "k-estorno", Guid.NewGuid());
        await _store.ReverseAsync(
            conta, original.EntryId, estorno, RequestFingerprint.OfReversal(conta, original.EntryId, estorno), CancellationToken.None);

        var (tipo, payload) = await EventoAsync(conta, 2);

        Assert.Equal("pacioli.ledger.entry-reversed.v1", tipo);
        Assert.Equal(CamposDoLancamento.Append("reversalOf").Order(StringComparer.Ordinal).ToArray(), Campos(payload));
        Assert.Equal(original.EntryId.ToString(), payload.GetProperty("reversalOf").GetString());
        Assert.Equal("Debit", payload.GetProperty("direction").GetString());
        Assert.Equal("80.00", payload.GetProperty("amount").GetString());
        Assert.Equal("0.00", payload.GetProperty("balanceAfter").GetString());
    }
}
