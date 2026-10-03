# ADR-0002: Adotar .NET 10 LTS com PostgreSQL, acesso a dados por Dapper e migrações por DbUp

- **Status:** Aceito
- **Data:** 2026-10-02
- **Decisor:** Eduardo J. G. do Carmo
- **Requisitos dirigentes:** RNF-001, RNF-003, RNF-025, RNF-037, RNF-038, R-06

> **Revisado em 2026-10-02 (card 27).** A decisão original escolhia o DbUp, mas não dizia onde a migração roda. A revisão, no fim deste documento, fixa: num passo separado, com o papel de migração, antes de a API subir.

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

- Build com `TreatWarningsAsErrors`, `Nullable=enable` e `AnalysisLevel=latest-recommended` em `Directory.Build.props` (RNF-034). *Estado em 2026-10-03:* atendido no card 28 (`AnalysisLevel=latest` com `AnalysisMode=Recommended`, equivalente). Até ali o modo era `Default`; a elevação fez disparar 4 regras, todas corrigidas no código e nenhuma suprimida (lacuna L-06)
- Teste de integração a partir de banco vazio, aplicando todas as migrações (RNF-038)
- Analisador proibindo `float`/`double` em código de domínio ([ADR-0004](./ADR-0004-representacao-monetaria.md))

## Gatilho de revisão

1. Decisão corporativa de padronização de SGBD
2. Entrada de agregados mutáveis complexos no escopo
3. Fim da janela de suporte do .NET 10

## Revisão de 2026-10-02 (card 27): onde a migração roda

### Problema

Até o card 27, o esquema era aplicado pelo `docker-entrypoint-initdb.d` do container PostgreSQL. Esse mecanismo só roda na **primeira** criação do volume: toda mudança de esquema exigia `docker compose down -v`, apagando os dados. Não atende RNF-038 e não existe fora do ambiente local. O DbUp, escolhido acima, resolve o "o quê"; faltava decidir **onde** ele roda, e a escolha toca o [ADR-0009](./ADR-0009-seguranca-e-privilegio-minimo.md).

### Decisão

**Migrador próprio, `PacioliBank.Migrations`, executado como passo separado antes da API, com o papel `pacioli_migrator`, que aplica o que falta e termina.**

- Scripts em `db/migrations/NNNN_descricao.sql`, copiados para a saída do migrador; o DbUp registra cada script aplicado em `public.schema_versions` e só aplica os que faltam. Script aplicado não se edita: mudança de esquema é migração nova
- Uma transação por script: script com erro desfaz o que fez e não entra no diário
- Massa do ambiente local em `db/seed/`, com diário próprio (`public.seed_versions`), aplicada só quando `PACIOLI_SEED_LOCAL=true`. Fora do ambiente local, não existe
- No `docker compose`, o serviço `pacioli-migrations` depende do banco saudável, e a API depende de `service_completed_successfully`: migração que falha impede a API de subir sobre esquema incompleto
- A API continua com `pacioli_runtime` (`SELECT, INSERT` no ledger). A credencial capaz de alterar e apagar o ledger nunca fica com o processo que atende requisições
- Os testes de integração montam o banco pelo mesmo `SchemaMigrator`, não por cópia do script

### Alternativas rejeitadas

**A API aplica as migrações na subida, com a credencial de migração.** É o arranjo mais comum e o que o critério original do card pedia. Rejeitado porque contraria o [ADR-0009](./ADR-0009-seguranca-e-privilegio-minimo.md): o papel de migração "nunca é usado pela aplicação em execução". A API passaria a guardar, durante toda a vida do processo, a credencial com `UPDATE`, `DELETE` e DDL sobre o ledger, e a imutabilidade por privilégio viraria imutabilidade por disciplina. *Voltaria a ser considerado* se o ambiente de execução oferecesse credencial de uso único, revogada ao fim da migração e antes de a API atender a primeira requisição.

**A API aplica as migrações com o próprio papel `pacioli_runtime`.** Impossível sem conceder DDL ao papel da aplicação, o que é a alternativa anterior por outro caminho.

**Manter o `docker-entrypoint-initdb.d`.** Zero código. Rejeitado: não reaplica em volume existente (RNF-038), exige `down -v` a cada mudança e não tem equivalente em banco gerenciado.

**Ferramenta externa de migração (Flyway, Liquibase, sqitch).** Mesmo desenho de passo separado, sem código C#. Rejeitada por acrescentar uma toolchain fora do .NET (Java, no caso das duas primeiras) e por deixar o banco dos testes de integração montado por um caminho diferente do usado pelo ambiente: o DbUp roda dentro do processo de teste, a ferramenta externa exigiria outro container na suíte. *Voltaria a ser considerada* se houvesse padrão corporativo de migração.

### Consequências

- **Positiva:** mudança de esquema sem perder dados; o mesmo migrador monta o ambiente local e o banco de cada teste
- **Positiva:** falha de migração é visível e bloqueante (código de saída diferente de zero, API não sobe)
- **Negativa:** um container e uma imagem a mais no `docker compose`
- **Negativa:** migração e versão da API sobem em passos distintos; uma migração precisa ser compatível com a versão anterior da API enquanto as duas coexistirem. Hoje há uma única instância e o passo precede a API, então a janela é nula no ambiente local. [NVI] Em implantação com várias instâncias, a regra de compatibilidade (expandir antes, contrair depois) ainda não está escrita
- **Transição, declarada:** bancos locais criados pelo mecanismo antigo não têm o diário e exigiram um último `docker compose down -v`

### Validação feita

- 7 testes de integração (`MigrationTests`), contra PostgreSQL real: banco vazio fica com as 5 tabelas; segunda execução não aplica nada; migração nova é aplicada sozinha e preserva os dados; migração com erro não deixa tabela parcial nem registro; delimitador nomeado (`$corpo$`) chega intacto; massa local só quando pedida e uma única vez; o papel da aplicação não consegue apagar o diário
- Poder de detecção medido: sem a transação por script, o teste de falha atômica reprova; sem desligar as variáveis do DbUp, o teste do delimitador nomeado reprova
- Regra de arquitetura: o migrador não depende do Ledger, de Events, da API nem de ASP.NET
- `docker compose up --build` a partir do zero: migrador termina com código 0, API saudável. Recriados o migrador e a API sobre o mesmo volume: "No new scripts need to be executed" e o crédito gravado antes continua na posição

### Gatilho de revisão desta parte

1. Implantação com mais de uma instância da API, que exige escrever a regra de compatibilidade entre migração e versão
2. Ambiente com credencial de uso único, que reabre a alternativa de migrar na subida da API
