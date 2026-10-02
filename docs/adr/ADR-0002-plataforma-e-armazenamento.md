# ADR-0002: Adotar .NET 10 LTS com PostgreSQL, acesso a dados por Dapper e migrações por DbUp

- **Status:** Aceito
- **Data:** 2026-10-02
- **Decisor:** Eduardo J. G. do Carmo
- **Requisitos dirigentes:** RNF-001, RNF-003, RNF-025, RNF-037, RNF-038, R-06

## Contexto e problema

O desafio impõe C# como linguagem. Restam três decisões: versão da plataforma, sistema de armazenamento e estratégia de acesso a dados.

A escolha de acesso a dados não é detalhe de implementação neste projeto. O modelo é append-only ([ADR-0003](./ADR-0003-ledger-append-only.md)) e exige bloqueio explícito por linha ([ADR-0005](./ADR-0005-controle-de-concorrencia.md)), duas características que alteram substancialmente a relação custo-benefício de um ORM com rastreamento de mudanças.

## Critérios de decisão

1. Suporte de longo prazo e previsibilidade de atualização
2. Controle explícito sobre o SQL executado, para previsibilidade de plano e auditabilidade
3. Suporte nativo a bloqueio de linha, constraints e particionamento declarativo
4. Capacidade de aplicar privilégio mínimo no nível do objeto (RNF-025)
5. Custo de licença e facilidade de execução em container (RNF-037)
6. Acoplamento tolerável a fornecedor (R-06)

## Opções consideradas

**Plataforma:** .NET 10 LTS; .NET 9 STS.
**Armazenamento:** PostgreSQL; SQL Server; armazenamento especializado em eventos (EventStoreDB).
**Acesso a dados:** EF Core 10; Dapper com Npgsql; Npgsql puro; Marten.

## Decisão

| Camada | Escolha |
|---|---|
| Plataforma | **.NET 10 (LTS)**, ASP.NET Core com Minimal APIs |
| Armazenamento | **PostgreSQL 17** |
| Acesso a dados | **Dapper sobre Npgsql** |
| Migrações | **DbUp** com scripts SQL versionados |
| Resiliência | **Polly v8** (`ResiliencePipeline`) |
| Telemetria | **OpenTelemetry** e **Serilog** |
| Testes | **xUnit**, **Reqnroll**, **Testcontainers**, **NetArchTest** |

[NVI] A janela exata de suporte do .NET 10 deve ser confirmada na política oficial de ciclo de vida da Microsoft antes de compromisso contratual. A escolha por uma versão LTS, e não pela mais recente, é o ponto da decisão.

### Por que Dapper e não EF Core

A vantagem central do EF Core é o rastreamento de mudanças e a unidade de trabalho: ler entidades, alterá-las em memória e deixar o framework calcular os comandos `UPDATE`. **Este sistema não faz nada disso.** O ledger é append-only: só existem `INSERT` e `SELECT`. O único `UPDATE` do sistema é o incremento de uma coluna de controle na tabela de contas.

Nesse perfil, o rastreamento de mudanças é custo sem contrapartida: consome memória por entidade carregada, introduz comportamento implícito em caminho crítico e, em consulta de extrato com milhares de linhas, exige lembrar de desabilitá-lo. Além disso, o bloqueio pessimista de linha ([ADR-0005](./ADR-0005-controle-de-concorrencia.md)) e a cláusula `FOR UPDATE SKIP LOCKED` do consumidor de outbox ([ADR-0008](./ADR-0008-outbox-transacional.md)) exigiriam SQL bruto de qualquer forma.

Dapper entrega mapeamento objeto-relacional sem abrir mão do SQL explícito. Em sistema financeiro, SQL explícito é vantagem de auditoria: a consulta executada é a consulta escrita, revisável em code review e estável em plano de execução.

### Por que PostgreSQL

Atende todos os critérios: bloqueio de linha com granularidade controlável, constraints ricas, particionamento declarativo por intervalo (relevante para R-04), `SKIP LOCKED` maduro, privilégio por objeto para RNF-025, container leve para Testcontainers e ausência de custo de licença.

**Honestidade sobre a comparação:** SQL Server atende todos os critérios técnicos igualmente bem. `sp_getapplock` equivale a bloqueio consultivo, `READPAST` equivale a `SKIP LOCKED`, o modelo de permissões suporta privilégio mínimo. A decisão por PostgreSQL se dá por custo de licença e por peso de container em CI, não por superioridade técnica. Registrar isso é necessário: apresentar preferência como imposição técnica é desonestidade de arquitetura.

### Mitigação do acoplamento (R-06)

O SQL fica confinado aos adaptadores de persistência de cada módulo. O domínio não conhece o SGBD. Trocar o armazenamento significa reescrever adaptadores, não o domínio, e o teste de arquitetura ([ADR-0010](./ADR-0010-estrategia-de-testes.md)) impede que essa fronteira seja furada.

## Consequências

**Positivas**

- SQL explícito, revisável e com plano de execução previsível
- Latência de acesso a dados próxima do mínimo da plataforma, servindo RNF-001
- Privilégio mínimo aplicável por objeto, viabilizando RNF-025
- Migrações como scripts SQL versionados: legíveis por DBA, auditáveis e idempotentes
- Toda a stack sobe em container, atendendo RNF-037

**Negativas**

- Mapeamento manual de colunas para objetos, com mais código repetitivo que um ORM completo
- Sem migração gerada automaticamente a partir do modelo: o script é escrito à mão, com risco de divergência entre código e esquema. Mitigação: teste de integração que sobe banco vazio, aplica todas as migrações e exercita todos os caminhos (RNF-038)
- Acoplamento a particularidades do PostgreSQL nos adaptadores
- Equipe acostumada a EF Core enfrenta curva inicial

**Neutras**

- Minimal APIs em vez de Controllers: menos cerimônia, com organização por módulo via grupos de endpoint
- A ausência de abstração de repositório genérico é intencional; cada módulo expõe as operações que seu domínio precisa, não um `IRepository<T>`

## Análise das opções rejeitadas

**EF Core 10.** Rejeitado porque seu principal valor não se aplica a modelo append-only. *Voltaria a ser a melhor escolha* se o sistema incorporasse agregados mutáveis com grafos complexos de objetos, ou se a produtividade em CRUD passasse a dominar o esforço.

**Npgsql puro, sem Dapper.** Rejeitado por obrigar leitura manual de `DbDataReader` em todo mapeamento, com ganho de desempenho irrelevante diante do custo de legibilidade.

**Marten.** Armazenamento de documentos e event sourcing sobre PostgreSQL, tecnicamente adequado ao modelo. Rejeitado porque imporia seu próprio modelo de eventos e seu próprio mecanismo de concorrência, substituindo decisões que este projeto precisa tomar e justificar explicitamente ([ADR-0003](./ADR-0003-ledger-append-only.md), [ADR-0005](./ADR-0005-controle-de-concorrencia.md)). *Voltaria a ser considerado* se o sistema evoluísse para múltiplos agregados com event sourcing genuíno.

**EventStoreDB.** Rejeitado por introduzir um segundo sistema de armazenamento, inviabilizando a transação única que sustenta a invariante crítica, e por elevar o custo operacional sem resolver nenhum requisito que o PostgreSQL não resolva.

**.NET 9 (STS).** Rejeitado: janela de suporte curta é passivo em sistema bancário, que raramente é atualizado na cadência de uma release de curto prazo.

## Validação

- Build com `TreatWarningsAsErrors`, `Nullable=enable` e `AnalysisLevel=latest-recommended` em `Directory.Build.props` (RNF-034)
- Teste de integração a partir de banco vazio, aplicando todas as migrações (RNF-038)
- Analisador proibindo `float`/`double` em código de domínio ([ADR-0004](./ADR-0004-representacao-monetaria.md))

## Gatilho de revisão

1. Decisão corporativa de padronização de SGBD
2. Entrada de agregados mutáveis complexos no escopo
3. Fim da janela de suporte do .NET 10
