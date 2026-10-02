# Estado do Projeto

**Documento vivo.** Atualizado a cada entrega. Descreve o que existe, o que falta e o que está decidido, sem otimismo.

**Última atualização:** 2026-10-02 (sexta revisão)
**Build:** verde, 0 avisos, 0 erros, os 5 projetos da solução (`dotnet build`, verificado em 2026-10-02)
**Testes:** 57 passando (39 de domínio, 18 de integração), 0 falhando (`dotnet test`, verificado em 2026-10-02)
**Atenção:** os tipos que a L-07 dava como "escritos e não compilados" **não existem no disco**. Ver §6, L-07.

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
| Testes automatizados | Atendido, 57 passando |
| Código compila sem erros e sem avisos | Atendido, `TreatWarningsAsErrors` ativo |
| README com instruções de execução local | Atendido, precisa da revisão final |
| Toda documentação no próprio repositório | **Parcial**: diagramas estão fora (ver §6, L-02) |
| Repositório público no GitHub | Atendido: https://github.com/eduardojnet/PacioliBank (ver §6, L-01) |

> O enunciado declara que o teste é desconsiderado se os requisitos obrigatórios não forem minimamente atendidos. Um item acima ainda impede a entrega hoje: os diagramas fora do repositório (L-02).

---

## 3. Decisões fechadas

Onze ADRs em formato MADR, em [`docs/adr/`](./adr/). Cada um com alternativas rejeitadas e gatilho de revisão.

| ADR | Decisão |
|---|---|
| 0001 | Monolito modular; Ports and Adapters com DDD tático no interior |
| 0002 | .NET 10 LTS, PostgreSQL 17, Dapper, DbUp |
| 0003 | Ledger append-only como única fonte da verdade; posição é derivada |
| 0004 | `decimal` e `numeric(19,4)`; `Money` com moeda embutida; string na API |
| 0005 | Serialização por conta via `FOR NO KEY UPDATE`; constraint única como rede |
| 0006 | Chave de idempotência obrigatória; detecção pela violação de chave primária |
| 0007 | Snapshot síncrono amortizado a cada 100 lançamentos; sem projetor assíncrono |
| 0008 | Outbox transacional, entrega ao menos uma vez |
| 0009 | Imutabilidade por ausência de privilégio; resposta opaca a terceiros |
| 0010 | Banco real (Testcontainers) para toda invariante de persistência |
| 0011 | Painel de evidência como página estática, escopo condicional |

### Questões de negócio decididas

| ID | Questão | Decisão |
|---|---|---|
| QA-003 | Estorno pode gerar posição negativa? | **Não.** Respeita RN-001, com política isolada para troca futura |
| RN-012 | Manter bitemporalidade (`occurredAt` separado de `recordedAt`)? | **Sim**, mantida no escopo |

### Decisões de processo, fora de ADR

| Decisão | Registro |
|---|---|
| `CLAUDE.md` **versionado** no repositório público (revertida a decisão de mantê-lo no `.gitignore`) | Correta: o arquivo descreve arquitetura, regras invioláveis e armadilhas encontradas. Em um desafio que avalia processo, é evidência, não configuração de ferramenta. O efeito colateral antes registrado ("quem clonar não recebe as regras") deixa de existir |
| `.claude/` permanece ignorado | Contém permissões locais de execução, que são do ambiente e não do projeto |
| Commits agrupados por decisão, não por arquivo | O enunciado avalia como se pensa e prioriza; push único sinaliza ausência de processo |
| Convenção de mensagem: Conventional Commits | Uma mensagem fora do padrão já entrou no histórico ("Atualizando CLAUDE.md no .gitignore"), e o histórico é lido pelo avaliador. Não reescrever: histórico publicado é imutável, pelo mesmo princípio do ledger |

### Questões de negócio ainda abertas

QA-001 (limite ou cheque especial), QA-002 (política de lançamento retroativo), QA-004 (retenção do ledger), QA-005 (multimoeda), QA-006 (volume real), QA-007 (modelo de identidade). Detalhes e conduta provisória na [EF §10](./specs/EF-especificacao-funcional.md).

---

## 4. O que está implementado

### Domínio (`src/PacioliBank.Ledger`)

Zero dependências externas. A ausência de `PackageReference` é a garantia estrutural de que o domínio não alcança infraestrutura.

| Arquivo | Conteúdo |
|---|---|
| `Domain/Money.cs` | Value Object com moeda embutida; construtor privado; escala validada na criação |
| `Domain/Currency.cs` | ISO 4217, catálogo restrito a BRL |
| `Domain/Account.cs` | Agregado raiz; valida invariantes e produz lançamentos |
| `Domain/LedgerEntry.cs` | Fato imutável, construtor interno |
| `Domain/PostingRequest.cs` | Comando de lançamento |
| `Domain/AccountStatus.cs`, `EntryDirection.cs` | Enums alinhados às colunas do banco |
| `Domain/LedgerDomainException.cs` | Exceção raiz mais 12 derivadas, uma por regra violável |
| `Application/ILedgerStore.cs` | Porta de saída, estreita e orientada a caso de uso |
| `Application/PostEntryResult.cs`, `BalanceResult.cs` | Contratos de saída |
| `Application/RequestFingerprint.cs` | Impressão canônica SHA-256 do comando |

**Decisão de modelagem a defender:** o agregado não carrega os lançamentos da conta. É reidratado dentro da transação, sob bloqueio, com a posição corrente já calculada. Carregar o histórico para validar um débito reintroduziria a degradação do sistema legado.

### Persistência (`src/PacioliBank.Ledger.Persistence`)

| Arquivo | Conteúdo |
|---|---|
| `PostgresLedgerStore.cs` | Transação, bloqueio, idempotência, outbox, snapshot, nova tentativa |
| `LedgerSql.cs` | Todo o SQL reunido, comentado por decisão |

**A ordem dos passos dentro da transação é a arquitetura**, não detalhe de implementação: bloqueia a linha da conta, depois lê a posição, o agregado decide, e lançamento, sequência, idempotência e outbox são gravados juntos.

### Banco (`db/init/`)

Esquema `ledger` com 5 tabelas e 3 papéis. Cada constraint carrega uma regra: `uq_entries_sequence` (RN-006), `uq_entries_idempotency` (RN-005), `uq_entries_reversal` (RN-004), `CHECK (amount > 0)` (RN-002).

O papel `pacioli_runtime` recebe `SELECT, INSERT` no ledger e nada mais. Alterar um lançamento gravado é impossível para a aplicação, qualquer que seja o código.

### Testes

| Projeto | Quantidade | Escopo |
|---|---|---|
| `PacioliBank.Domain.Tests` | 39 | Invariantes puras, sem I/O |
| `PacioliBank.Integration.Tests` | 18 | PostgreSQL real via Testcontainers, incluindo concorrência |

Os testes de concorrência usam barreira de sincronização para liberar as tarefas no mesmo instante. Disparar em laço serializa por acidente de escalonamento e o teste perde o propósito.

### Especificações e diagramas

[BDD](./specs/BDD-comportamento.md) com 10 funcionalidades e ~55 cenários Gherkin, [EF](./specs/EF-especificacao-funcional.md) e [ENF](./specs/ENF-especificacao-nao-funcional.md), todas com rastreabilidade cruzada por IDs.

Três documentos no Lucid: C4 detalhado (3 páginas), diagrama de sequência (nível 4), C4 consolidado (C1 a C4 em uma página). **Fora do repositório** (ver L-02).

---

## 5. O que NÃO está implementado

Declarar isto é parte da entrega. Apresentar requisito especificado como implementado seria, em contrato real, informação incorreta prestada ao cliente.

| Item | Situação |
|---|---|
| Endpoints HTTP de negócio | A API tem apenas `/health/live` e `/health/ready` |
| Injeção de dependência na API | `NpgsqlDataSource` e `ILedgerStore` não registrados; connection string não lida |
| Mapeamento de exceções para `ProblemDetails` | Não existe |
| Porta de entrada (lado dirigente do hexágono) | Não existe |
| Despachante de outbox | Tabela e gravação existem; o publicador não |
| Migrações com DbUp | Esquema aplicado pelo entrypoint do PostgreSQL, que só roda na primeira criação do volume |
| Testes de arquitetura (NetArchTest) | Previstos no ADR-0010, não escritos |
| Testes de contrato (OpenAPI) | Previstos, não escritos |
| Testes de carga | Especificados na ENF §11, fora do escopo do desafio |
| Painel de evidência | Condicional, ADR-0011 |

---

## 6. Lacunas conhecidas, por severidade

### L-01: Repositório público no GitHub (ENCERRADA em 2026-10-02)

Requisito obrigatório explícito. Atendido: https://github.com/eduardojnet/PacioliBank, público, branch `main`.

O histórico tem 7 commits agrupados por área (base, banco, domínio, persistência, API, decisões, estado). Foram criados no mesmo dia, ao versionar o trabalho já existente, e não reproduzem a cronologia original; nenhuma data foi alterada. A partir daqui cada entrega vira commit próprio. Que cada um dos 7 commits compile isoladamente não foi verificado [NVI].

### L-02: Diagramas fora do repositório (BLOQUEANTE PARCIAL)

O enunciado exige toda a documentação no próprio repositório. Hoje quem clona o repo não vê diagrama algum.

**Correção recomendada:** recriar em Mermaid sob `docs/diagrams/`. O GitHub renderiza nativamente, o arquivo é versionável e diffável, e não depende de conta no Lucid. Os documentos do Lucid continuam úteis para apresentação.

**Segunda parte:** os diagramas mostram componentes que ainda não existem (painel, despachante, endpoints, autorização). Legítimo como arquitetura-alvo, desonesto como estado atual. Cada diagrama precisa de nota distinguindo o implementado do especificado.

### L-03: Hexágono implementado só no lado dirigido (ALTA)

`ILedgerStore` é porta de saída e está bem feita. **Não existe porta de entrada.** Hoje quem chama o store é o teste de integração, diretamente.

Quando os endpoints forem escritos, a recomendação é criar `IPostEntryHandler` como porta de entrada, deixando o endpoint como adaptador HTTP fino. O cálculo do fingerprint é regra do ADR-0006 e não pertence ao adaptador HTTP.

Resposta honesta hoje: "Ports and Adapters com desvio consciente, implementado no lado dirigido; o lado dirigente ainda não foi construído".

### L-04: Poder de detecção do teste de concorrência não verificado (ALTA)

O ADR-0010 e o próprio `ConcurrencyTests` afirmam que o teste deve falhar contra implementação sem bloqueio. **Isso nunca foi medido.**

Experimento pendente: remover `FOR NO KEY UPDATE`, rodar só a suíte de concorrência, observar. Duas hipóteses legítimas:

- **Falha:** o bloqueio sustenta RN-001 e o teste tem poder de detecção
- **Passa:** a constraint mais a nova tentativa funcionam como controle otimista, e a defesa em profundidade do ADR-0005 é real com duas camadas independentes. Nesse caso o critério escrito está errado e precisa ser corrigido

Qualquer resultado vai para o ADR-0005 como validação empírica.

### L-05: Correções pendentes nos documentos (MÉDIA)

Divergências já identificadas e registradas, ainda não aplicadas nos documentos de origem:

| Documento | Correção | Registrada em |
|---|---|---|
| EF §8.6 e BDD F09 | `403` para conta de terceiro revela existência; deve ser `404` para cliente final | ADR-0009 |
| ADR-0001 | Módulo `Integration` renomeado para `Events`, por colisão com os testes de integração | convenções §3 |
| ADR-0010 | Projetos de teste com prefixo `Ledger.`, substituído por `PacioliBank.` | convenções §3 |
| ADR-0009 | Papéis `app_*` nos exemplos, substituídos por `pacioli_*` | convenções §5 |

Registrar em vez de corrigir em silêncio preserva a rastreabilidade de por que mudou. Mas as correções precisam ser aplicadas antes da entrega.

### L-07: Código dado como escrito, mas ausente do disco (ENCERRADA como verificação; o trabalho migra para o item 1 da fila)

A revisão anterior registrava dois arquivos como escritos e não compilados. A verificação de 2026-10-02 mostrou que **nenhum dos dois existe no repositório**:

- `src/PacioliBank.Ledger/Application/Commands.cs` não existe. Nenhum dos tipos `PostingCommand`, `ReversalCommand`, `StatementQuery`, `StatementEntry`, `StatementPage` aparece em código algum
- `src/PacioliBank.Ledger/Domain/LedgerDomainException.cs` existe, mas **sem** `EntryNotFoundException`, `PageSizeExceededException` e `InvalidPointInTimeException`. O arquivo tem a exceção raiz mais 12 derivadas, nenhuma delas as três citadas

O build verde e os 57 testes verdes valem para o código que está no disco, não para o que a revisão anterior descrevia. A descrição antiga apresentava como escrito algo que não estava escrito, exatamente o tipo de divergência que a §5 deste documento existe para impedir: apresentar o especificado como implementado. Fica registrada aqui em vez de apagada.

Os tipos continuam necessários e passam a ser escritos dentro do item 1 da fila (§7).

**Primeira ação de qualquer sessão nova continua sendo `dotnet test`**, e conferir no disco o que este documento afirma existir.

### L-08: Estorno sem caminho de persistência (ALTA)

O domínio tem `Account.Reverse(original, ...)`, que valida titularidade e proíbe estorno de estorno (RN-004). **A persistência não o usa.** O `PostgresLedgerStore` só expõe `PostAsync`, que chama `Account.Post`. Consequências verificadas na leitura do código, sem execução:

- Não existe leitura do lançamento original dentro da transação, então `EntryNotFromThisAccountException` e `CannotReverseReversalException` nunca são lançadas fora dos testes de domínio
- Um `PostingRequest` com `ReversalOf` preenchido seria gravado sem essas validações. A FK `reversal_of` garante só que o original existe, não que pertence à conta, nem que não é estorno, nem que sentido e valor são os opostos
- A violação de `uq_entries_reversal` (estorno duplicado) não é tratada: sai como `PostgresException` crua. Pela EF §8.6 deveria ser `ENTRY_ALREADY_REVERSED` (409)

Nenhum teste de integração cobre estorno. Corrigir dentro do item 1 da fila, com teste contra PostgreSQL real (ADR-0010).

### L-09: README público subdeclarava o estado da implementação (ENCERRADA em 2026-10-02)

Detectada ao verificar o repositório recém-publicado. A tabela "Estado atual da implementação" do README declarava como **pendentes** o agregado Conta, a idempotência, o controle de concorrência e os testes de integração, todos implementados e verdes. A árvore de estrutura omitia `PacioliBank.Ledger.Persistence` e `PacioliBank.Integration.Tests`.

**Causa:** o README foi escrito antes dessas entregas e não foi revisado quando elas entraram. A regra 9 das invioláveis cobria o `ESTADO.md`, não o README.

**Por que importa mais do que parece:** a §5 deste documento e a regra 5 existem contra **sobre**declarar. Aqui o erro foi o inverso, e no desafio o dano é maior: o avaliador lê "pendente" na primeira tela e não procura o código. Entrega existente avaliada como ausente.

**Corrigido:** tabela de estado reescrita com nove linhas separando implementado de pendente, árvore de estrutura atualizada para os cinco projetos reais, comando de teste declarando 57 testes e o pré-requisito de Docker, e a promessa de DbUp "na próxima entrega" trocada por item da fila.

**Controle adotado:** o README entra na mesma verificação da regra 9. Nenhuma entrega fecha com README divergente da §4 deste documento.

### L-06: `AnalysisMode` ainda em `Default` (BAIXA)

O ADR-0002 previu `latest-recommended` após o primeiro build limpo. O build está limpo há três ciclos. Elevar é um commit próprio e pequeno.

---

## 6-A. Gestão de projeto

O quadro Kanban vive no TickTick, projeto **PacioliBank**, com 41 cartões distribuídos em 7 colunas, numerados conforme a convenção do `PROCESSO-KANBAN.md` §4. `docs/KANBAN.md` é o espelho em texto, versionado no repositório.

**Limitação a conhecer:** o quadro é mantido no ambiente de gestão do projeto, separado do terminal de desenvolvimento. Enquanto o trabalho correr no terminal, o quadro fica congelado e precisa ser sincronizado a partir deste documento e de `KANBAN.md`.

A política do quadro está em [`docs/PROCESSO-KANBAN.md`](./PROCESSO-KANBAN.md): critério de entrada e saída de cada coluna, limite de trabalho em andamento, anatomia do cartão e o procedimento de sincronização em cinco passos. Esse documento existe porque L-07 e L-09 nasceram, as duas, de sincronização mal definida.

Regra de triagem adotada: a coluna **Não Classificado permanece vazia**. Um cartão ali significa trabalho registrado sem critério, e a ação é triá-lo, não executá-lo. O limite de **Em Andamento é um cartão**.

## 7. Fila de execução

Ordem fixa. Cada item só começa quando o anterior está verde. O número em colchetes é o do cartão no quadro; subníveis (19.x) dependem do pai e não alteram a ordem.

1. **[19] Endpoints HTTP e tratamento de erro**: injeção de dependência, porta de entrada (L-03), tipos de comando e exceções ausentes do disco (L-07), endpoints de crédito, débito, estorno, posição e extrato, `ProblemDetails` com os códigos da EF §8.6, leitura do `Idempotency-Key`
   - **[19.1] Caminho de persistência do estorno** (L-08), junto ou depois do 19; pré-requisito do endpoint de estorno
   - **[19.2] Teste via Insomnia**, bloqueado pelo 19
   - **[19.3] Testes de contrato da API**, depois do 19
2. ~~**[16] Repositório público no GitHub** (L-01)~~ concluído em 2026-10-02, antecipado ao item 1
3. **[20] Diagramas em Mermaid no repositório** (L-02)
4. **[21] Correções documentais** (L-05)
5. **[22] Experimento de detecção do teste de concorrência** (L-04). Pode ser antecipado a qualquer momento: custa dois minutos
6. **[23] README final**
7. **[24] Despachante de outbox**
8. **[25] Painel de evidência** (condicional, ADR-0011, com critério de corte na hora 16)

---

## 8. Como verificar o estado atual

```bash
cd pacioli-bank-ledger
docker compose down -v        # necessário após mudança de esquema
dotnet test                   # esperado: 57 passando, 0 falhando, sem avisos
docker compose up --build     # API em http://localhost:8080
curl http://localhost:8080/health/live
```

Os testes de integração exigem Docker em execução.

---

## 9. Versões congeladas

Congeladas porque estão verificadas com build limpo e suíte verde, não porque são as mais recentes. Atualização de dependências é item próprio da fila, com verificação própria.

| Pacote | Versão |
|---|---|
| xunit | 2.9.2 |
| xunit.runner.visualstudio | 2.8.2 |
| Microsoft.NET.Test.Sdk | 17.12.0 |
| coverlet.collector | 6.0.2 |
| Testcontainers.PostgreSql | 4.15.0 |
| Npgsql | 10.0.3 |
| Dapper | 2.1.89 |

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
  docs/ESTADO.md                 estado atual, lacunas e fila de execução
  docs/adr/README.md             índice das 11 decisões arquiteturais
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

Comece lendo docs/ESTADO.md e me diga qual é o próximo item da fila e
por quê. Não avance sem minha confirmação.
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
