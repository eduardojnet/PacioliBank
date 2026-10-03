using System.Reflection;
using NetArchTest.Rules;
using PacioliBank.Api.Endpoints;
using PacioliBank.Events;
using PacioliBank.Ledger.Domain;
using PacioliBank.Ledger.Persistence;
using PacioliBank.Migrations;

namespace PacioliBank.Architecture.Tests;

/// <summary>
/// Regras de dependencia entre modulos e camadas (ADR-0001, ADR-0010).
/// </summary>
/// <remarks>
/// Defesa automatizada contra o risco R-07, a erosao de fronteiras que
/// produziu o sistema legado do enunciado. Uma fronteira que so existe no
/// diagrama dura ate a primeira entrega apressada; aqui ela reprova o build de
/// testes.
/// <para>
/// O compilador ja impede o dominio de usar o driver do banco, porque o projeto
/// nao declara o pacote. Estas regras cobrem o que o compilador nao cobre:
/// referencia de projeto acrescentada sem decisao, e dependencia entre
/// namespaces de um mesmo projeto.
/// </para>
/// </remarks>
public class DependencyRulesTests
{
    private static readonly Assembly Ledger = typeof(Money).Assembly;
    private static readonly Assembly Persistence = typeof(PostgresLedgerStore).Assembly;
    private static readonly Assembly Events = typeof(OutboxDispatcher).Assembly;
    private static readonly Assembly Api = typeof(LedgerEndpoints).Assembly;
    private static readonly Assembly Migrations = typeof(SchemaMigrator).Assembly;

    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string Npgsql = "Npgsql";
    private const string Dapper = "Dapper";

    private static void Aprovar(TestResult resultado, string regra) =>
        Assert.True(
            resultado.IsSuccessful,
            $"{regra}. Tipos que violam: {string.Join(", ", resultado.FailingTypeNames ?? [])}");

    [Fact]
    public void Ledger_nao_conhece_infraestrutura_nem_os_outros_modulos()
    {
        var resultado = Types.InAssembly(Ledger)
            .ShouldNot()
            .HaveDependencyOnAny(Npgsql, Dapper, AspNetCore,
                "PacioliBank.Ledger.Persistence", "PacioliBank.Events", "PacioliBank.Api")
            .GetResult();

        Aprovar(resultado, "O módulo Ledger (domínio e aplicação) não pode depender de banco, de HTTP nem de Persistence, Events ou Api");
    }

    [Fact]
    public void Dominio_nao_conhece_a_camada_de_aplicacao()
    {
        var resultado = Types.InAssembly(Ledger)
            .That().ResideInNamespace("PacioliBank.Ledger.Domain")
            .ShouldNot()
            .HaveDependencyOn("PacioliBank.Ledger.Application")
            .GetResult();

        Aprovar(resultado, "O domínio é a camada mais interna: não pode depender da aplicação, que depende dele");
    }

    [Fact]
    public void Persistence_e_adaptador_de_dados_e_nao_conhece_HTTP_nem_Events()
    {
        var resultado = Types.InAssembly(Persistence)
            .ShouldNot()
            .HaveDependencyOnAny(AspNetCore, "PacioliBank.Api", "PacioliBank.Events")
            .GetResult();

        Aprovar(resultado, "O adaptador de dados do Ledger não pode depender de HTTP, da API nem do módulo Events");
    }

    [Fact]
    public void Events_le_a_outbox_pelo_contrato_da_tabela_e_nao_conhece_o_Ledger()
    {
        var resultado = Types.InAssembly(Events)
            .ShouldNot()
            .HaveDependencyOnAny("PacioliBank.Ledger", "PacioliBank.Api", AspNetCore)
            .GetResult();

        Aprovar(resultado, "O módulo Events não pode depender do Ledger, da API nem de HTTP (ADR-0001, ADR-0008)");
    }

    [Fact]
    public void Endpoints_conhecem_so_a_porta_de_entrada_e_nao_o_banco()
    {
        var resultado = Types.InAssembly(Api)
            .That().ResideInNamespace("PacioliBank.Api.Endpoints")
            .ShouldNot()
            .HaveDependencyOnAny("PacioliBank.Ledger.Persistence", Npgsql, Dapper)
            .GetResult();

        Aprovar(resultado, "O adaptador HTTP fala com ILedgerService; quem conhece o banco é a raiz de composição (Program.cs)");
    }

    [Fact]
    public void Migracoes_conhecem_o_banco_e_nenhum_outro_modulo()
    {
        var resultado = Types.InAssembly(Migrations)
            .ShouldNot()
            .HaveDependencyOnAny("PacioliBank.Ledger", "PacioliBank.Events", "PacioliBank.Api", AspNetCore)
            .GetResult();

        Aprovar(resultado, "O migrador aplica SQL versionado com o papel de migração e termina; não pode depender do domínio nem dos módulos que rodam com o papel da aplicação (ADR-0002, ADR-0009)");
    }
}
