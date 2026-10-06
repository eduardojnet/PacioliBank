# Estado do Projeto

**Documento vivo.** Atualizado a cada entrega. Descreve o que existe, o que falta e o que está decidido, sem otimismo.

**Última atualização:** 2026-10-06 (trigésima quarta revisão)
**Build:** verde, 0 avisos, 0 erros, os 9 projetos da solução (5 de produção, 4 de teste), analisadores em modo `Recommended` (`dotnet build --no-incremental`, verificado em 2026-10-06, sobre a mudança do card 47, ainda sem commit)
**Testes:** 237 passando (117 de domínio, 6 de arquitetura, 2 de contrato, 112 de integração), 0 falhando (`dotnet test`, xunit v3 na Microsoft Testing Platform, verificado em 2026-10-06). Cobertura de linha do domínio: 94,11% (coverlet.MTP), medida pelos testes de domínio em Release; o CI reprova abaixo de 85%. Teste de mutação do domínio: 86,96% de mutantes mortos; o CI reprova abaixo de 85%
**Verificação manual:** `docker compose up --build` servindo os 6 endpoints de negócio; 21 cenários exercitados via curl em 2026-10-02 (card 19); a transferência, com reenvio e duas recusas, em 2026-10-05 (card 38); valor como número, com reenvio de comando anterior à mudança e texto recusado, e as quatro demonstrações do painel em Chrome headless, em 2026-10-06 (card 47).

---

## 1. Identificação

| | |
|---|---|
| Produto | **PacioliBank Ledger** |
| Repositório | `pacioli-bank-ledger` |
| Propósito | Desafio técnico de Arquiteto de Software |
| Domínio | Livro-razão de contas correntes: registra movimentações e responde à posição consolidada em qualquer instante |
| Teto de tempo | 16h a 24h de trabalho efetivo |
| Plataforma | .NET 10.0.12 (SDK 10.0.401), PostgreSQL 17 |

O nome refere-se a Luca Pacioli, que codificou as partidas dobradas em 1494. O sistema adota ledger append-only com correção por lançamento compensatório, que é o mesmo método: registro imutável, erro corrigido por contrapartida, nunca por rasura.

---

## 2. Requisitos obrigatórios do desafio

| Requisito | Estado |
|---|---|
| Implementação em C# | Atendido |
| Testes automatizados | Atendido, 237 passando |
| Código compila sem erros e sem avisos | Atendido, `TreatWarningsAsErrors` ativo |
| README com instruções de execução local | Atendido: revisão final feita (card 23), verificada seguindo o README num clone limpo |
| Toda documentação no próprio repositório | Atendido: diagramas C4 em Mermaid em [`docs/diagrams/`](./diagrams/) (L-02 encerrada) |
| Repositório público no GitHub | Atendido: https://github.com/eduardojnet/PacioliBank (ver §6, L-01) |

> O enunciado declara que o teste é desconsiderado se os requisitos obrigatórios não forem minimamente atendidos. Desde o card 23, os seis requisitos obrigatórios estão atendidos.

---

## 3. Decisões fechadas

Onze ADRs em formato MADR, em [`docs/adr/`](./adr/). Cada um com alternativas rejeitadas e gatilho de revisão.

| ADR | Decisão |
|---|---|
| 0001 | Monolito modular; Ports and Adapters com DDD tático no interior |
| 0002 | .NET 10 LTS, PostgreSQL 17, Dapper, DbUp |
| 0003 | Ledger append-only como única fonte da verdade; posição é derivada |
| 0004 | `decimal` e `numeric(19,4)`; `Money` com moeda embutida; número JSON na API, com as casas da moeda (revisado em 2026-10-06, card 47: era string) |
| 0005 | Serialização por conta via `FOR NO KEY UPDATE`; constraint única como rede |
| 0006 | Chave de idempotência obrigatória; detecção pela violação de chave primária |
| 0007 | Snapshot síncrono amortizado a cada 100 lançamentos; sem projetor assíncrono |
| 0008 | Outbox transacional, entrega ao menos uma vez |
| 0009 | Imutabilidade por ausência de privilégio; resposta opaca a terceiros |
| 0010 | Banco real (Testcontainers) para toda invariante de persistência |
| 0011 | Painel de evidência como página estática, escopo condicional |
| 0012 | Log mascarado no formatador, correlação fora do adaptador HTTP, telemetria por OTLP (card 33) |

### Questões de negócio decididas

| ID | Questão | Decisão |
|---|---|---|
| QA-003 | Estorno pode gerar posição negativa? | **Não.** Respeita RN-001, pela mesma validação de `Account.Post`. Trocar a decisão é mudar esse ponto; não há política isolada (corrigido no card 29, L-14) |
| RN-012 | Manter bitemporalidade (`occurredAt` separado de `recordedAt`)? | **Sim**, mantida no escopo |

### Decisões de processo, fora de ADR

| Decisão | Registro |
|---|---|
| `CLAUDE.md` **só local, fora do repositório** (decisão do usuário em 2026-10-05, card 43; reverte a de 2026-10-02, que o versionava) | Retirado por `6d2e2d8` e ignorado por `755568e`. Não será restaurado. Motivo não declarado. As regras invioláveis passaram a [`docs/REGRAS.md`](./REGRAS.md), versionado, e as citações apontam para lá (L-17, card 43.1). O histórico continua com as versões antigas do arquivo, porque histórico publicado não é reescrito |
| `.claude/` permanece ignorado | Contém permissões locais de execução, que são do ambiente e não do projeto |
| Commits agrupados por decisão, não por arquivo | O enunciado avalia como se pensa e prioriza; push único sinaliza ausência de processo |
| Convenção de mensagem: Conventional Commits | Uma mensagem fora do padrão já entrou no histórico ("Atualizando CLAUDE.md no .gitignore"), e o histórico é lido pelo avaliador. Não reescrever: histórico publicado é imutável, pelo mesmo princípio do ledger |
| **Dívida aceita:** três mensagens de commit fora da convenção em 2026-10-05: `ae209cd` ("Melhorias no README.md", card 41), `6d2e2d8` ("Delete CLAUDE.md") e `755568e` ("Modificações necessárias", que põe o `CLAUDE.md` no `.gitignore`); as duas últimas, card 43 | Mesmo critério: o histórico já publicado não é reescrito. A mensagem não descreve a mudança; quem lê o histórico encontra a descrição aqui e no cartão. Regra mantida para os commits seguintes |

### Questões de negócio ainda abertas

QA-001 (limite ou cheque especial), QA-002 (política de lançamento retroativo), QA-004 (retenção do ledger), QA-005 (multimoeda), QA-006 (volume real), QA-007 (modelo de identidade), QA-008 (tamanho de página), QA-009 (regras da transferência, card 38). Detalhes, conduta provisória e o ponto do código que cada resposta mudaria na [EF §10](./specs/EF-especificacao-funcional.md).

Continuam sem resposta porque o desafio não tem interlocutor de negócio. O card 29, que pedia respondê-las, foi **encerrado como decisão registrada** em 2026-10-04: o que o desafio pode entregar é a lacuna declarada, com conduta provisória e risco, e não a resposta.

---

## 4. O que está implementado

### Domínio (`src/PacioliBank.Ledger`)

Zero dependências externas. A ausência de `PackageReference` é a garantia estrutural de que o domínio não alcança infraestrutura.

| Arquivo | Conteúdo |
|---|---|
| `Domain/Money.cs` | Value Object com moeda embutida; construtor privado; escala validada na criação |
| `Domain/Currency.cs` | ISO 4217, catálogo restrito a BRL |
| `Domain/Account.cs` | Agregado raiz; valida invariantes e produz lançamentos |
| `Domain/LedgerEntry.cs` | Fato imutável, construtor interno; `Rehydrate` reconstrói o já gravado, para o estorno |
| `Domain/PostingRequest.cs`, `ReversalRequest.cs` | Comandos de lançamento e de estorno |
| `Domain/Transfer.cs` | Card 38, ADR-0014: serviço de domínio da transferência; decide as duas pernas por `Account.Post` em cada agregado; `TransferRequest`, `TransferLegs` |
| `Domain/AccountStatus.cs`, `EntryDirection.cs` | Enums alinhados às colunas do banco |
| `Domain/LedgerDomainException.cs` | Exceção raiz mais 17 derivadas, uma por regra violável |
| `Application/ILedgerStore.cs` | Porta de saída, estreita e orientada a caso de uso: lançamento, estorno, transferência, posição, extrato |
| `Application/ILedgerService.cs`, `LedgerService.cs` | **Porta de entrada** (L-03): conversão do valor, impressão do comando, validação de instante e de página, antes de qualquer I/O |
| `Application/Commands.cs` | `PostingCommand`, `ReversalCommand`, `TransferCommand`, `StatementQuery`, `StatementEntry`, `StatementPage` |
| `Application/TransferResult.cs`, `TransferResponse.cs` | Card 38: resultado e corpo da transferência (EF §8.4.1), gravado em `response_body`; do destino, só o identificador do lançamento |
| `Application/PostEntryResult.cs`, `BalanceResult.cs` | Contratos de saída |
| `Application/PostingResponse.cs` | Corpo da resposta de escrita (EF §8.4), gravado em `response_body` e devolvido como texto na primeira resposta e na repetição |
| `Application/LedgerEntryEvent.cs`, `WireFormat.cs` | Payload dos eventos (EF §9), separado do resultado da API; formato de instante compartilhado com a API |
| `Application/RequestFingerprint.cs` | Impressão canônica SHA-256 do comando, com variantes próprias para estorno e transferência |

**Decisão de modelagem a defender:** o agregado não carrega os lançamentos da conta. É reidratado dentro da transação, sob bloqueio, com a posição corrente já calculada. Carregar o histórico para validar um débito reintroduziria a degradação do sistema legado.

### Persistência (`src/PacioliBank.Ledger.Persistence`)

| Arquivo | Conteúdo |
|---|---|
| `PostgresLedgerStore.cs` | Transação, bloqueio, idempotência, outbox, snapshot, nova tentativa; lançamento e estorno sob o mesmo bloqueio; transferência com as duas contas bloqueadas em ordem crescente de identificador e as duas pernas na mesma transação (card 38) |
| `LedgerSql.cs` | Todo o SQL reunido, comentado por decisão |

**A ordem dos passos dentro da transação é a arquitetura**, não detalhe de implementação: bloqueia a linha da conta, depois lê a posição, o agregado decide, e lançamento, sequência, idempotência e outbox são gravados juntos.

Instantes devolvidos são truncados para microssegundos, a precisão do `timestamptz`: sem isso, a primeira resposta e a repetição idempotente (lida do banco) diferiam no `recordedAt`, contra a EF §8.4. Defeito encontrado no teste via curl, coberto por teste desde então.

### Eventos (`src/PacioliBank.Events`, card 24)

| Arquivo | Conteúdo |
|---|---|
| `OutboxDispatcher.cs` | Uma passada, uma transação: lote com `FOR UPDATE SKIP LOCKED`, publicação, marcação ou recuo exponencial; limite de tentativas com alerta |
| `IEventPublisher.cs` | Porta de saída para o barramento, que não foi escolhido (ADR-0008) |

Sem referência ao `Ledger`: lê a outbox pelo contrato da tabela. Na API, `Events/OutboxDispatcherService.cs` roda o despachante em laço e `Events/LoggingEventPublisher.cs` publica em log.

### API (`src/PacioliBank.Api`)

| Arquivo | Conteúdo |
|---|---|
| `Program.cs` | Raiz de composição: único lugar que conhece todas as camadas; `/health/ready` consulta o PostgreSQL |
| `Endpoints/LedgerEndpoints.cs` | Os 6 endpoints da EF §8.3, como adaptador fino sobre `ILedgerService`; `POST /api/v1/transfers` desde o card 38 |
| `Endpoints/LedgerProblems.cs` | Único mapa de exceção para `application/problem+json` com `code` da EF §8.6 |
| `Endpoints/Contracts.cs` | Corpos de requisição e resposta; valores monetários como número JSON com as casas da moeda (texto na entrada é recusado), instantes ISO 8601 UTC; OpenAPI com `"format": "decimal"` nos campos monetários (card 47) |
| `wwwroot/` | Painel de evidência (ADR-0011): quatro demonstrações na raiz, só com a API pública; sem teste automatizado próprio, verificado em Chrome headless |
| `Endpoints/Correlation.cs` | `X-Correlation-Id` aceito ou gerado, devolvido inclusive em resposta de erro |
| `Observability/` | Card 33, ADR-0012: log em JSON na saída padrão (Serilog), uma linha por entrada; correlação da requisição em toda entrada emitida durante ela; uma linha de resumo por requisição; mascaramento no formatador (todo GUID truncado aos quatro últimos caracteres, CPF e token por marcador), coberto por teste pelo caminho completo |
| `Observability/ObservedLedgerService.cs` | Card 33.1: decorador de `ILedgerService` que abre o span do caso de uso; requisição, caso de uso e PostgreSQL no mesmo traço, exportado por OTLP só com `OTEL_EXPORTER_OTLP_ENDPOINT`. Só comando dentro de traço é rastreado. Painel Aspire 13.6.0 no `docker compose`, em `http://localhost:18888` |
| `Observability/LedgerTelemetry.cs`, `OutboxMetrics.cs` | Card 33.2: seis métricas de negócio (RNF-032) no medidor `PacioliBank.Ledger`: lançamentos por sentido, reenvios, rejeições pelo código da EF §8.6, consultas por origem do cálculo, lançamentos somados além do snapshot e fila da outbox (pendente e estacionada) |

Repetição idempotente responde `200` com `Idempotency-Replayed: true` e corpo idêntico ao original; lançamento novo responde `201`.

### Banco e migrações (`db/migrations/`, `src/PacioliBank.Migrations`, card 27)

O esquema é aplicado por migrações numeradas (DbUp, `dbup-postgresql` 7.0.1), num passo separado do `docker compose` (`pacioli-migrations`): roda com `pacioli_migrator`, aplica só o que falta, registra em `public.schema_versions` e termina. A API depende de `service_completed_successfully` e continua com `pacioli_runtime`. Transação por script: migração com erro não deixa alteração parcial nem registro. A massa local (`db/seed/`) tem diário próprio e só roda com `PACIOLI_SEED_LOCAL=true`. Os testes de integração montam o banco pelo mesmo `SchemaMigrator`. Decisão e alternativas rejeitadas na revisão do ADR-0002.

Esquema `ledger` com 8 tabelas e 3 papéis. **Valores numéricos** (migração 0005, card 47): converte para número os valores monetários gravados como texto em `idempotency_records.response_body` e `outbox_messages.payload`; aplicada ao volume local em uso, com 284 respostas e 290 mensagens convertidas e o restante do texto idêntico. **Transferências** (migração 0004, card 38, ADR-0014): `transfers` amarra as duas pernas por chaves estrangeiras compostas que conferem conta, sentido, valor, moeda e instante de registro; perna reaproveitada e origem igual ao destino são recusadas pelo banco. **O ledger é particionado por mês de registro** (migração 0003, card 37, ADR-0013): as restrições de identidade, sequência, idempotência e estorno único vivem em `entry_keys`, tabela não particionada gravada na mesma transação, e cada linha do ledger é amarrada à sua chave por chave estrangeira composta; o migrador abre as partições dos próximos 12 meses a cada execução. A sexta, `daily_balances` (migração 0002, card 32), guarda o fechamento diário por conta: a posição em instante passado parte do fechamento do dia anterior e soma só os lançamentos do próprio dia; a escrita mantém os fechamentos, inclusive os dias seguintes a um retroativo, na mesma transação. Cada constraint carrega uma regra: `uq_entries_sequence` (RN-006), `uq_entries_idempotency` (RN-005), `uq_entries_reversal` (RN-004), `CHECK (amount > 0)` (RN-002).

O papel `pacioli_runtime` recebe `SELECT, INSERT` no ledger e nada mais. Alterar um lançamento gravado é impossível para a aplicação, qualquer que seja o código.

### Testes

| Projeto | Quantidade | Escopo |
|---|---|---|
| `PacioliBank.Domain.Tests` | 117 | Invariantes puras, transferência, validações da porta de entrada e contratos de fio (corpo de escrita e de transferência, evento, impressão do comando), sem I/O. Cobertura de linha do `PacioliBank.Ledger`: 94,11% (coverlet.MTP); teste de mutação 87,06% (cards 36, 34.1 e 38) |
| Coleção do Insomnia (`insomnia/`) | 54 testes em 18 requisições (52 até o card 47, que acrescentou a verificação da escala no texto da resposta) | Contra a API no ar, pelo `inso` 13.3.0, com o sub-ambiente `Local` (card 45); código de saída 0 só com tudo verde. Execuções de 2026-10-06 contra o `docker compose`: no runner do aplicativo Insomnia 13.1.0, 18 requisições e 52 testes verdes, executada pelo usuário às 01:59 UTC, ainda com as variáveis no *Base Environment* (card 46); pelo `inso` 13.3.0 com o `Local`, 52/52 duas vezes, e com o *Base Environment* vazio reprova com código 1, como controle |
| `PacioliBank.Architecture.Tests` | 6 | Regras de dependência com NetArchTest 1.3.2; cada regra reprova o que viola (medido em duas delas). A sexta, do card 27, isola o migrador do domínio e dos módulos |
| `PacioliBank.Contract.Tests` | 2 | OpenAPI gerado comparado com o instantâneo aprovado (`openapi.v1.approved.json`); reprova quando o contrato muda (medido) |
| `PacioliBank.Integration.Tests` | 112 | PostgreSQL real via Testcontainers, incluindo concorrência, estorno (F06), transferência (F11: atomicidade com falha provocada, ordem dos bloqueios verificada de forma determinística, pernas amarradas pelo banco), extrato (F05), reenvio após mudança de estado (L-10), despachante de outbox (F08), migrações (7, card 27: banco vazio, reexecução, evolução, falha atômica, delimitador nomeado, massa local, diário fora do alcance da aplicação) e snapshot (3, card 30.1: gravação na centésima escrita, limite de 99 somados, posição histórica sem snapshot), log (9, card 33: mascaramento no formatador e caminho completo com API e PostgreSQL reais), rastreamento (2, card 33.1), métricas (1, card 33.2) e fechamento diário (5, card 32: posição histórica contra o ledger em 63 instantes, fechamentos, origem, privilégio e preenchimento pela migração); particionamento (9, card 37: duplicidade em outro período recusada pelo banco, partição do mês, migração com dados) |

Os testes de concorrência usam barreira de sincronização para liberar as tarefas no mesmo instante. Disparar em laço serializa por acidente de escalonamento e o teste perde o propósito.

### Integração contínua (`.github/workflows/ci.yml`, card 31)

A cada push, em qualquer ramo, e a cada pull request para `main`, três jobs em paralelo, em cerca de um minuto: build em Release com aviso tratado como erro e os quatro projetos de teste (integração com Testcontainers no Docker do runner), com resultados e cobertura publicados como artefato; a coleção do Insomnia pelo `inso` contra o `docker compose`; e o `gitleaks` no histórico completo. Ações fixadas por SHA; binários conferidos por sha256. Poder de detecção medido: um aviso plantado num ramo deixou o pipeline vermelho, com a anotação na linha. Um falso positivo do `gitleaks` está ignorado pela impressão digital em `.gitleaksignore`.

Auditoria de dependências (card 31.1): o restore audita as dependências, transitivas inclusive, e alerta alto ou crítico vira erro (`NuGetAudit` explícito no `Directory.Build.props`, modo `all`, nível `high`); vale localmente, no CI e na imagem. O CI lista as demais severidades num passo informativo. Em 2026-10-04, nenhum pacote vulnerável em nenhuma severidade.

Cobertura mínima (card 31.2): passo próprio mede a cobertura de linha do `PacioliBank.Ledger` só pelos testes de domínio, em Release, e reprova abaixo de 85%. Com isso, o CI aplica todo o critério de bloqueio do ADR-0010. Desde o card 34.1, o coletor é o `coverlet.MTP`: hoje 95,41% (94,45% antes do card 38), contra 88,15% na última medida do `coverlet.msbuild`. Os números não são comparáveis entre os coletores; a causa da diferença não foi decomposta [NVI]. O limite foi verificado de novo: sem os testes de contrato, 77,83%, e o passo reprova.

Teste de mutação (card 36): tarefa própria do CI roda o Stryker.NET 5.0.0 (fixado em `dotnet-tools.json`) sobre o domínio, em 17 segundos localmente e 64 no runner, em paralelo, e reprova abaixo de 85% de mutantes mortos. Num ramo de prova sem os testes de contrato, a tarefa reprovou (ramo apagado). Hoje: 87,06% (card 38: as cinco sobreviventes novas são das categorias já aceitas, quatro `Guid.ToString("D")` equivalentes e o valor padrão do corpo da resposta); antes, 89,17% com o executor `mtp` do Stryker (card 34.1; era 89,81% no VSTest, diferença não decomposta [NVI]); com limite de 95% a execução falha, o que confirma o limite. Exclusões e sobreviventes aceitos, todos com motivo, na revisão do ADR-0010. Localmente: `dotnet tool restore` e, em `tests/PacioliBank.Domain.Tests`, `dotnet stryker`.

### Especificações e diagramas

[BDD](./specs/BDD-comportamento.md) com 10 funcionalidades e ~55 cenários Gherkin, [EF](./specs/EF-especificacao-funcional.md) e [ENF](./specs/ENF-especificacao-nao-funcional.md), todas com rastreabilidade cruzada por IDs.

Modelo C4 em Mermaid em [`docs/diagrams/`](./diagrams/): contexto, contêineres, componentes da API e sequência do débito sob concorrência, mais o diagrama de entidades e relacionamentos do esquema (card 20.1), conferido contra o catálogo do PostgreSQL. Cada diagrama declara o estado de cada elemento (implementado, parcial ou especificado). Renderização dos quatro validada localmente com `mermaid-cli` antes do commit. Os três documentos do Lucid continuam para apresentação, mas deixaram de ser a fonte: C3 e C4 foram redesenhados a partir do código, e a correspondência com o Lucid está em `c3-componentes.md`.

---

## 5. O que NÃO está implementado

Declarar isto é parte da entrega. Apresentar requisito especificado como implementado seria, em contrato real, informação incorreta prestada ao cliente.

| Item | Situação |
|---|---|
| Autenticação e autorização (RF-009) | **Não implementadas.** Qualquer chamador opera qualquer conta. Os endpoints existem sem a borda de segurança do ADR-0009 |
| `description` e `metadata` do corpo (EF §8.4) | Aceitos e ignorados: o lançamento de domínio não os carrega e a coluna `metadata` fica com o padrão `{}` |
| Limite de taxa (RNF-012, código `RATE_LIMIT_EXCEEDED`) | Não implementado |
| Interface de exploração do OpenAPI | Não implementada; o documento `/openapi/v1.json` é gerado e importável pelo Insomnia |
| Barramento de eventos real | Não escolhido (ADR-0008). O despachante publica em log; nenhum consumidor externo recebe eventos |
| Expurgo das mensagens publicadas da outbox | Não implementado; a tabela cresce sem limite (ADR-0008, consequências) |
| Conciliação ledger × outbox (RNF-033) | Não implementada |
| Regra de compatibilidade entre migração e versão da API | Não escrita. Com uma instância e o migrador antes da API, a janela é nula; com várias instâncias, a migração precisa ser compatível com a versão anterior (expandir antes, contrair depois). [NVI] Ver a revisão do ADR-0002 |
| Testes de carga | Especificados na ENF §11, fora do escopo do desafio; card 30 encerrado como decisão registrada |
| Regras de negócio da transferência (QA-009) | Não definidas: estorno de transferência, tarifa, limite e moedas diferentes. Conduta provisória na EF §10; estornar uma perna pelo caminho comum desfaz só metade da transferência |
| Identificador da transferência nos eventos | Não incluído: cada perna gera o evento comum da sua conta, com a mesma correlação; juntar as pernas pelo evento exige versão do contrato (EF §9, ADR-0014) |
| Arquivamento de períodos do ledger (R-04) | Não implementado: depende da política de retenção (QA-004), sem resposta. O ledger já é particionado por mês (card 37); nada é arquivado ou apagado |

---

## 6. Lacunas conhecidas, por severidade

### L-01: Repositório público no GitHub (ENCERRADA em 2026-10-02)

Requisito obrigatório explícito. Atendido: https://github.com/eduardojnet/PacioliBank, público, branch `main`.

O histórico tem 7 commits agrupados por área (base, banco, domínio, persistência, API, decisões, estado). Foram criados no mesmo dia, ao versionar o trabalho já existente, e não reproduzem a cronologia original; nenhuma data foi alterada. A partir daqui cada entrega vira commit próprio. Que cada um dos 7 commits compile isoladamente não foi verificado [NVI].

### L-02: Diagramas fora do repositório (ENCERRADA em 2026-10-02, card 20)

**Situação registrada:** o enunciado exige toda a documentação no repositório, e os diagramas viviam só no Lucid. Além disso, mostravam componentes inexistentes (painel, despachante, autorização) como se existissem.

**Corrigido:** quatro diagramas em Mermaid em [`docs/diagrams/`](./diagrams/), renderizados nativamente pelo GitHub. Cada um traz uma tabela de estado por elemento, com a evidência no código, e uma convenção visual (borda tracejada para o especificado).

**Divergência registrada, não corrigida em silêncio:** o C3 do Lucid descreve oito componentes que o código organizou de outra forma (idempotência, cálculo de posição e outbox vivem dentro do `PostgresLedgerStore` e do SQL; a porta de entrada `LedgerService` não existia no Lucid). O C3 do repositório segue o código, com tabela de correspondência. O diagrama de sequência foi redesenhado a partir de `PostgresLedgerStore.PostOnceAsync` e torna visível a lacuna L-10.

### L-03: Hexágono implementado só no lado dirigido (ENCERRADA em 2026-10-02, card 19)

**Situação registrada:** `ILedgerStore` existia como porta de saída, mas não havia porta de entrada; quem chamava o store era o teste de integração.

**Corrigido:** `ILedgerService` é a porta de entrada. O endpoint HTTP é adaptador fino: traduz rota, cabeçalho, corpo e status, e não conhece o store. Conversão do valor monetário, impressão do comando (ADR-0006), validação de instante e de tamanho de página ficam na porta, de modo que uma segunda borda (fila, por exemplo) não reimplemente regra. A ligação entre as camadas acontece só em `Program.cs`.

### L-04: Poder de detecção do teste de concorrência não verificado (ENCERRADA em 2026-10-02, card 22)

**Situação registrada:** o ADR-0010 e o `ConcurrencyTests` afirmavam que o teste reprova uma implementação sem bloqueio. Nunca medido.

**Medido:** `FOR NO KEY UPDATE` removido, suíte executada três vezes, sonda de desfechos executada duas vezes, código restaurado e conferido por `git diff` vazio. O teste **reprova** nas três execuções. Mas nenhuma das duas hipóteses declaradas estava certa como escrita:

- **Sem o bloqueio, a invariante se mantém.** Nenhuma posição negativa, nenhuma lacuna de sequência: `uq_entries_sequence` mais a nova tentativa funcionam como controle otimista. A defesa em profundidade do ADR-0005 é real e agora medida
- **Sem o bloqueio, a disponibilidade desaba.** Entre 24% e 78% dos comandos esgotam as três tentativas e recebem `503`
- **O teste detecta a falta do bloqueio pela contagem exata dos desfechos**, não por saldo negativo, que não acontece

Registrado no ADR-0005 ("Validação empírica do bloqueio"), no ADR-0010 e no comentário do próprio teste. Achada no caminho e corrigida: a validação do ADR-0005 citava 200 créditos e 100 contas, e os testes usam 50 e 25, com motivo já documentado no teste.

### L-05: Correções pendentes nos documentos (ENCERRADA em 2026-10-02, card 21)

Quatro divergências estavam registradas e não aplicadas. Todas aplicadas, cada uma com nota de revisão no documento de origem dizendo o que mudou e por quê, em vez de edição silenciosa:

| Documento | Correção | Origem da regra |
|---|---|---|
| EF §8.6 e BDD F09 | Conta de terceiro responde `404 ACCOUNT_NOT_FOUND` ao cliente final; `403 FORBIDDEN` fica restrito a serviço interno | ADR-0009 §3 |
| ADR-0001 | Módulo `Integration` renomeado para `Events` | convenções §3 |
| ADR-0010 | Prefixo `Ledger.` dos projetos de teste substituído por `PacioliBank.` | convenções §3 |
| ADR-0009 | Papéis `app_*` substituídos por `pacioli_*` nos exemplos e na validação | convenções §5 |

**Achadas ao verificar o critério "coerentes com o código"**, e tratadas no mesmo card, com o escopo ampliado registrado no cartão:

| Documento | Divergência | Tratamento |
|---|---|---|
| ADR-0010 | Árvore de testes com Reqnroll, `.feature` executáveis e projeto próprio de concorrência; nada disso existe | Nota de estado: hoje há tradução dos cenários para xUnit, e ela pode divergir do BDD. A decisão não muda |
| ADR-0001 e convenções §3 | Módulos `Balances`, `Accounts`, `Events` e o projeto `Migrations` descritos como se existissem; `Persistence`, que existe, ausente | Nota de estado nos dois documentos. Árvore e tabela permanecem como alvo |
| EF §8.4 | `type` do problema ilustrado com URL; o código e as convenções usam URN | Exemplo corrigido para URN |

Notas de estado não alteram decisão: separam o alvo do que existe, pela [regra 5](./REGRAS.md).

### L-07: Código dado como escrito, mas ausente do disco (ENCERRADA em 2026-10-02, cards 19.1 e 19)

**Situação registrada:** uma revisão anterior dava como escritos `Application/Commands.cs` e três exceções (`EntryNotFoundException`, `PageSizeExceededException`, `InvalidPointInTimeException`). A verificação mostrou que **nenhum existia no disco**. Fica registrado em vez de apagado: era apresentar o especificado como implementado.

**Corrigido:** os cinco tipos de `Commands.cs` e as três exceções existem, mais `EntryAlreadyReversedException`, exigida pelo estorno. Todos compilados e cobertos por teste. A raiz de exceções tem agora 16 derivadas.

**Primeira ação de qualquer sessão nova continua sendo `dotnet test`**, e conferir no disco o que este documento afirma existir.

### L-08: Estorno sem caminho de persistência (ENCERRADA em 2026-10-02, card 19.1)

**Situação registrada:** o domínio tinha `Account.Reverse`, mas a persistência não o usava. Um `PostingRequest` com `ReversalOf` seria gravado sem validar titularidade nem estorno de estorno, e a violação de `uq_entries_reversal` saía como `PostgresException` crua.

**Corrigido:** `ILedgerStore.ReverseAsync`, na mesma transação e sob o mesmo bloqueio por conta do lançamento comum (ADR-0005). O original é lido pela chave primária e reidratado (`LedgerEntry.Rehydrate`); titularidade, estorno de estorno e saldo são decididos pelo agregado. A violação de `uq_entries_reversal` vira `EntryAlreadyReversedException`, depois de conferir se não é o reenvio legítimo do mesmo estorno: o reenvio viola as duas unicidades, e qual o banco reporta primeiro não é contratual [NVI].

**Verificado** por 9 testes de integração contra PostgreSQL real (`ReversalTests`), incluindo 10 estornos simultâneos do mesmo lançamento, dos quais exatamente um é aceito.

### L-09: README público subdeclarava o estado da implementação (ENCERRADA em 2026-10-02)

Detectada ao verificar o repositório recém-publicado. A tabela "Estado atual da implementação" do README declarava como **pendentes** o agregado Conta, a idempotência, o controle de concorrência e os testes de integração, todos implementados e verdes. A árvore de estrutura omitia `PacioliBank.Ledger.Persistence` e `PacioliBank.Integration.Tests`.

**Causa:** o README foi escrito antes dessas entregas e não foi revisado quando elas entraram. A regra 9 das invioláveis cobria o `ESTADO.md`, não o README.

**Por que importa mais do que parece:** a §5 deste documento e a regra 5 existem contra **sobre**declarar. Aqui o erro foi o inverso, e no desafio o dano é maior: o avaliador lê "pendente" na primeira tela e não procura o código. Entrega existente avaliada como ausente.

**Corrigido:** tabela de estado reescrita com nove linhas separando implementado de pendente, árvore de estrutura atualizada para os cinco projetos reais, comando de teste declarando 57 testes e o pré-requisito de Docker, e a promessa de DbUp "na próxima entrega" trocada por item da fila.

**Controle adotado:** o README entra na mesma verificação da regra 9. Nenhuma entrega fecha com README divergente da §4 deste documento.

### L-10: Reenvio idempotente recusado quando o estado da conta mudou (ENCERRADA em 2026-10-02, card 19.4)

**Situação registrada, verificada por execução:** crédito de 150, débito de 100 com chave `k`, reenvio do mesmo débito. Esperado pela RN-005: o resultado original. Obtido: `422 INSUFFICIENT_FUNDS`. O agregado decidia antes de a repetição ser reconhecida, e a repetição só era reconhecida na gravação. Valia também para conta bloqueada depois do envio original, e reusar a chave com outro conteúdo devolvia `422` em vez de `409`.

**Corrigido:** sob o bloqueio da conta e antes do agregado, o registro da chave é lido; existindo, o reenvio recebe o resultado original, ou `409` se o conteúdo difere. Sob o bloqueio não há a janela de corrida que o ADR-0006 rejeitava, porque o registro de uma chave só é gravado por quem detém o mesmo bloqueio. A violação de chave primária continua como segunda barreira.

**Decisão registrada:** revisão do ADR-0006 com três alternativas rejeitadas (ler só quando o agregado rejeita; gravar registro também para rejeições; manter e documentar) e gatilho de revisão próprio: qualquer caminho de escrita sem o bloqueio da conta invalida o argumento.

**Verificado:** três testes de integração novos, que reprovaram antes da correção e passam depois; suíte com 97 verdes; o cenário original reproduzido via curl no ambiente Docker, agora com `200` e corpo idêntico. Diagrama de sequência redesenhado com o passo novo.

### L-11: Decisões de contrato tomadas na implementação, sem respaldo na EF (ENCERRADA em 2026-10-02, card 21)

Ao escrever os endpoints, seis pontos não tinham resposta na EF. Cada um recebeu conduta provisória, registrada aqui para não virar suposição silenciosa. Todas são reversíveis sem migração de dados.

| Ponto | Conduta provisória | Por quê |
|---|---|---|
| Lançamento inexistente, ou de outra conta, no estorno | `404 ENTRY_NOT_FOUND`, código ausente da EF §8.6 | Responder diferente para "de outra conta" revelaria a existência do lançamento, mesmo princípio do ADR-0009 para contas |
| Corpo malformado, campo obrigatório ausente, cursor ilegível | `400 INVALID_REQUEST`, código ausente da EF §8.6 | Erro de protocolo, sem regra de negócio correspondente; sem código, o chamador recebia 400 sem corpo |
| Moeda fora do catálogo (ex.: `USD`) | `400 CURRENCY_MISMATCH` | O BDD F01 espera esse código; para o chamador, é moeda divergente da conta (QA-005) |
| Tamanho de página do extrato | Padrão 50, máximo 200 | A EF não fixa valor; o BDD F05 usa 50 e recusa 10000 |
| `occurredAt` no estorno | Obrigatório no corpo | Sem ele, o padrão "agora" mudaria a cada reenvio e a repetição viraria conflito de chave |
| `X-Correlation-Id` que não é GUID | Substituído por um GUID gerado, devolvido no cabeçalho | O domínio usa GUID; a EF não define formato |

**Incorporado à EF 1.1:** §8.7 com as decisões de contrato, códigos `ENTRY_NOT_FOUND` e `INVALID_REQUEST` no catálogo da §8.6, e o tamanho de página como questão aberta QA-008 na §10, por ser decisão de produto e não de engenharia. Continuam provisórias e marcadas `[INFERIDO]`: encerrar a lacuna significa que deixaram de ser silenciosas, não que foram validadas pelo negócio.

### L-12: Ausência de chave estrangeira na outbox sem decisão registrada (ENCERRADA em 2026-10-02, card 24.1)

**Situação registrada:** `outbox_messages` não tinha chave estrangeira, e nenhum documento dizia por quê; o cartão 20.1 atribuía uma justificativa ao ADR-0008 que o ADR não continha.

**Decidido pelo usuário e aplicado:** chave composta `fk_outbox_entry (account_id, sequence)` para `ledger_entries`. Registrada no ADR-0008 com três alternativas rejeitadas (chave simples para `accounts`; coluna `entry_id`; manter sem chave). Teste novo, com o papel da aplicação, verifica a recusa de mensagem sem lançamento; reprovou antes da mudança. ERD atualizado e reconferido contra o catálogo (6 FK). **Ambiente local:** mudança de esquema exige `docker compose down -v`.

### L-16: Observabilidade dada como realizada sem existir (ENCERRADA em 2026-10-04, card 33)

A ENF §11 listava RNF-031 (log estruturado com correlação) e RNF-032 (métricas de negócio) como "realizadas no código". A API usava o logger padrão do ASP.NET Core, em texto livre, sem correlação, sem mascaramento (exigido pelo ADR-0009 §5) e sem nenhuma métrica.

**Corrigido no card 33:** RNF-031 implementada de fato, com teste pelo caminho completo e poder de detecção medido (ADR-0012). ENF 1.6: RNF-032 volta a apenas especificada até o card 33.2, e RNF-030 até o 33.1.

**Achado no caminho:** com o resto do log em JSON, apareceu uma linha em texto livre do Npgsql, que tentava carregar `libgssapi_krb5`, ausente na imagem (12 vezes no migrador). Negociação GSS desligada nas connection strings locais.

**Severidade MÉDIA:** além da afirmação falsa, faltava o mascaramento que o ADR-0009 exige; identificadores de conta saíam íntegros no log do despachante.

### L-15: Requisitos de desempenho dados como realizados sem ressalva nem teste (ENCERRADA em 2026-10-04, card 30)

Ao encerrar o card 30, a ENF §11 foi conferida contra o código. Ela listava RNF-003 (custo de leitura não cresce com o histórico) e RNF-006 (no máximo 1.000 lançamentos somados após o snapshot) como "realizados no código", e o README dizia "snapshot amortizado: implementado, com testes". O que vale:

- **Posição corrente:** snapshot gravado a cada 100 lançamentos (`SnapshotEvery`), então no máximo 99 somados depois dele. RNF-006 vale por construção, mas **nenhum teste automatizado** verifica que o snapshot é gravado ou usado; a evidência era a demonstração manual do painel (card 25)
- **Posição histórica:** não usa snapshot; soma todos os lançamentos até o instante pelo índice. Para ela, RNF-003 e RNF-006 não valem. A limitação já estava no card 32, com gatilho
- A latência da RNF-003 nunca foi medida

**Corrigido:** ENF 1.4 (§11 com as ressalvas) e README (snapshot sem teste automatizado). **Trabalho que virou card:** 30.1, teste de integração do snapshot e do limite de replay, com poder de detecção. *Feito no card 30.1:* `SnapshotTests`, três testes, três mutações detectadas; ENF 1.5 e README voltam a dizer "com teste" para a posição corrente.

**Severidade BAIXA:** nenhum comportamento muda. Mesma família da L-09, L-13 e L-14: documento afirmando mais do que o código prova.

### L-14: Ponto de extensão declarado e inexistente nas questões de negócio (ENCERRADA em 2026-10-04, card 29)

Ao encerrar o card 29, as condutas provisórias da EF §10 foram conferidas contra o código. Três afirmações não valiam:

- A EF dizia que cada conduta estava "isolada em ponto de extensão", alterável por "configuração ou uma política". Não há política nem configuração para nenhuma. A validação de saldo de QA-001 (limite) e QA-003 (estorno) está escrita em `Account.Post`; o ESTADO §3 chamava isso de "política isolada para troca futura"
- QA-007 tinha como conduta "JWT validado contra emissor externo", mas não há autenticação alguma (RF-009 pendente, já declarado no README e na §5)
- QA-003 estava decidida no ESTADO §3 e aberta na EF e no BDD

**Corrigido:** EF 1.5 e BDD 1.2 descrevem o que existe, um ponto único de mudança por questão, coberto por teste; QA-003 marcada como decidida nos três documentos; QA-007 como especificada e não implementada. As políticas não foram criadas: fazê-lo antes da resposta do negócio seria construir contra premissa não validada.

**Severidade BAIXA:** nenhum comportamento muda; a afirmação exagerava a flexibilidade do desenho. Mesma família da L-09 e da L-13.

### L-13: RNF-036 declarada realizada sem nunca ter sido medida (ENCERRADA em 2026-10-04, card 31.2)

A ENF §11 listava a RNF-036 (cobertura de linha ≥ 85% no projeto de domínio) como "realizada no código", e o ADR-0010 a põe no critério de bloqueio. Ninguém tinha medido. Medida no card 31, no projeto `PacioliBank.Ledger`, com o coverlet: **56,8%** só com os testes de domínio, **49,9%** só com os de integração, **74,9%** somando os dois (união das linhas cobertas).

**Já feito:** ENF corrigida para a versão 1.2 (RNF-036 como apenas especificada); nota de estado no ADR-0010; README declara a meta como não atendida.

**Pendente, card 31.2, por decisão do usuário:** (a) escrever testes até 85% e ligar o limite no CI; (b) revisar a meta no ADR-0010 e na ENF, com alternativa rejeitada; ou (c) manter a cobertura informativa e a RNF-036 como especificada.

**Severidade MÉDIA:** não há defeito de comportamento; há uma afirmação falsa sobre verificação, do mesmo tipo da L-09.

**Resolução (opção a, decidida pelo usuário):** cobertura de linha do `PacioliBank.Ledger` medida só pelos testes de domínio, em Release, de **56,8% para 86,4%**, com limite de 85% no CI. Dois movimentos:

- **40 construtores de exceção sem uso removidos.** Construtores "padrão" escritos por hábito (a CA1032, que os exigiria, não está ativa), que ninguém chamava e que permitiam criar, por exemplo, saldo insuficiente sem os valores que a API devolve. Testá-los subiria o número sem provar nada; excluí-los da medida esconderia código
- **19 testes de domínio**, sobre comportamento observável: corpo de escrita e payload de evento campo a campo, instante em UTC com `Z`, impressão do comando (estável na correlação, sensível a valor, sentido, conta; estorno nunca coincide com lançamento), reidratação do lançamento, defesas da conta (valor sem moeda, instante ausente, identificador vazio), estorno pela porta de entrada

Medida em Release (86,4%) e não em Debug (87,9%): o CI compila em Release. Poder de detecção: sem os testes de contrato, 72,8%, e o passo reprova (localmente e num ramo de prova no CI). Método e alternativas rejeitadas na revisão do ADR-0010. ENF na versão 1.3.

### L-17: Documentos citam regras de um arquivo que não está no repositório (ENCERRADA em 2026-10-05, cards 43 e 43.1)

Com o `CLAUDE.md` fora do repositório, o `PROCESSO-KANBAN.md` (linhas 44 e 257), esta página (§6, nota de estado), `docs/diagrams/README.md` e `docs/DECISOES-EM-RESUMO.md` citam "regra N do `CLAUDE.md`", que o leitor não consegue consultar. Em `DECISOES-EM-RESUMO.md` a citação é um link relativo, que fica quebrado. O histórico dos cartões no `KANBAN.md` também o menciona; ali é registro de época e não se altera.

**Risco:** o avaliador encontra justificativas que remetem a regras invisíveis, e um link quebrado no documento de decisões.

**Conduta provisória:** nenhuma alteração até decisão do usuário. Opções: (a) trazer as regras invioláveis para um documento versionado e repontar as citações; (b) trocar as citações pelo texto da própria regra; (c) remover só o link quebrado.

**Resolução:** opção (a), por decisão do usuário. As dez regras foram para [`docs/REGRAS.md`](./REGRAS.md), com o texto da última versão versionada do `CLAUDE.md` (`6d2e2d8~1`). As cinco citações vivas apontam para lá, e o link quebrado saiu. O `DECISOES-EM-RESUMO.md` dizia "nove regras"; são dez, e o texto foi corrigido. O README lista o arquivo novo. O histórico dos cartões no `KANBAN.md` não foi alterado.

### L-06: `AnalysisMode` ainda em `Default` (ENCERRADA em 2026-10-03, card 28)

O ADR-0002 previu `latest-recommended` após o primeiro build limpo. O build está limpo há três ciclos. Elevar é um commit próprio e pequeno.

**Resolução:** `AnalysisMode=Recommended` no `Directory.Build.props`. Elevado o modo, o build reprovou com 4 regras, todas corrigidas no código e nenhuma suprimida:

- **CA1716** (palavra reservada como nome de parâmetro): `to` em `ILedgerStore.GetStatementAsync` passou a `until`; o nome do parâmetro SQL (`@to`) e o da API não mudaram
- **CA1862** (comparação sem distinguir maiúsculas): `Currency.TryFromCode` compara com `OrdinalIgnoreCase` em vez de converter para maiúsculas. Antes da troca, um teste novo fixou o comportamento (7 casos: aceita `brl`, `BRL` e ` bRl `; recusa `USD`, `BRLX`, vazio e nulo), porque a validação da porta de entrada passa por esse método e nenhum teste o cobria
- **CA1711** (sufixo reservado no nome do tipo): `LedgerCollection` dos testes passou a `LedgerCollectionDefinition`
- **CA1859** (tipo concreto para desempenho): dublê de publicador dos testes expõe `List` em vez de `IReadOnlyList`

Supressões que já existiam e continuam, cada uma com o motivo ao lado: CA1707 nos projetos de teste (nomes em português com sublinhado) e CA1031 em `OutboxDispatcher` (falha do publicador vira nova tentativa). O card não acrescentou nenhuma.

---

## 6-A. Gestão de projeto

O quadro Kanban vive no TickTick, projeto **PacioliBank**, com 65 cartões distribuídos em 7 colunas, numerados conforme a convenção do `PROCESSO-KANBAN.md` §4. `docs/KANBAN.md` é o espelho em texto, versionado no repositório.

**O quadro é a fonte** de toda atividade e da ordem de execução ([`PROCESSO-KANBAN.md`](./PROCESSO-KANBAN.md) 2.0, card 18.1). A §7 abaixo e o `KANBAN.md` são espelhos dele; em divergência, vale o quadro. Até a versão 1.2 da política era o inverso.

A política do quadro está em [`docs/PROCESSO-KANBAN.md`](./PROCESSO-KANBAN.md): critério de entrada e saída de cada coluna, limite de trabalho em andamento, anatomia do cartão e o procedimento de sincronização em cinco passos. Esse documento existe porque L-07 e L-09 nasceram, as duas, de sincronização mal definida.

Regra de triagem adotada: a coluna **Não Classificado permanece vazia**. Um cartão ali significa trabalho registrado sem critério, e a ação é triá-lo, não executá-lo. O limite de **Em Andamento é um cartão**.

## 7. Fila de execução (espelho do quadro)

**Espelho da fila do quadro**, que é a fonte (PROCESSO-KANBAN 2.0). Cada item só começa quando o anterior está verde. O número em colchetes é o do cartão; subníveis (19.x) dependem do pai ou realizam o mesmo item.

1. ~~**[19] Endpoints HTTP e tratamento de erro** (L-03, L-07)~~ concluído em 2026-10-02
   - ~~**[19.1] Caminho de persistência do estorno** (L-08)~~ concluído em 2026-10-02
   - ~~**[19.2] Teste via Insomnia**~~ concluído em 2026-10-02: coleção versionada com 43 testes, executada pelo `inso`
   - ~~**[19.3] Testes de contrato da API**~~ concluído em 2026-10-02: OpenAPI gerado e comparado por instantâneo
   - ~~**[19.5] Resposta da repetição devolvida do `response_body`**~~ concluído em 2026-10-02, opção (b) por decisão do usuário
   - ~~**[19.4] Repetição idempotente recusada quando o estado da conta mudou** (L-10)~~ concluído em 2026-10-02, antecipado ao 23 por decisão do usuário
2. ~~**[16] Repositório público no GitHub** (L-01)~~ concluído em 2026-10-02, antecipado ao item 1
3. ~~**[20] Diagramas em Mermaid no repositório** (L-02)~~ concluído em 2026-10-02
   - ~~**[20.1] Diagrama de entidade e relacionamento do esquema**~~ concluído em 2026-10-02
4. ~~**[21] Correções documentais** (L-05, L-11)~~ concluído em 2026-10-02
   - ~~**[21.2] Afirmações desatualizadas do `CLAUDE.md`**~~ concluído em 2026-10-02
   - ~~**[21.1] Nomes dos eventos da EF §9 alinhados ao código**~~ concluído em 2026-10-02 (também ENF R-05 e R-06)
5. ~~**[22] Experimento de detecção do teste de concorrência** (L-04)~~ concluído em 2026-10-02
6. ~~**[23] README final**~~ concluído em 2026-10-02
7. ~~**[24] Despachante de outbox**~~ concluído em 2026-10-02
   - ~~**[24.1] Chave estrangeira na outbox** (L-12)~~ concluído em 2026-10-02
   - ~~**[24.2] Contrato do payload dos eventos**~~ concluído em 2026-10-02
8. ~~**[25] Painel de evidência** (ADR-0011)~~ concluído em 2026-10-02; o critério de corte não se aplicou

Previstos por ADR, que entram com a fila ativa vazia:

9. ~~**[26] Testes de arquitetura com NetArchTest**~~ concluído em 2026-10-02
10. ~~**[27] Migrações com DbUp** (RNF-038)~~ concluído em 2026-10-02, em passo separado com o papel de migração (opção b, por decisão do usuário)
11. ~~**[28] AnalysisMode para recommended** (L-06)~~ concluído em 2026-10-03

Trazidos do Backlog por decisão do usuário, com critério escrito ao entrar:

12. ~~**[31] CI no GitHub Actions** (ADR-0010)~~ concluído em 2026-10-04
   - ~~**[31.1] Auditoria de dependências vulneráveis no CI** (RNF-026)~~ concluído em 2026-10-04
   - ~~**[31.2] Cobertura do domínio abaixo da RNF-036** (L-13)~~ concluído em 2026-10-04, opção (a)

Bloqueados, encerrados como decisão registrada por decisão do usuário:

13. ~~**[29] Responder às questões de negócio em aberto**~~ encerrado em 2026-10-04 como decisão registrada; conferência achou e corrigiu a L-14
14. ~~**[30] Testes de carga e RNF de desempenho**~~ encerrado em 2026-10-04 como decisão registrada; conferência achou e corrigiu a L-15
   - ~~**[30.1] Teste do limite de replay do snapshot** (RNF-006)~~ concluído em 2026-10-04

Do Backlog, por decisão do usuário:

15. ~~**[34] Atualizar as dependências NuGet**~~ concluído em 2026-10-04, dentro do xunit v2
   - ~~**[34.1] Migrar os testes para xunit v3**~~ concluído em 2026-10-05 por antecipação: xunit v3 na Microsoft Testing Platform, que sai do VSTest

16. ~~**[36] Teste de mutação com Stryker**~~ concluído em 2026-10-04: 57,49% para 89,81%, limite de 85% no CI

17. ~~**[33] Log estruturado com correlação e mascaramento** (RNF-031, L-16)~~ concluído em 2026-10-04; ADR-0012
   - ~~**[33.1] Rastreamento ponta a ponta** (RNF-030)~~ concluído em 2026-10-05
   - ~~**[33.2] Métricas de negócio** (RNF-032)~~ concluído em 2026-10-05

Do Backlog, por antecipação decidida pelo usuário (gatilho não ocorrido):

18. ~~**[32] Fechamento diário para a consulta histórica** (ADR-0007)~~ concluído em 2026-10-05

19. ~~**[37] Particionar o ledger por tempo** (R-04)~~ concluído em 2026-10-05 por antecipação, opção b2: chaves de unicidade numa tabela não particionada (ADR-0013)

Do Backlog, por decisão do usuário:

20. ~~**[38] Transferência entre contas**~~ concluído em 2026-10-05: transação local, contas bloqueadas em ordem crescente de identificador, pernas amarradas pelo banco (ADR-0014)

A Fazer e Backlog estão vazios. Próximos por decisão do usuário.

---

## 8. Como verificar o estado atual

```bash
cd pacioli-bank-ledger
dotnet test                   # esperado: 237 passando, 0 falhando, sem avisos
docker compose up --build     # migrador aplica o que falta e termina; API em http://localhost:8080
curl http://localhost:8080/health/ready
curl -X POST http://localhost:8080/api/v1/accounts/11111111-1111-1111-1111-111111111111/credits \
     -H 'Idempotency-Key: k1' -H 'Content-Type: application/json' \
     -d '{"amount":150.00,"currency":"BRL","occurredAt":"2026-10-02T10:00:00Z"}'
curl http://localhost:8080/api/v1/accounts/11111111-1111-1111-1111-111111111111/balance
```

Demais requisições em `requests.http`, na raiz.

Os testes de integração exigem Docker em execução.

---

## 9. Versões congeladas

Congeladas porque estão verificadas com build limpo e suíte verde, não porque são as mais recentes. Atualização de dependências é item próprio da fila, com verificação própria.

Revisada no card 34.1 (2026-10-05): saem o VSTest (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`), o `xunit` 2.x e os dois coletores do coverlet; entram `xunit.v3` e `coverlet.MTP`.

Revisada no card 34 (2026-10-04): `dotnet list package --outdated` mostrou defasadas só as ferramentas de teste, atualizadas dentro do xunit v2; os pacotes de produção já estavam na última versão. A tabela também passou a listar todos os pacotes da solução: quatro tinham entrado sem registro aqui.

| Pacote | Versão | Onde |
|---|---|---|
| Npgsql | 10.0.3 | Persistence, Events, Migrations, testes |
| Dapper | 2.1.89 | Persistence, Events, testes |
| dbup-postgresql | 7.0.1 (traz dbup-core 6.1.1; card 27) | Migrations |
| Microsoft.AspNetCore.OpenApi | 10.0.12 (igual ao runtime; card 19.3) | Api |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 | Contract.Tests, Integration.Tests |
| Serilog.AspNetCore | 10.0.0 (traz Serilog 4.3.0, Formatting.Compact 3.0.0, Sinks.Console 6.1.1; card 33) | Api |
| OpenTelemetry.Extensions.Hosting, OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.19.1 (card 33.1) | Api |
| OpenTelemetry.Instrumentation.AspNetCore | 1.19.0 (card 33.1) | Api |
| Npgsql.OpenTelemetry | 10.0.3 (igual ao Npgsql; card 33.1) | Api |
| OpenTelemetry.Exporter.InMemory | 1.19.1 (card 33.1) | Integration.Tests |
| Imagem `mcr.microsoft.com/dotnet/aspire-dashboard` | 13.6.0 (card 33.1) | `docker compose`, só local |
| NetArchTest.Rules | 1.3.2 (card 26) | Architecture.Tests |
| Testcontainers.PostgreSql | 4.15.0 | Integration.Tests |
| xunit.v3 | 4.0.1 (card 34.1; na Microsoft Testing Platform, executor declarado no `global.json`) | testes |
| coverlet.MTP | 10.1.0 (card 34.1; limite de cobertura, card 31.2) | testes |
| dotnet-stryker | 5.0.0 (ferramenta local, `dotnet-tools.json`; card 36) | teste de mutação |

---

## 10. Bloco de retomada

Para colar no início de uma sessão nova, junto deste arquivo e do enunciado do desafio.

```text
Contexto: estou executando um desafio técnico de Arquiteto de Software.
Atue como Arquiteto de Soluções e Software, incluindo o papel de dev C#.

O projeto chama-se PacioliBank Ledger: um livro-razão de contas correntes
em C# que registra movimentações financeiras e responde à posição
consolidada de um cliente em qualquer instante, com consistência forte
por conta.

Leia, nesta ordem, antes de qualquer sugestão:
  o quadro no TickTick, projeto PacioliBank   fonte das atividades e da ordem
  docs/ESTADO.md                 estado atual e lacunas; a §7 espelha o quadro
  docs/adr/README.md             índice das 13 decisões arquiteturais
  docs/specs/EF-especificacao-funcional.md    domínio, regras e contratos
  docs/convencoes-de-nomenclatura.md

Regras de trabalho desta sessão:

1. Uma ação por vez. As demais ficam em fila registrada.
2. Toda entrega termina com um comando único que eu executo e cujo
   resultado eu devolvo. Ciclo curto de verificação.
3. Nenhuma decisão arquitetural nova sem ADR correspondente, com
   alternativas rejeitadas e gatilho de revisão.
4. Não preencher lacuna de negócio com suposição silenciosa. O que falta
   vira questão aberta com conduta provisória declarada.
5. Marcar com [NVI] tudo que não foi verificado diretamente.
6. Separar sempre o que está implementado do que está apenas
   especificado. Nunca apresentar um como o outro.
7. O build roda com TreatWarningsAsErrors. Aviso é erro.
8. Invariante financeira é garantida por constraint e privilégio no
   banco, não por disciplina de código.

Comece lendo o quadro e comparando-o com docs/KANBAN.md e a §7 do
ESTADO.md; em divergência, vale o quadro. Depois me diga qual é o próximo
cartão e por quê. Nenhum trabalho sem cartão. Não avance sem minha
confirmação.
```

---

## 11. Histórico

| Data | Alteração |
|---|---|
| 2026-10-02 | Criação. Domínio e persistência completos, 57 testes verdes, 11 ADRs, 3 diagramas no Lucid |
| 2026-10-02 | Quadro Kanban criado no TickTick com 37 cartões; `KANBAN.md` acrescentado; lacuna L-07 registrada (código não compilado) |
| 2026-10-02 | Build e suíte verificados (0 avisos, 57 verdes). L-07 encerrada: os tipos descritos não existiam no disco. L-08 registrada (estorno sem caminho de persistência). Contagem de exceções corrigida para 12 |
| 2026-10-02 | Repositório público criado e publicado: L-01 encerrada, item 2 da fila concluído antes do item 1. Cartão movido para Concluído em `KANBAN.md` |
| 2026-10-02 | L-09 registrada e encerrada: o README público subdeclarava domínio, persistência, idempotência e testes de integração como pendentes. README corrigido; a regra 9 passa a cobri-lo |
| 2026-10-02 | Correções da L-09 commitadas e publicadas (3 commits, `origin/main` em `c8d90ff`). `CLAUDE.md` retirado do `.gitignore` e versionado; decisão registrada na §3 |
| 2026-10-02 | Quadro sincronizado com a numeração aplicada no TickTick (41 cartões, 01 a 38 com subníveis 19.1 a 19.3). `KANBAN.md` regenerado por leitura direta do quadro; §6-A e §7 passam a citar o número do cartão. Push dos commits 12 e 13 confirmado (`origin/main` em `99924c4`) |
| 2026-10-02 | Card 19.1 concluído: `ReverseAsync` no store, sob o mesmo bloqueio por conta, com 9 testes de integração (66 verdes). L-08 encerrada. L-10 registrada e verificada por execução: reenvio idempotente recusado quando o saldo mudou |
| 2026-10-02 | Card 19 concluído: porta de entrada `ILedgerService`, os 5 endpoints da EF §8.3, `ProblemDetails` com o catálogo da §8.6, correlação, prontidão ligada ao PostgreSQL. L-03 e L-07 encerradas; L-11 registrada (6 decisões de contrato provisórias). O teste via curl achou 2 defeitos que a suíte não via (corpo da repetição diferente por precisão de instante; estorno duplicado saindo como saldo insuficiente), corrigidos com teste que reprova sem a correção. 94 verdes. `.dockerignore` criado: sem ele a imagem não compilava |
| 2026-10-02 | Card 20 concluído: modelo C4 em Mermaid em `docs/diagrams/` (C1, C2, C3, sequência do débito), com estado por elemento. L-02 encerrada; requisito "documentação no repositório" passa a atendido. C3 e sequência redesenhados a partir do código, com correspondência explícita ao Lucid |
| 2026-10-02 | Card 21 concluído: as 4 correções da L-05 aplicadas com nota de revisão; 3 divergências novas achadas ao conferir os documentos contra o código (ADR-0010, ADR-0001 e convenções, EF §8.4) e tratadas; L-11 incorporada à EF 1.1 (§8.7, QA-008). EF e BDD passam à versão 1.1. L-05 e L-11 encerradas |
| 2026-10-02 | Card 22 concluído: experimento sem `FOR NO KEY UPDATE`. O teste reprova, mas por perda de disponibilidade (24% a 78% de `503`), não por saldo negativo: a constraint de sequência preserva a invariante sem o bloqueio. Resultado no ADR-0005. L-04 encerrada |
| 2026-10-02 | Card 19.4 criado e concluído: repetição reconhecida sob o bloqueio da conta, antes do agregado. ADR-0006 revisado com alternativas rejeitadas. 3 testes novos (97 verdes), cenário reproduzido via curl. L-10 encerrada; nenhuma lacuna ALTA aberta |
| 2026-10-02 | Card 23 concluído: README final com início em cinco minutos, uso da API com exemplos curl, erros, testes, decisões atualizadas (ADR-0005 medido, ADR-0006 revisado) e "o que seria feito com mais tempo". Verificado seguindo o README literalmente num clone limpo; o caminho "fora do Docker", que falharia como estava escrito, corrigido e verificado. Todos os requisitos obrigatórios atendidos |
| 2026-10-02 | Sincronização do quadro: card 20.1 (ERD do esquema), criado no Cowork, incluído na §7 e no `KANBAN.md`; cards 24 e 25 movidos de Backlog para A Fazer, coerentes com a §7. **Registro de falha de processo:** o commit `38eeb4f` (L-10) incluiu, por `git add -A`, a emenda do usuário ao `PROCESSO-KANBAN.md` (versão 1.2, regra de subnível), feita no Cowork. O conteúdo é o do usuário; a mensagem do commit não o descreve. Histórico publicado não é reescrito; a correção de causa é adicionar só caminhos explícitos daqui em diante |
| 2026-10-02 | Card 20.1 concluído: ERD do esquema em `docs/diagrams/ERD-esquema-ledger.md`, 5 tabelas e 40 colunas idênticas ao catálogo do PostgreSQL (comparação automática), 5 PK, 5 FK, 3 UNIQUE, 5 CHECK, índices e privilégios, com as 4 notas pedidas. Layout corrigido para a outbox não parecer relacionada. L-12 registrada: a justificativa da ausência de FK na outbox, atribuída ao ADR-0008 pelo cartão, não existe no ADR |
| 2026-10-02 | Card 24 concluído: módulo `PacioliBank.Events` com o despachante de outbox (`SKIP LOCKED`, recuo exponencial, limite de tentativas com alerta), executado no processo da API e publicando em log. Cartão recebeu critério de conclusão antes de começar. 5 testes de integração (102 verdes); o de paralelismo reprova sem o `SKIP LOCKED`. Verificado no Docker: crédito via curl publicado em menos de 3 s. Card 24.1 criado para a L-12 |
| 2026-10-02 | Card 24.1 concluído: chave estrangeira `fk_outbox_entry (account_id, sequence)` na outbox, por decisão do usuário, registrada no ADR-0008 com alternativas rejeitadas. Teste novo reprovou antes e passa depois; 103 verdes. L-12 encerrada. Cards 18.1 (inverter a política do quadro) e 21.1 (nomes dos eventos da EF §9) criados |
| 2026-10-02 | Card 18.1 concluído: política do quadro invertida (PROCESSO-KANBAN 2.0). O quadro no TickTick é a fonte de toda atividade e da ordem; §7 e `KANBAN.md` passam a espelhos. Regra nova: nenhum trabalho sem cartão |
| 2026-10-02 | Card 21.1 concluído: EF §9 (1.2), ENF (1.1) e convenções §8 alinhados ao código: `pacioli.ledger.*.v1` e `message_id`. Escopo ampliado com motivo: ENF R-06 descrevia o bloqueio consultivo, opção rejeitada no ADR-0005. Lendo eventos reais, achados defeitos no payload; viraram o card 24.2, declarados na EF |
| 2026-10-02 | Card 24.2 concluído: payload dos eventos com tipo próprio (`LedgerEntryEvent`), valores como string, instantes em `Z`, sentido como texto e `reversalOf` no estorno; especificado na EF §9 (1.3), mantendo `v1` porque nenhum consumidor recebeu eventos. 2 testes leem o payload gravado e reprovaram antes. 105 verdes; verificado no Docker. Card 19.5 criado: o ADR-0006 diz devolver `response_body`, o código reconstrói do ledger |
| 2026-10-02 | Card 19.5 concluído, opção (b): a repetição devolve o `response_body` gravado, no formato do contrato; coluna de `jsonb` para `json` para preservar o texto exato. ADR-0006 revisado. 2 testes novos (107 verdes); no Docker, primeira resposta, reenvio e coluna com o mesmo SHA-256 |
| 2026-10-02 | Card 21.2 concluído: o `CLAUDE.md` descrevia a idempotência como "nunca por consulta prévia" (superado pelas revisões 19.4 e 19.5 do ADR-0006) e a EF na versão 1.1 (está na 1.3) |
| 2026-10-02 | Card 25 concluído: painel de evidência em `wwwroot`, quatro demonstrações (concorrência, linha do tempo, reenvio, origem do cálculo). Contas `3333…`, `4444…`, `5555…` no script de massa; `entriesReplayed` na posição (EF 1.4). Verificado em Chrome headless, repetidamente. Com isso, a fila ativa 19 a 25 está concluída |
| 2026-10-02 | Card 19.2 concluído: o teste manual no Insomnia virou coleção versionada com testes embutidos (15 requisições, 43 testes), executada pelo `inso` 13.3.0 contra o `docker compose`: verde em três execuções seguidas; apontada para conta inexistente, reprova com código de saída 1 |
| 2026-10-02 | Card 19.3 concluído: OpenAPI gerado do código em `/openapi/v1.json` (pacote 10.0.12, igual ao runtime), com corpos e problemas declarados por endpoint; três imprecisões corrigidas antes de aprovar (título, inteiro declarado como "inteiro ou string", campo anulável declarado obrigatório). Projeto `PacioliBank.Contract.Tests` com comparação por instantâneo; reprova quando o contrato muda (medido). 109 verdes; coleção do Insomnia segue 43/43 |
| 2026-10-02 | Card 26 concluído: `PacioliBank.Architecture.Tests` com 5 regras de dependência (NetArchTest 1.3.2, verificado contra assemblies .NET 10). A regra dos endpoints achou uma violação real: o tradutor de erros HTTP conhecia o driver do banco. Corrigido na causa: o adaptador de dados traduz falha transitória do driver para `LedgerUnavailableException` (2 testes novos, que reprovaram antes). 116 verdes; `503` verificado no Docker com o banco parado |
| 2026-10-02 | Card 27 concluído: migrações DbUp em projeto próprio (`PacioliBank.Migrations`), executadas num passo separado do `docker compose` com o papel de migração; a API continua só com `SELECT, INSERT` (ADR-0009). Critério do card revisado (opção b, decisão do usuário): "aplicadas na subida da aplicação" exigiria dar à API a credencial de migração. Revisão do ADR-0002 com 4 alternativas rejeitadas. `db/init/` substituído por `db/migrations/` e `db/seed/`; C2 ganha o migrador. 7 testes de integração novos (reprovaram com o esboço) e 1 regra de arquitetura; poder de detecção medido em duas mutações. No Docker: do zero, migrador sai com 0 e API saudável; recriado sobre o mesmo volume, nada reaplicado e dado preservado; painel e Insomnia (43/43) verdes. Transição: um último `down -v` local. 124 verdes |
| 2026-10-03 | Card 28 concluído: analisadores do .NET elevados a `Recommended`. O build reprovou com 4 regras (CA1716, CA1862, CA1711, CA1859), todas corrigidas no código, nenhuma suprimida; teste novo fixou o comportamento de `Currency.TryFromCode` antes da troca. L-06 encerrada; nenhuma lacuna aberta. Imagens Docker compilam no modo novo; filtro de período do extrato conferido contra a API. Contagens do ADR-0010 corrigidas: tinham ficado desatualizadas no card 27. 131 verdes |
| 2026-10-04 | Card 31 (CI no GitHub Actions) movido do Backlog para A Fazer por decisão do usuário, com critério de conclusão escrito ao entrar (cinco camadas de teste, cobertura informativa, varredura de segredos, poder de detecção medido) |
| 2026-10-04 | Card 31 concluído: CI no GitHub Actions com três jobs (build e testes, coleção do Insomnia, `gitleaks`), ações fixadas por SHA, verde em `main` em cerca de um minuto; vermelho num ramo com aviso plantado (prova apagada depois). Descobertos e viraram cards: 31.1 (auditoria de dependências) e 31.2, com a lacuna L-13: a ENF dava a RNF-036 por realizada, e a cobertura medida do domínio é 74,9%. ENF corrigida para 1.2 |
| 2026-10-04 | Card 31.1 concluído: auditoria de dependências no restore, explícita (`NuGetAudit`, modo `all`, nível `high`). Medido localmente: nível `high` reprova o alerta alto e `critical` deixa passar; modo `all` pega a vulnerabilidade transitiva e `direct` deixa passar. No CI, um ramo com dependência transitiva vulnerável ficou vermelho (apagado depois). Nenhum pacote vulnerável hoje. RNF-026 realizada |
| 2026-10-04 | Card 31.2 concluído, opção (a): cobertura de linha do domínio, medida pelos testes de domínio em Release, de 56,8% para 86,4%, com limite de 85% no CI. 40 construtores de exceção sem uso removidos e 19 testes de domínio escritos. Sem os testes de contrato, 72,8%: o passo reprova (medido no CI). L-13 encerrada; nenhuma lacuna aberta. ADR-0010 revisado com o método e 3 alternativas rejeitadas; ENF 1.3. 150 verdes |
| 2026-10-04 | Card 29 encerrado como decisão registrada, por decisão do usuário: as questões de negócio seguem sem interlocutor. Antes de encerrar, as condutas provisórias foram conferidas contra o código: a EF prometia política substituível e configuração que não existem, QA-007 dava como conduta um JWT não implementado e QA-003 estava decidida num documento e aberta em dois. Lacuna L-14 aberta e encerrada; EF 1.5 e BDD 1.2 |
| 2026-10-04 | Card 30 encerrado como decisão registrada, por decisão do usuário: sem ambiente de carga nem volume real. Na conferência, a ENF dava RNF-003 e RNF-006 por realizadas sem ressalva, e o README dizia o snapshot "com testes": vale só na posição corrente, por construção, sem teste automatizado; a histórica soma todo o histórico. Lacuna L-15 aberta e encerrada; ENF 1.4; o teste virou o card 30.1 |
| 2026-10-04 | Card 30.1 concluído: `SnapshotTests`, três testes de integração do snapshot (gravação na centésima escrita, posição corrente partindo dele com no máximo 99 somados, posição histórica sem snapshot), sempre conferidos contra a soma do ledger. Três mutações detectadas. A mutação 2 mostrou que snapshot errado contaminaria o `balance_after` dos lançamentos seguintes: registrado no ADR-0007. ENF 1.5. 153 verdes |
| 2026-10-04 | Card 34 concluído: ferramentas de teste atualizadas dentro do xunit v2 (xunit 2.9.3, runner 4.0.0, Test.Sdk 18.10.1, coverlet 10.1.0); produção já estava na última versão. Mesmos 153 testes, build sem avisos, auditoria sem alerta, cobertura de linha do domínio igual (86,4%) e limite funcionando. xunit v3 separado no card 34.1, no Backlog com gatilho. §9 completada: quatro pacotes estavam sem registro |
| 2026-10-04 | Card 36 concluído: teste de mutação com Stryker.NET 5.0.0 no domínio, 17 s por execução, tarefa própria do CI com limite de 85%. Pontuação de 57,49% para 89,81%, estável: 13 testes novos sobre asserções fracas de verdade (limites da página, período de um instante, cursor que não chegava ao armazenamento, precisão da impressão, dados das rejeições) e teste de valor fixo da impressão do comando, que é gravada. Achado: o construtor estático de `Currency` deixava a pontuação instável; excluído com motivo. ADR-0010 revisado. 166 verdes |
| 2026-10-04 | Card 33 concluído, dividido em 33, 33.1 e 33.2: log em JSON (Serilog) com correlação e mascaramento no formatador, coberto por 9 testes, inclusive um pelo caminho completo com API e PostgreSQL reais; três mutações detectadas. Lacuna L-16 encerrada: a ENF dava RNF-031 e RNF-032 por realizadas sem nada no código. ADR-0012 com 6 alternativas rejeitadas; ENF 1.6. Achado: aviso do Npgsql em texto livre (GSS), desligado nas connection strings locais. No Docker, 100% das linhas da API em JSON e nenhum GUID íntegro fora dos campos preservados. 175 verdes |
| 2026-10-05 | Card 33, correção pós-entrega: o CI reprovou na varredura de segredos. O teste de mascaramento trazia um JWT sintético escrito inteiro, que o gitleaks tomou por credencial. Histórico publicado não se reescreve: a ocorrência do commit `481b566` foi ignorada pela impressão digital, com motivo, e o teste passou a montar o token em tempo de execução. Falha minha: rodei o gitleaks antes do card 33, não depois |
| 2026-10-05 | Card 33.1 concluído: rastreamento ponta a ponta (RNF-030). Decorador de `ILedgerService` na API abre o span do caso de uso, e não código no núcleo, para não baixar a pontuação de mutação; requisição, caso de uso e PostgreSQL no mesmo traço, sem identificador integro. Filtro do Npgsql: só comando dentro de traço, senão o despachante geraria um traço solto por segundo (achado no painel). Painel Aspire no compose. 2 testes, quatro mutações detectadas. ADR-0012 revisado; ENF 1.7. 177 verdes |
| 2026-10-05 | Card 33.2 concluído: seis métricas de negócio (RNF-032) pelo mesmo decorador do 33.1; código da rejeição do mesmo mapa do problem+json; fila da outbox lida do banco na coleta. 1 teste com sequência conhecida, quatro mutações detectadas; métricas conferidas no painel local. ADR-0012 revisado; ENF 1.8. Com o 33, 33.1 e 33.2, a RNF-030, a RNF-031 e a RNF-032 estão realizadas. 178 verdes |
| 2026-10-05 | Card 35 (avaliar o PostgreSQL 18) removido do quadro por decisão do usuário. Motivo: sem gatilho (o 17 tem suporte até 2029) e o ganho citado, UUID v7 na chave primária, não depende do banco, porque o identificador é gerado pela aplicação, e o .NET 10 já tem `Guid.CreateVersion7()` |
| 2026-10-05 | Card 32 concluído por antecipação (o gatilho, p99 da histórica acima da RNF-002, não foi medido): fechamento diário síncrono, mantido na transação do lançamento, com correção dos dias seguintes a um retroativo; migração 0002 com preenchimento; `computedFrom` = `dailyBalance` (EF 1.6). Revisão do ADR-0007 com 3 alternativas rejeitadas, inclusive a recomputação assíncrona que o próprio ADR previa. 5 testes, cinco mutações detectadas. A 0002 foi aplicada ao volume local em uso, sem recriar o banco: nenhuma divergência. ENF 1.9. 183 verdes |
| 2026-10-05 | Card 37 concluído por antecipação (QA-004 sem resposta), opção b2 decidida pelo usuário: ledger particionado por mês de `recorded_at`; as quatro restrições de unicidade, verificadas como recusadas pelo PostgreSQL em tabela particionada, foram para `entry_keys`, não particionada, com os mesmos nomes; regra 6 preservada. Migração 0003 com cópia dos dados e repontamento das chaves estrangeiras; função de partição chamada pelo migrador. 9 testes, mutações detectadas; duas mutações (sequência e partição por fato) são recusadas pela própria estrutura. No volume local: 260 lançamentos e a mesma soma antes e depois. ADR-0013 com 5 alternativas rejeitadas. 192 verdes |
| 2026-10-05 | Card 34.1 concluído por antecipação (o xunit v2 segue suportado): xunit v3 4.0.1 nos quatro projetos. O escopo inicial, manter o VSTest, se mostrou impossível, medido: o `dotnet test` do SDK 10 recusa VSTest com a Microsoft Testing Platform ligada, e com ela desligada o Stryker pontua 0% (não exercita o código mutado). Adotada a Microsoft Testing Platform: `global.json` declara o executor, `coverlet.MTP` no lugar dos dois coletores, Stryker com executor `mtp`, CI reescrito. Mesmos 192 testes. Cobertura do domínio 94,45% com o coletor novo (88,15% com o antigo; não comparáveis) e mutação 89,17%; os dois limites verificados de novo, reprovando. Achado: um teste de métricas e dois de rastreamento liam o exportador em memória com a API viva, intermitente sob o v3; a API passou a ser encerrada antes da leitura. ADR-0010 revisado; ENF 1.11 |
| 2026-10-05 | Card 38 concluído, por decisão do usuário: transferência entre contas como **transação local**, e não a saga da ENF 5.2, cujo motivo (particionamento por conta) não existe. As duas contas bloqueadas em ordem crescente de identificador; serviço de domínio `Transfer` decide as pernas por `Account.Post`; migração 0004 com `transfers` e chaves estrangeiras compostas que conferem conta, sentido, valor, moeda e registro de cada perna; `POST /api/v1/transfers`, código `SAME_ACCOUNT_TRANSFER`. 38 testes novos, reprovando antes da implementação; oito mutações detectadas, entre elas a ordem do sentido (o teste de ordem é determinístico) e o débito confirmado em transação separada (falha provocada no banco). Insomnia com 3 requisições novas, 52/52. A 0004 foi aplicada ao volume local em uso. ADR-0014 com 8 alternativas rejeitadas; revisões do ADR-0001, ADR-0005, ADR-0006 e ADR-0009; EF 1.7 (RF-012, RN-013, QA-009), ENF 1.12, BDD 1.3 (F11), ERD reconferido. Condutas provisórias de negócio aceitas pelo usuário. 230 verdes |
| 2026-10-05 | Card 39, apontado pelo usuário: o cabeçalho dizia "os 6 projetos da solução", e a solução tem 9 (5 de produção, 4 de teste; o .sln lista 11 entradas, das quais 2 são pastas), e datava o build de 2026-10-03, com 34 commits depois. O build foi reverificado do zero (`--no-incremental`): 9 projetos, 0 avisos, 0 erros. Linhas de testes e de verificação manual conferidas: já datadas de hoje, por execução de hoje |
| 2026-10-05 | Registro retroativo no quadro, a pedido do usuário, de quatro entregas feitas sem cartão, contra o PROCESSO-KANBAN 2.0: card 40 (`DECISOES-EM-RESUMO.md` reescrito no formato do registro de decisão, `c3e9d62`), card 41 (execução local do README em quatro passos, com resultado esperado e diagnóstico, `ae209cd`), card 41.1 (passo 3 espera a API subir, em vez de verificar uma vez, `bde5931`) e card 42 (nome do projeto fixado no compose, para duas cópias do repositório não colidirem nos `container_name`, `bde5931`). Verificado hoje só o 42: `docker compose config` devolve o mesmo nome no repositório e numa cópia em outra pasta; os demais levam [NVI] no cartão. As mensagens de `ae209cd` e `755568e`, fora da convenção, ficam como dívida aceita na §3, sem reescrever o histórico |
| 2026-10-05 | Card 43, decisão do usuário: o `CLAUDE.md` fica só local e não volta ao repositório. Reverte a decisão de 2026-10-02 (§3), que tinha sido desfeita de fato em `6d2e2d8` e `755568e` sem registro; a §3 dizia uma coisa e o repositório fazia outra. Motivo não declarado. Consequência aberta como L-17 (BAIXA): documentos citam regras do arquivo, e há um link quebrado em `DECISOES-EM-RESUMO.md`. A mensagem de `6d2e2d8` entra na dívida aceita da §3 |
| 2026-10-05 | Card 43.1 concluído, opção (a) por decisão do usuário: as dez regras invioláveis em `docs/REGRAS.md`, com o texto da última versão versionada do `CLAUDE.md`. Citações repontadas em `PROCESSO-KANBAN.md` (2), `diagrams/README.md`, `DECISOES-EM-RESUMO.md` (que dizia "nove", e são dez) e nesta página. O link quebrado para `../CLAUDE.md` saiu; o README lista o arquivo. L-17 encerrada; nenhuma lacuna aberta |
| 2026-10-06 | Card 44 aberto: a requisição vazia "New Request", que roda antes de tudo e derruba a execução, existe só no workspace importado no aplicativo Insomnia; o arquivo versionado não a contém. Remoção pela interface do aplicativo, pendente com o usuário: o banco local do aplicativo não é editado com ele aberto |
| 2026-10-06 | Card 45 concluído: sub-ambiente `Local` (`env_pacioli_local`) na coleção, com as 4 variáveis; o *Base Environment* fica vazio. Elimina o aviso "No environment is selected" do aplicativo e dá existência ao passo de escolher o ambiente, agora escrito no README. CI e README passam a `--env env_pacioli_local`. `inso` 13.3.0 contra o `docker compose`: 52/52 duas vezes; com o *Base Environment*, reprova com código 1. No aplicativo com o `Local` selecionado: [NVI] |
| 2026-10-06 | Card 46 concluído: §4 registra a execução da coleção no runner do aplicativo Insomnia 13.1.0, feita pelo usuário às 01:59 UTC contra o `docker compose`: 18 requisições, 52 testes verdes. O horário vem do banco local do aplicativo, que registra as variáveis gravadas pelos testes; o resultado verde é o relato do usuário |
| 2026-10-06 | Card 47 concluído, por decisão do usuário: **valor monetário como número JSON** na API e no evento, revertendo a escolha original do ADR-0004 (texto). Entrada só número: texto sai `400 INVALID_REQUEST`, escala acima da moeda `400 INVALID_AMOUNT`. Saída com exatamente as casas da moeda (`Money.ToContractAmount`): `150` entra e `150.00` sai. Evento muda mantendo `v1` (nenhum consumidor). Migração 0005 converte as respostas gravadas para reenvio e as mensagens da outbox; a impressão do comando não muda, e o reenvio de comando anterior à mudança é reconhecido (verificado via curl). OpenAPI com `"format": "decimal"`, porque o gerador declarava `double`. 237 verdes; mutações medidas: sem a normalização da escala, 10 testes reprovam; aceitando texto, 5. Insomnia 54/54 duas vezes; painel verificado em Chrome headless. Revisão do ADR-0004 com 3 alternativas rejeitadas; EF 1.8, BDD 1.4. Risco aceito, registrado no ADR: consumidor JavaScript com o parser padrão lê ponto flutuante. **Registro posterior no quadro:** o TickTick estava desconectado; o usuário autorizou executar antes e criar o cartão 47 na reconexão |
