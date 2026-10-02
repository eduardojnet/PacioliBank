using System.Collections.Concurrent;
using Dapper;
using Npgsql;
using PacioliBank.Events;
using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Domain;
using PacioliBank.Ledger.Persistence;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Despachante de outbox (ADR-0008), contra PostgreSQL real.
/// </summary>
/// <remarks>
/// A outbox e compartilhada por toda a colecao de testes, e as outras classes
/// gravam nela a cada lancamento. Por isso cada teste comeca marcando como
/// publicado o que ja estava pendente: as classes de uma mesma colecao rodam
/// em sequencia, entao isso isola o teste sem filtro artificial no despachante.
/// </remarks>
[Collection(LedgerCollection.Name)]
public class OutboxDispatcherTests
{
    private readonly LedgerFixture _fixture;
    private readonly PostgresLedgerStore _store;

    public OutboxDispatcherTests(LedgerFixture fixture)
    {
        _fixture = fixture;
        _store = new PostgresLedgerStore(fixture.RuntimeDataSource);
    }

    private async Task LimparPendentesAsync()
    {
        await using var admin = new NpgsqlConnection(_fixture.MigratorConnectionString);
        await admin.ExecuteAsync("UPDATE ledger.outbox_messages SET published_at = now() WHERE published_at IS NULL");
    }

    private async Task<Guid> ContaComCreditosAsync(int quantidade)
    {
        var conta = await _fixture.CreateAccountAsync();

        for (var i = 0; i < quantidade; i++)
        {
            var comando = new PostingRequest(
                EntryDirection.Credit, Money.Of(1.00m, Currency.Brl), DateTimeOffset.UtcNow, $"k-{i}", Guid.NewGuid());
            await _store.PostAsync(conta, comando, RequestFingerprint.Of(conta, comando), CancellationToken.None);
        }

        return conta;
    }

    private OutboxDispatcher Despachante(IEventPublisher publicador, int maxTentativas = 10, int lote = 100) =>
        new(_fixture.RuntimeDataSource, publicador, new OutboxDispatcherOptions { MaxAttempts = maxTentativas, BatchSize = lote });

    private async Task<OutboxRow> LinhaAsync(Guid messageId)
    {
        await using var admin = new NpgsqlConnection(_fixture.MigratorConnectionString);
        return await admin.QuerySingleAsync<OutboxRow>(
            """
            SELECT published_at IS NOT NULL AS Publicada,
                   attempts::int            AS Tentativas,
                   next_attempt_at > now()  AS Adiada
              FROM ledger.outbox_messages
             WHERE message_id = @messageId
            """,
            new { messageId });
    }

    private async Task LiberarParaNovaTentativaAsync()
    {
        await using var admin = new NpgsqlConnection(_fixture.MigratorConnectionString);
        await admin.ExecuteAsync(
            "UPDATE ledger.outbox_messages SET next_attempt_at = now() WHERE published_at IS NULL");
    }

    [Fact]
    public async Task Publica_as_pendentes_marca_e_nao_republica()
    {
        await LimparPendentesAsync();
        var conta = await ContaComCreditosAsync(3);
        var publicador = new PublicadorQueRegistra();

        var primeira = await Despachante(publicador).DispatchOnceAsync(CancellationToken.None);
        var segunda = await Despachante(publicador).DispatchOnceAsync(CancellationToken.None);

        Assert.Equal(3, primeira.Published.Count);
        Assert.Empty(segunda.Published);
        Assert.All(publicador.Mensagens, m => Assert.Equal(conta, m.AccountId));
        Assert.Equal([1L, 2L, 3L], publicador.Mensagens.Select(m => m.Sequence));
        Assert.All(publicador.Mensagens, m => Assert.Equal("pacioli.ledger.entry-recorded.v1", m.EventType));

        foreach (var id in primeira.Published)
        {
            Assert.True((await LinhaAsync(id)).Publicada);
        }
    }

    [Fact]
    public async Task Falha_de_publicacao_incrementa_tentativas_e_adia_sem_marcar()
    {
        await LimparPendentesAsync();
        await ContaComCreditosAsync(1);

        var resultado = await Despachante(new PublicadorQueFalha()).DispatchOnceAsync(CancellationToken.None);

        var id = Assert.Single(resultado.Failed);
        var linha = await LinhaAsync(id);
        Assert.False(linha.Publicada);
        Assert.Equal(1, linha.Tentativas);
        Assert.True(linha.Adiada);

        // Adiada: uma nova passada imediata nao a le.
        var imediata = await Despachante(new PublicadorQueRegistra()).DispatchOnceAsync(CancellationToken.None);
        Assert.Equal(0, imediata.Read);
    }

    [Fact]
    public async Task Barramento_retomado_publica_a_mensagem_pendente_com_o_mesmo_message_id()
    {
        // BDD F08: barramento indisponivel nao impede o lancamento; o evento
        // fica pendente e sai na retomada, com identificador estavel.
        await LimparPendentesAsync();
        await ContaComCreditosAsync(1);

        var falha = await Despachante(new PublicadorQueFalha()).DispatchOnceAsync(CancellationToken.None);
        await LiberarParaNovaTentativaAsync();

        var publicador = new PublicadorQueRegistra();
        var retomada = await Despachante(publicador).DispatchOnceAsync(CancellationToken.None);

        Assert.Equal(Assert.Single(falha.Failed), Assert.Single(retomada.Published));
        Assert.Equal(1, Assert.Single(publicador.Mensagens).Attempts);
    }

    [Fact]
    public async Task Despachantes_em_paralelo_nao_publicam_a_mesma_mensagem_duas_vezes()
    {
        const int Mensagens = 60;
        const int Despachantes = 6;

        await LimparPendentesAsync();
        await ContaComCreditosAsync(Mensagens);
        var publicador = new PublicadorQueRegistra(atraso: TimeSpan.FromMilliseconds(5));

        using var largada = new SemaphoreSlim(0, Despachantes);
        var tarefas = Enumerable.Range(0, Despachantes).Select(async _ =>
        {
            await largada.WaitAsync();
            var total = 0;
            DispatchResult r;
            do
            {
                r = await Despachante(publicador, lote: 7).DispatchOnceAsync(CancellationToken.None);
                total += r.Published.Count;
            }
            while (r.Read > 0);
            return total;
        }).ToArray();
        largada.Release(Despachantes);

        var porDespachante = await Task.WhenAll(tarefas);

        Assert.Equal(Mensagens, porDespachante.Sum());
        Assert.Equal(Mensagens, publicador.Mensagens.Select(m => m.MessageId).Distinct().Count());
        Assert.Equal(Mensagens, publicador.Mensagens.Count);

        // Mais de um despachante trabalhou: sem isso, o teste nao teria
        // exercitado o SKIP LOCKED.
        Assert.True(porDespachante.Count(n => n > 0) > 1);
    }

    [Fact]
    public async Task Mensagem_que_atinge_o_limite_de_tentativas_sai_da_fila_sem_bloquear_as_demais()
    {
        await LimparPendentesAsync();
        await ContaComCreditosAsync(1);

        var primeira = await Despachante(new PublicadorQueFalha(), maxTentativas: 2).DispatchOnceAsync(CancellationToken.None);
        Assert.Empty(primeira.Parked);

        await LiberarParaNovaTentativaAsync();
        var segunda = await Despachante(new PublicadorQueFalha(), maxTentativas: 2).DispatchOnceAsync(CancellationToken.None);
        var estacionada = Assert.Single(segunda.Parked);

        // Uma mensagem nova, de outra conta, continua saindo normalmente.
        await ContaComCreditosAsync(1);
        await LiberarParaNovaTentativaAsync();
        var publicador = new PublicadorQueRegistra();
        var depois = await Despachante(publicador, maxTentativas: 2).DispatchOnceAsync(CancellationToken.None);

        Assert.Single(depois.Published);
        Assert.DoesNotContain(estacionada, depois.Published);
        Assert.Equal(2, (await LinhaAsync(estacionada)).Tentativas);
        Assert.False((await LinhaAsync(estacionada)).Publicada);
    }

    private sealed class PublicadorQueRegistra(TimeSpan? atraso = null) : IEventPublisher
    {
        private readonly ConcurrentQueue<OutboxMessage> _mensagens = new();

        public IReadOnlyList<OutboxMessage> Mensagens => _mensagens.ToList();

        public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            if (atraso is { } espera)
            {
                await Task.Delay(espera, cancellationToken);
            }

            _mensagens.Enqueue(message);
        }
    }

    private sealed class PublicadorQueFalha : IEventPublisher
    {
        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Barramento indisponivel.");
    }

    private sealed class OutboxRow
    {
        public bool Publicada { get; set; }

        public int Tentativas { get; set; }

        public bool Adiada { get; set; }
    }
}
