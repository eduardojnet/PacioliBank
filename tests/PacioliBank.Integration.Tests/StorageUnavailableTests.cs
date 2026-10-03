using Npgsql;
using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Domain;
using PacioliBank.Ledger.Persistence;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Armazenamento indisponivel sai da porta como <see cref="LedgerUnavailableException"/>,
/// e nao como excecao do driver (RF-008, card 26).
/// </summary>
/// <remarks>
/// Quem traduz a falha de infraestrutura para o contrato e o adaptador que a
/// conhece. Antes, o adaptador HTTP capturava a excecao do Npgsql, e a borda
/// passava a saber qual banco existe por tras da porta: a regra de arquitetura
/// do card 26 apontou a dependencia. Aqui o banco esta de fato inalcancavel.
/// </remarks>
public class StorageUnavailableTests
{
    private static NpgsqlDataSource BancoInalcancavel() =>
        NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2");

    [Fact]
    public async Task Escrita_com_banco_inalcancavel_e_recusada_como_repetivel()
    {
        await using var fonte = BancoInalcancavel();
        var store = new PostgresLedgerStore(fonte);
        var comando = new PostingRequest(EntryDirection.Credit, Money.Of(10m, Currency.Brl), DateTimeOffset.UtcNow, "k", Guid.NewGuid());

        var erro = await Assert.ThrowsAsync<LedgerUnavailableException>(
            () => store.PostAsync(Guid.NewGuid(), comando, RequestFingerprint.Of(Guid.NewGuid(), comando), CancellationToken.None));

        Assert.IsAssignableFrom<NpgsqlException>(erro.InnerException);
    }

    [Fact]
    public async Task Consultas_com_banco_inalcancavel_sao_recusadas_como_repetiveis()
    {
        await using var fonte = BancoInalcancavel();
        var store = new PostgresLedgerStore(fonte);

        await Assert.ThrowsAsync<LedgerUnavailableException>(
            () => store.GetBalanceAsync(Guid.NewGuid(), null, CancellationToken.None));
        await Assert.ThrowsAsync<LedgerUnavailableException>(
            () => store.GetStatementAsync(Guid.NewGuid(), null, null, 0, 10, CancellationToken.None));
    }
}
