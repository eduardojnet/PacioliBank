using Dapper;
using Npgsql;
using PacioliBank.Ledger.Application;
using PacioliBank.Ledger.Domain;
using PacioliBank.Ledger.Persistence;

namespace PacioliBank.Integration.Tests;

/// <summary>
/// Transferencia entre contas (RF-012, RN-013, ADR-0014), contra PostgreSQL
/// real: as duas pernas numa so transacao, as contas bloqueadas em ordem
/// crescente de identificador, e as pernas amarradas pelo banco a uma mesma
/// transferencia.
/// </summary>
[Collection(LedgerCollectionDefinition.Name)]
public class TransferTests
{
    private static readonly DateTimeOffset Fato = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly LedgerFixture _fixture;
    private readonly PostgresLedgerStore _store;

    public TransferTests(LedgerFixture fixture)
    {
        _fixture = fixture;
        _store = new PostgresLedgerStore(fixture.RuntimeDataSource);
    }

    private static TransferRequest Pedido(decimal valor, string chave) =>
        new(Money.Of(valor, Currency.Brl), Fato, chave, Guid.NewGuid());

    private Task<TransferResult> TransferirAsync(Guid origem, Guid destino, decimal valor, string chave)
    {
        var pedido = Pedido(valor, chave);
        return _store.TransferAsync(origem, destino, pedido, RequestFingerprint.OfTransfer(origem, destino, pedido), CancellationToken.None);
    }

    private async Task<decimal> SaldoAsync(Guid conta) =>
        (await _store.GetBalanceAsync(conta, null, CancellationToken.None)).Balance.Amount;

    private async Task<long> ContarAsync(string sql, object parametros)
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        return await connection.ExecuteScalarAsync<long>(sql, parametros);
    }

    private Task<long> LancamentosAsync(Guid conta) =>
        ContarAsync("SELECT count(*) FROM ledger.ledger_entries WHERE account_id = @conta", new { conta });

    private Task<long> TransferenciasAsync(Guid conta) =>
        ContarAsync("SELECT count(*) FROM ledger.transfers WHERE source_account_id = @conta OR destination_account_id = @conta", new { conta });

    // ------------------------------------------------------------ caminho feliz

    [Fact]
    public async Task Transferencia_move_o_valor_e_preserva_a_soma_das_duas_contas()
    {
        var origem = await _fixture.CreateAccountAsync(100.00m);
        var destino = await _fixture.CreateAccountAsync(5.00m);

        var resultado = await TransferirAsync(origem, destino, 40.00m, "t-1");

        Assert.False(resultado.Replayed);
        Assert.Equal(60.00m, await SaldoAsync(origem));
        Assert.Equal(45.00m, await SaldoAsync(destino));

        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        var gravada = await connection.QuerySingleAsync<(Guid Origem, Guid Destino, decimal Valor, Guid Debito, Guid Credito)>(
            """
            SELECT source_account_id, destination_account_id, amount, debit_entry_id, credit_entry_id
              FROM ledger.transfers WHERE transfer_id = @id
            """,
            new { id = resultado.TransferId });

        Assert.Equal((origem, destino, 40.00m, resultado.DebitEntryId, resultado.CreditEntryId), gravada);
    }

    [Fact]
    public async Task Cada_perna_entra_no_extrato_e_na_fila_de_eventos_da_sua_conta()
    {
        var origem = await _fixture.CreateAccountAsync(100.00m);
        var destino = await _fixture.CreateAccountAsync();

        var resultado = await TransferirAsync(origem, destino, 25.00m, "t-2");

        var extratoOrigem = await _store.GetStatementAsync(origem, null, null, 0, 10, CancellationToken.None);
        var extratoDestino = await _store.GetStatementAsync(destino, null, null, 0, 10, CancellationToken.None);
        Assert.Equal(resultado.DebitEntryId, extratoOrigem.Entries[^1].EntryId);
        Assert.Equal(EntryDirection.Debit, extratoOrigem.Entries[^1].Direction);
        Assert.Equal(resultado.CreditEntryId, extratoDestino.Entries.Single().EntryId);
        Assert.Equal(EntryDirection.Credit, extratoDestino.Entries.Single().Direction);

        Assert.Equal(1, await ContarAsync("SELECT count(*) FROM ledger.outbox_messages WHERE account_id = @destino", new { destino }));
        Assert.Equal(1, await ContarAsync(
            "SELECT count(*) FROM ledger.outbox_messages WHERE account_id = @origem AND sequence = 2", new { origem }));
    }

    // -------------------------------------------------------- tudo ou nada

    [Fact]
    public async Task Saldo_insuficiente_na_origem_nao_grava_nada_em_nenhuma_das_contas()
    {
        var origem = await _fixture.CreateAccountAsync(39.99m);
        var destino = await _fixture.CreateAccountAsync();

        await Assert.ThrowsAsync<InsufficientFundsException>(() => TransferirAsync(origem, destino, 40.00m, "t-3"));

        Assert.Equal(1, await LancamentosAsync(origem));
        Assert.Equal(0, await LancamentosAsync(destino));
        Assert.Equal(0, await TransferenciasAsync(origem));
    }

    [Fact]
    public async Task Destino_inexistente_nao_grava_nada_na_origem()
    {
        var origem = await _fixture.CreateAccountAsync(100.00m);

        await Assert.ThrowsAsync<AccountNotFoundException>(() => TransferirAsync(origem, Guid.NewGuid(), 10.00m, "t-4"));

        Assert.Equal(1, await LancamentosAsync(origem));
        Assert.Equal(100.00m, await SaldoAsync(origem));
    }

    [Fact]
    public async Task Transferencia_para_a_propria_conta_e_recusada_sem_gravar()
    {
        var conta = await _fixture.CreateAccountAsync(100.00m);

        await Assert.ThrowsAsync<SameAccountTransferException>(() => TransferirAsync(conta, conta, 10.00m, "t-5"));

        Assert.Equal(1, await LancamentosAsync(conta));
    }

    [Fact]
    public async Task Falha_ao_gravar_a_perna_de_credito_desfaz_a_perna_de_debito()
    {
        // Falha provocada no banco DEPOIS de o dominio aceitar as duas pernas:
        // so a transacao unica impede o debito sem o credito correspondente.
        var origem = await _fixture.CreateAccountAsync(100.00m);
        var destino = await _fixture.CreateAccountAsync();
        var gatilho = "falha_" + destino.ToString("N");

        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        await connection.ExecuteAsync(
            $"""
            CREATE FUNCTION ledger.{gatilho}() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN RAISE EXCEPTION 'falha provocada pelo teste'; END $$;
            CREATE TRIGGER {gatilho} BEFORE INSERT ON ledger.ledger_entries
               FOR EACH ROW WHEN (NEW.account_id = '{destino}') EXECUTE FUNCTION ledger.{gatilho}();
            """);

        try
        {
            var erro = await Assert.ThrowsAsync<PostgresException>(() => TransferirAsync(origem, destino, 30.00m, "t-6"));
            Assert.Equal(PostgresErrorCodes.RaiseException, erro.SqlState);
        }
        finally
        {
            await connection.ExecuteAsync($"DROP TRIGGER {gatilho} ON ledger.ledger_entries; DROP FUNCTION ledger.{gatilho}();");
        }

        Assert.Equal(1, await LancamentosAsync(origem));
        Assert.Equal(100.00m, await SaldoAsync(origem));
        Assert.Equal(0, await TransferenciasAsync(origem));
        Assert.Equal(0, await ContarAsync(
            "SELECT count(*) FROM ledger.idempotency_records WHERE account_id = @origem", new { origem }));
    }

    // ---------------------------------------------------------- idempotencia

    [Fact]
    public async Task Reenvio_com_a_mesma_chave_devolve_a_resposta_original_sem_gravar_de_novo()
    {
        var origem = await _fixture.CreateAccountAsync(100.00m);
        var destino = await _fixture.CreateAccountAsync();
        var pedido = Pedido(40.00m, "t-7");
        var impressao = RequestFingerprint.OfTransfer(origem, destino, pedido);

        var primeira = await _store.TransferAsync(origem, destino, pedido, impressao, CancellationToken.None);

        // Entre o envio e o reenvio, a origem perde o saldo: o reenvio de uma
        // transferencia consumada recebe a resposta dela, nao uma rejeicao.
        var debito = new PostingRequest(EntryDirection.Debit, Money.Of(60.00m, Currency.Brl), Fato, "zera", Guid.NewGuid());
        await _store.PostAsync(origem, debito, RequestFingerprint.Of(origem, debito), CancellationToken.None);

        var segunda = await _store.TransferAsync(origem, destino, pedido with { CorrelationId = Guid.NewGuid() }, impressao, CancellationToken.None);

        Assert.True(segunda.Replayed);
        Assert.Equal(primeira.ResponseBody, segunda.ResponseBody);
        Assert.Equal(primeira.TransferId, segunda.TransferId);
        Assert.Equal(1, await TransferenciasAsync(origem));
        Assert.Equal(40.00m, await SaldoAsync(destino));
    }

    [Fact]
    public async Task Mesma_chave_com_outro_conteudo_e_conflito()
    {
        var origem = await _fixture.CreateAccountAsync(100.00m);
        var destino = await _fixture.CreateAccountAsync();
        await TransferirAsync(origem, destino, 40.00m, "t-8");

        await Assert.ThrowsAsync<IdempotencyConflictException>(() => TransferirAsync(origem, destino, 41.00m, "t-8"));

        // A mesma chave num debito comum da origem tambem e reuso indevido.
        var debito = new PostingRequest(EntryDirection.Debit, Money.Of(40.00m, Currency.Brl), Fato, "t-8", Guid.NewGuid());
        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => _store.PostAsync(origem, debito, RequestFingerprint.Of(origem, debito), CancellationToken.None));
    }

    [Fact]
    public async Task Envios_simultaneos_com_a_mesma_chave_produzem_uma_unica_transferencia()
    {
        var origem = await _fixture.CreateAccountAsync(100.00m);
        var destino = await _fixture.CreateAccountAsync();
        var pedido = Pedido(10.00m, "t-9");
        var impressao = RequestFingerprint.OfTransfer(origem, destino, pedido);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() => _store.TransferAsync(origem, destino, pedido, impressao, CancellationToken.None))));

        Assert.Single(resultados.Select(r => r.TransferId).Distinct());
        Assert.Equal(1, await TransferenciasAsync(origem));
        Assert.Equal(90.00m, await SaldoAsync(origem));
    }

    // ---------------------------------------------------------- concorrencia

    [Fact]
    public async Task Contas_sao_bloqueadas_em_ordem_crescente_de_identificador_qualquer_que_seja_o_sentido()
    {
        // A origem e a conta de MAIOR identificador. Com a outra transacao
        // segurando a maior, a transferencia correta ja bloqueou a menor e
        // espera pela maior. Bloquear a origem primeiro deixaria a menor livre:
        // e essa ordem variavel que produz o impasse entre transferencias
        // cruzadas (ADR-0005, revisao do card 38).
        var a = await _fixture.CreateAccountAsync(100.00m);
        var b = await _fixture.CreateAccountAsync(100.00m);
        var (menor, maior) = a.CompareTo(b) < 0 ? (a, b) : (b, a);

        await using var segurando = new NpgsqlConnection(_fixture.MigratorConnectionString);
        await segurando.OpenAsync(TestContext.Current.CancellationToken);
        await using var transacao = await segurando.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await segurando.ExecuteAsync(
            "SELECT 1 FROM ledger.accounts WHERE account_id = @maior FOR NO KEY UPDATE", new { maior }, transacao);

        var transferencia = TransferirAsync(maior, menor, 10.00m, "t-10");
        await AguardarTransferenciaEsperandoBloqueioAsync();

        await using (var sonda = new NpgsqlConnection(_fixture.MigratorConnectionString))
        {
            await sonda.OpenAsync(TestContext.Current.CancellationToken);
            await using var tx = await sonda.BeginTransactionAsync(TestContext.Current.CancellationToken);
            var erro = await Assert.ThrowsAsync<PostgresException>(() => sonda.ExecuteAsync(
                "SELECT 1 FROM ledger.accounts WHERE account_id = @menor FOR NO KEY UPDATE NOWAIT", new { menor }, tx));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, erro.SqlState);
        }

        await transacao.RollbackAsync(TestContext.Current.CancellationToken);
        var resultado = await transferencia;

        Assert.False(resultado.Replayed);
        Assert.Equal(90.00m, await SaldoAsync(maior));
        Assert.Equal(110.00m, await SaldoAsync(menor));
    }

    [Fact]
    public async Task Transferencias_cruzadas_simultaneas_terminam_todas_e_preservam_a_soma()
    {
        const int PorSentido = 15;
        var a = await _fixture.CreateAccountAsync(1000.00m);
        var b = await _fixture.CreateAccountAsync(1000.00m);

        using var largada = new SemaphoreSlim(0, 2 * PorSentido);
        var tarefas = Enumerable.Range(0, 2 * PorSentido).Select(async i =>
        {
            await largada.WaitAsync();
            return i % 2 == 0
                ? await TransferirAsync(a, b, 10.00m, $"ab-{i}")
                : await TransferirAsync(b, a, 10.00m, $"ba-{i}");
        }).ToArray();
        largada.Release(2 * PorSentido);

        var resultados = await Task.WhenAll(tarefas);

        Assert.All(resultados, r => Assert.False(r.Replayed));
        Assert.Equal(1000.00m, await SaldoAsync(a));
        Assert.Equal(1000.00m, await SaldoAsync(b));
        Assert.Equal(1 + (2 * PorSentido), await LancamentosAsync(a));
    }

    private async Task AguardarTransferenciaEsperandoBloqueioAsync()
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        for (var i = 0; i < 100; i++)
        {
            var esperando = await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM pg_stat_activity WHERE usename = 'pacioli_runtime' AND wait_event_type = 'Lock'");
            if (esperando > 0)
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("A transferencia nao chegou a esperar pelo bloqueio da conta.");
    }

    // ------------------------------------------- amarracao das pernas no banco

    private sealed record Perna(Guid EntryId, Guid Conta);

    /// <summary>
    /// Grava um lancamento direto no banco, com o papel de migracao, para
    /// montar pernas que o caminho de escrita nunca produziria.
    /// </summary>
    private async Task<Perna> PernaAsync(NpgsqlConnection connection, short sentido, decimal valor, DateTimeOffset registro)
    {
        var conta = await _fixture.CreateAccountAsync();
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO ledger.entry_keys (entry_id, account_id, sequence, idempotency_key, reversal_of, recorded_at)
            VALUES (@id, @conta, 1, @chave, NULL, @registro);
            INSERT INTO ledger.ledger_entries
                (entry_id, account_id, sequence, direction, amount, currency, occurred_at, recorded_at,
                 idempotency_key, correlation_id, balance_after)
            VALUES (@id, @conta, 1, @sentido, @valor, 'BRL', @registro, @registro, @chave, @id, 0);
            """,
            new { id, conta, chave = "perna-" + id.ToString("N"), sentido, valor, registro });
        return new Perna(id, conta);
    }

    private const string InserirTransferencia = """
        INSERT INTO ledger.transfers
            (transfer_id, source_account_id, destination_account_id, amount, currency, recorded_at,
             debit_entry_id, credit_entry_id)
        VALUES (@id, @origem, @destino, @valor, 'BRL', @registro, @debito, @credito)
        """;

    private static object Transferencia(Perna debito, Perna credito, decimal valor, DateTimeOffset registro, Guid? origem = null) => new
    {
        id = Guid.NewGuid(),
        origem = origem ?? debito.Conta,
        destino = credito.Conta,
        valor,
        registro,
        debito = debito.EntryId,
        credito = credito.EntryId,
    };

    [Fact]
    public async Task Pernas_coerentes_sao_aceitas_pelo_banco()
    {
        // Controle positivo: sem ele, os testes de recusa abaixo passariam com
        // uma tabela que recusa tudo.
        var registro = DateTimeOffset.UtcNow;
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        var debito = await PernaAsync(connection, -1, 40.00m, registro);
        var credito = await PernaAsync(connection, 1, 40.00m, registro);

        Assert.Equal(1, await connection.ExecuteAsync(InserirTransferencia, Transferencia(debito, credito, 40.00m, registro)));
    }

    [Fact]
    public async Task Pernas_de_valores_diferentes_sao_recusadas_pelo_banco()
    {
        var registro = DateTimeOffset.UtcNow;
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        var debito = await PernaAsync(connection, -1, 40.00m, registro);
        var credito = await PernaAsync(connection, 1, 39.00m, registro);

        var erro = await Assert.ThrowsAsync<PostgresException>(
            () => connection.ExecuteAsync(InserirTransferencia, Transferencia(debito, credito, 40.00m, registro)));

        Assert.Equal("fk_transfers_credit", erro.ConstraintName);
    }

    [Fact]
    public async Task Perna_de_debito_que_e_credito_e_recusada_pelo_banco()
    {
        var registro = DateTimeOffset.UtcNow;
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        var debito = await PernaAsync(connection, 1, 40.00m, registro);
        var credito = await PernaAsync(connection, 1, 40.00m, registro);

        var erro = await Assert.ThrowsAsync<PostgresException>(
            () => connection.ExecuteAsync(InserirTransferencia, Transferencia(debito, credito, 40.00m, registro)));

        Assert.Equal("fk_transfers_debit", erro.ConstraintName);
    }

    [Fact]
    public async Task Perna_de_outra_conta_que_nao_a_origem_e_recusada_pelo_banco()
    {
        var registro = DateTimeOffset.UtcNow;
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        var debito = await PernaAsync(connection, -1, 40.00m, registro);
        var credito = await PernaAsync(connection, 1, 40.00m, registro);
        var outra = await _fixture.CreateAccountAsync();

        var erro = await Assert.ThrowsAsync<PostgresException>(
            () => connection.ExecuteAsync(InserirTransferencia, Transferencia(debito, credito, 40.00m, registro, origem: outra)));

        Assert.Equal("fk_transfers_debit", erro.ConstraintName);
    }

    [Fact]
    public async Task Perna_ja_usada_em_outra_transferencia_e_recusada_pelo_banco()
    {
        var registro = DateTimeOffset.UtcNow;
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        var debito = await PernaAsync(connection, -1, 40.00m, registro);
        var credito = await PernaAsync(connection, 1, 40.00m, registro);
        await connection.ExecuteAsync(InserirTransferencia, Transferencia(debito, credito, 40.00m, registro));

        var erro = await Assert.ThrowsAsync<PostgresException>(
            () => connection.ExecuteAsync(InserirTransferencia, Transferencia(debito, credito, 40.00m, registro)));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, erro.SqlState);
        Assert.Equal("uq_transfers_debit", erro.ConstraintName);
    }

    [Fact]
    public async Task Transferencia_da_conta_para_ela_mesma_e_recusada_pelo_banco()
    {
        var registro = DateTimeOffset.UtcNow;
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        var debito = await PernaAsync(connection, -1, 40.00m, registro);
        var credito = await PernaAsync(connection, 1, 40.00m, registro);

        var erro = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(InserirTransferencia, new
        {
            id = Guid.NewGuid(),
            origem = debito.Conta,
            destino = debito.Conta,
            valor = 40.00m,
            registro,
            debito = debito.EntryId,
            credito = credito.EntryId,
        }));

        Assert.Equal("ck_transfers_distinct_accounts", erro.ConstraintName);
    }

    [Fact]
    public async Task Aplicacao_nao_altera_nem_apaga_transferencia()
    {
        var origem = await _fixture.CreateAccountAsync(100.00m);
        var destino = await _fixture.CreateAccountAsync();
        var resultado = await TransferirAsync(origem, destino, 10.00m, "t-11");

        await using var connection = await _fixture.RuntimeDataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var alterar = await Assert.ThrowsAsync<PostgresException>(() =>
            connection.ExecuteAsync("UPDATE ledger.transfers SET amount = 1 WHERE transfer_id = @id", new { id = resultado.TransferId }));
        var apagar = await Assert.ThrowsAsync<PostgresException>(() =>
            connection.ExecuteAsync("DELETE FROM ledger.transfers WHERE transfer_id = @id", new { id = resultado.TransferId }));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, alterar.SqlState);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, apagar.SqlState);
    }
}
