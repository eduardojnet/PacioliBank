# PacioliBank Ledger

[![CI](https://github.com/eduardojnet/PacioliBank/actions/workflows/ci.yml/badge.svg)](https://github.com/eduardojnet/PacioliBank/actions/workflows/ci.yml)

Livro-razão de contas correntes: registra movimentações financeiras de clientes e responde à posição consolidada em qualquer instante, com consistência forte por conta.

O nome é uma referência a Luca Pacioli, que codificou o método das partidas dobradas em 1494. A escolha não é ornamental: o sistema adota ledger append-only com correção por lançamento compensatório, que é exatamente o método que Pacioli descreveu. Registro imutável, erro corrigido por contrapartida, nunca por rasura.

---

## Em cinco minutos

Pré-requisito único: Docker com Compose.

```bash
git clone https://github.com/eduardojnet/PacioliBank.git
cd PacioliBank
docker compose up --build -d
curl http://localhost:8080/health/ready        # {"status":"ready"}
```

O banco é criado, o migrador aplica as migrações pendentes e termina, e só então a API sobe. Os papéis com privilégio mínimo são provisionados e cinco contas de exemplo, ativas e em BRL, já existem:

| Conta | Uso sugerido |
|---|---|
| `11111111-1111-1111-1111-111111111111` | Exemplos abaixo |
| `22222222-2222-2222-2222-222222222222` | Testes livres |
| `33333333-…`, `44444444-…`, `55555555-…` | Reservadas ao [painel de evidência](#painel-de-evidência) |

Registrar um crédito e consultar a posição:

```bash
A=http://localhost:8080/api/v1/accounts/11111111-1111-1111-1111-111111111111

curl -i -X POST $A/credits \
  -H 'Idempotency-Key: credito-001' -H 'Content-Type: application/json' \
  -d '{"amount":"150.00","currency":"BRL","occurredAt":"2026-10-02T10:00:00Z"}'

curl $A/balance
```

O primeiro comando responde `201 Created` com o lançamento; o segundo, a posição `"150.00"`.

---

## Painel de evidência

Com o ambiente no ar, abra **<http://localhost:8080/>**. Quatro demonstrações, cada uma provando uma decisão de arquitetura contra a API real, pelos mesmos endpoints públicos de qualquer integrador:

| Painel | O que acontece | Decisão provada |
|---|---|---|
| Disparo concorrente | 50 débitos de uma vez sobre saldo para 10: exatamente 10 aceitos, 40 recusados, posição nunca negativa | [ADR-0005](./docs/adr/ADR-0005-controle-de-concorrencia.md) |
| Linha do tempo | Controle deslizante recalculando a posição em qualquer instante passado, com lançamentos de data do fato retroativa | [ADR-0003](./docs/adr/ADR-0003-ledger-append-only.md) |
| Reenvio idempotente | Mesmo comando duas vezes: `201`, depois `200` com `Idempotency-Replayed` e o mesmo corpo, byte a byte | [ADR-0006](./docs/adr/ADR-0006-idempotencia.md) |
| Origem do cálculo | Atravessa a âncora de snapshot (a cada 100 lançamentos) e mostra `computedFrom` e `entriesReplayed` | [ADR-0007](./docs/adr/ADR-0007-snapshot-e-projecao.md) |

É material de demonstração, sem teste automatizado próprio, por decisão do [ADR-0011](./docs/adr/ADR-0011-painel-de-evidencia.md): a prova está na suíte de testes, e o painel a torna visível. Usa as contas `3333…`, `4444…` e `5555…`, reservadas a ele; cada demonstração prepara o próprio estado e pode ser repetida.

---

## Usar a API

Contrato completo na [EF §8](./docs/specs/EF-especificacao-funcional.md). As mesmas chamadas estão em [`requests.http`](./requests.http), executável no VS Code ou no Rider, e numa [coleção do Insomnia](#coleção-do-insomnia) com testes embutidos. O documento OpenAPI, gerado a partir do código, é servido em **`/openapi/v1.json`**.

| Verbo | Rota | O que faz |
|---|---|---|
| `POST` | `/api/v1/accounts/{conta}/credits` | Registra crédito |
| `POST` | `/api/v1/accounts/{conta}/debits` | Registra débito; recusa se a posição ficaria negativa |
| `POST` | `/api/v1/accounts/{conta}/entries/{lancamento}/reversals` | Estorna um lançamento por compensação |
| `GET` | `/api/v1/accounts/{conta}/balance` | Posição corrente; com `?asOf=`, a posição naquele instante |
| `GET` | `/api/v1/accounts/{conta}/entries` | Extrato paginado por cursor |
| `GET` | `/health/live`, `/health/ready` | Vivo; pronto (consulta o PostgreSQL) |

**Regras do contrato que mais importam:**

- **Valor monetário é string**, nunca número JSON: `"150.00"`. Número JSON passa por ponto flutuante em muitos clientes, e arredondamento silencioso em ledger é defeito financeiro (EF §8.2)
- **`Idempotency-Key` é obrigatório** em toda escrita. Reenviar o mesmo comando com a mesma chave devolve `200`, o cabeçalho `Idempotency-Replayed: true` e o corpo original, byte a byte, mesmo que o saldo tenha mudado desde então. Mesma chave com conteúdo diferente: `409`
- **Instantes em ISO 8601 UTC.** `occurredAt` é a data do fato; a consulta histórica usa ela, não a data de registro
- **`X-Correlation-Id`** é aceito do chamador ou gerado, e sempre devolvido, inclusive em erro

### Exemplos

Os comandos abaixo continuam a sequência de [Em cinco minutos](#em-cinco-minutos) e usam a mesma variável `$A`.

```bash
# Débito
curl -i -X POST $A/debits \
  -H 'Idempotency-Key: debito-001' -H 'Content-Type: application/json' \
  -d '{"amount":"40.00","currency":"BRL","occurredAt":"2026-10-02T11:00:00Z"}'

# Reenvio do mesmo débito: 200, Idempotency-Replayed: true, nenhum lançamento novo
curl -i -X POST $A/debits \
  -H 'Idempotency-Key: debito-001' -H 'Content-Type: application/json' \
  -d '{"amount":"40.00","currency":"BRL","occurredAt":"2026-10-02T11:00:00Z"}'

# Débito acima do saldo: 422 INSUFFICIENT_FUNDS, nada gravado
curl -i -X POST $A/debits \
  -H 'Idempotency-Key: debito-002' -H 'Content-Type: application/json' \
  -d '{"amount":"999.00","currency":"BRL","occurredAt":"2026-10-02T11:30:00Z"}'

# Posição em um instante passado, antes do débito
curl "$A/balance?asOf=2026-10-02T10:30:00Z"

# Extrato, 50 por página; para a próxima, repita com ?cursor=<nextCursor>
curl "$A/entries?limit=50"

# Estorno do débito: copie o entryId do débito no extrato
curl -i -X POST $A/entries/<entryId>/reversals \
  -H 'Idempotency-Key: estorno-001' -H 'Content-Type: application/json' \
  -d '{"occurredAt":"2026-10-02T12:00:00Z"}'
```

### Coleção do Insomnia

[`insomnia/pacioli-ledger.insomnia.json`](./insomnia/pacioli-ledger.insomnia.json): 15 requisições em três pastas (saúde, escrita, consulta), cada uma com os próprios testes, 43 no total. Cobre crédito, débito, reenvio idempotente, conflito de chave, saldo insuficiente, chave ausente, estorno e estorno duplicado, posição corrente e histórica, instante futuro, extrato e conta inexistente. As escritas geram a chave de idempotência antes do envio, e o reenvio e o estorno usam o resultado das anteriores; a coleção pode ser rodada repetidamente. Usa a conta `2222…`.

- **No aplicativo:** *Import*, escolher o arquivo, e *Run* na coleção
- **Pela linha de comando**, com o [`inso`](https://github.com/Kong/insomnia/releases) (verificado na versão 13.3.0), o mesmo motor do aplicativo:

```bash
inso run collection wrk_pacioli_ledger -w insomnia/pacioli-ledger.insomnia.json --env env_pacioli_base
```

O código de saída é 0 só com todos os testes verdes, então o comando serve para CI.

### Erros

Toda rejeição sai como `application/problem+json` (RFC 9457) com um campo `code` estável:

```json
{
  "type": "urn:pacioli:problem:insufficient-funds",
  "title": "Saldo insuficiente",
  "status": 422,
  "code": "INSUFFICIENT_FUNDS",
  "detail": "O debito solicitado excede a posicao disponivel.",
  "availableBalance": "110.00",
  "requestedAmount": "999.00",
  "traceId": "…",
  "correlationId": "…"
}
```

| HTTP | Códigos | Reenviar com a mesma chave? |
|---|---|---|
| 400 | `INVALID_AMOUNT`, `CURRENCY_MISMATCH`, `IDEMPOTENCY_KEY_REQUIRED`, `INVALID_POINT_IN_TIME`, `PAGE_SIZE_EXCEEDED`, `INVALID_REQUEST` | Não |
| 404 | `ACCOUNT_NOT_FOUND`, `ENTRY_NOT_FOUND` | Não |
| 409 | `IDEMPOTENCY_KEY_CONFLICT`, `ENTRY_ALREADY_REVERSED`, `CANNOT_REVERSE_REVERSAL` | Não |
| 422 | `INSUFFICIENT_FUNDS`, `ACCOUNT_INACTIVE` | Não |
| 503 | `SERVICE_UNAVAILABLE` | **Sim**: a chave de idempotência torna o reenvio seguro |

Catálogo completo, com a regra de negócio de cada código, na [EF §8.6](./docs/specs/EF-especificacao-funcional.md).

---

## Testes

```bash
dotnet test        # 116 testes, sem erro e sem aviso
```

Pré-requisitos: SDK do .NET 10 e **Docker em execução**. Os testes de integração sobem um PostgreSQL real por execução (Testcontainers) e aplicam o mesmo script de esquema do ambiente local, com os mesmos papéis e privilégios.

| Projeto | Testes | O que verifica |
|---|---|---|
| `PacioliBank.Domain.Tests` | 59 | Invariantes do agregado, `Money`, validações da porta de entrada. Sem I/O, menos de um segundo |
| `PacioliBank.Architecture.Tests` | 5 | Regras de dependência (NetArchTest): domínio sem aplicação, Ledger sem banco nem HTTP, Events sem Ledger, endpoints sem banco |
| `PacioliBank.Contract.Tests` | 2 | Contrato da API: o OpenAPI gerado é comparado com o instantâneo aprovado; mudança de contrato reprova |
| `PacioliBank.Integration.Tests` | 50 | Transação, bloqueio, idempotência, estorno, extrato, privilégio negado, concorrência real e despachante de outbox |

```bash
dotnet test tests/PacioliBank.Domain.Tests                                 # só domínio, sem Docker
dotnet test tests/PacioliBank.Integration.Tests --filter ConcurrencyTests  # só concorrência
```

**Por que banco real, e não repositório em memória:** as invariantes que importam aqui vivem na interação entre código e banco. Posição não negativa depende de bloqueio de linha; sequência sem lacunas, de constraint; idempotência sob envio simultâneo, de chave primária; imutabilidade, de privilégio. Um teste de concorrência contra memória passa na implementação ingênua, o que é pior que não testar ([ADR-0010](./docs/adr/ADR-0010-estrategia-de-testes.md)).

**O teste de concorrência foi testado.** Removendo o bloqueio da conta, ele reprova. O motivo surpreendeu e está registrado no [ADR-0005](./docs/adr/ADR-0005-controle-de-concorrencia.md): sem o bloqueio, a constraint de sequência ainda impede saldo negativo, mas a maior parte dos comandos termina em `503`. O teste enxerga isso porque confere a contagem exata de cada desfecho, não só a posição final.

---

## Decisões de arquitetura

As decisões estão registradas com alternativas rejeitadas e gatilho de revisão. Onde o código divergiu do que estava escrito, o documento foi corrigido com nota de revisão, não em silêncio.

| Documento | Conteúdo |
|---|---|
| [`docs/diagrams/`](./docs/diagrams/) | Modelo C4 em Mermaid (contexto, contêineres, componentes, sequência), com o estado de cada elemento |
| [`docs/adr/`](./docs/adr/) | 11 decisões arquiteturais em formato MADR |
| [`docs/specs/EF-especificacao-funcional.md`](./docs/specs/EF-especificacao-funcional.md) | Domínio, regras de negócio, requisitos funcionais, contrato de API |
| [`docs/specs/ENF-especificacao-nao-funcional.md`](./docs/specs/ENF-especificacao-nao-funcional.md) | Atributos de qualidade, SLOs, riscos |
| [`docs/specs/BDD-comportamento.md`](./docs/specs/BDD-comportamento.md) | Cenários de aceite em Gherkin |
| [`docs/ESTADO.md`](./docs/ESTADO.md) | Estado detalhado, lacunas por severidade, espelho da fila do quadro e histórico |
| [`docs/PROCESSO-KANBAN.md`](./docs/PROCESSO-KANBAN.md) | Política do quadro Kanban, que é a fonte de toda atividade |

### As decisões que definem o sistema

**[ADR-0003](./docs/adr/ADR-0003-ledger-append-only.md): o ledger é a única fonte da verdade.** A posição consolidada é derivada, nunca armazenada como verdade primária. É o que permite responder "qual era o saldo em 20 de janeiro" sem estrutura adicional, e o que torna cada alteração auditável por construção. Snapshot é descartável: apagá-lo não altera nenhuma resposta, só o tempo de resposta.

**[ADR-0005](./docs/adr/ADR-0005-controle-de-concorrencia.md): a conta é a unidade de serialização.** Toda escrita bloqueia a linha da conta (`FOR NO KEY UPDATE`) antes de ler a posição, e `UNIQUE (account_id, sequence)` é a garantia estrutural independente. Medido: a constraint sustenta a correção mesmo sem o bloqueio; o bloqueio é o que mantém o sistema disponível sob contenção. Contas distintas não se bloqueiam, e é daí que vem a escala horizontal.

**[ADR-0006](./docs/adr/ADR-0006-idempotencia.md): a chave de idempotência é obrigatória, e o reenvio recebe sempre o resultado original.** A chave primária no banco impede a duplicidade, inclusive sob envio simultâneo. A repetição é reconhecida sob o bloqueio da conta, antes de as regras de negócio avaliarem o comando: assim, um débito reenviado depois de o saldo cair recebe o resultado original, e não uma recusa por saldo para um pagamento que já aconteceu. Essa segunda parte é uma revisão do ADR, feita quando o defeito foi achado.

**[ADR-0009](./docs/adr/ADR-0009-seguranca-e-privilegio-minimo.md): a imutabilidade é garantida por privilégio, não por disciplina.** O papel da aplicação recebe `SELECT, INSERT` sobre o ledger e nada mais. Alterar um lançamento gravado é impossível, qualquer que seja o código, e há teste de integração que tenta e verifica a recusa.

**[ADR-0001](./docs/adr/ADR-0001-estilo-arquitetural.md): monolito modular, Ports and Adapters.** Lançamento, sequência, idempotência e evento precisam estar na mesma transação local; distribuí-los trocaria uma transação ACID por uma saga sem ganho correspondente. O projeto de domínio não declara nenhum pacote externo: referenciar o driver do banco ali é erro de compilação.

---

## Estrutura

```
src/
  PacioliBank.Ledger/              domínio e aplicação. ZERO pacotes externos
    Domain/                        Money, Currency, Account, LedgerEntry, invariantes
    Application/                   ILedgerService (porta de entrada), ILedgerStore (porta de saída), comandos
  PacioliBank.Ledger.Persistence/  adaptador PostgreSQL: Dapper, SQL explícito, transação, bloqueio
  PacioliBank.Events/              despachante de outbox: SKIP LOCKED, recuo, alerta
  PacioliBank.Api/                 adaptador HTTP: endpoints, problem+json, correlação; hospeda o despachante
    wwwroot/                       painel de evidência: HTML, CSS e JavaScript, sem dependências
  PacioliBank.Migrations/          migrador (DbUp): aplica o que falta com o papel de migração e termina
tests/
  PacioliBank.Domain.Tests/        85 testes, sem I/O; cobertura do domínio ≥ 85% exigida no CI
  PacioliBank.Architecture.Tests/  6 regras de dependência (NetArchTest)
  PacioliBank.Contract.Tests/      2 testes, instantâneo do contrato OpenAPI
  PacioliBank.Integration.Tests/   60 testes, PostgreSQL real, inclui concorrência, estorno, extrato, outbox, migrações e snapshot
db/migrations/                     esquema, papéis e privilégios, em migrações numeradas
db/seed/                           contas de exemplo, só no ambiente local
docs/                              diagramas, ADRs, especificações, estado do projeto
requests.http                      chamadas prontas para todos os endpoints
insomnia/                          colecao do Insomnia, com testes, executavel pelo inso
```

---

## Estado atual da implementação

Apresentar requisito especificado como implementado seria, em contrato real, informação incorreta prestada ao cliente. Detalhe e lacunas em [`docs/ESTADO.md`](./docs/ESTADO.md).

| Item | Estado |
|---|---|
| Especificações, ADRs e diagramas C4 | Completos e coerentes com o código |
| Esquema, papéis e privilégio mínimo | Implementado |
| Agregado Conta, `Money`, invariantes e lançamento imutável | Implementado, com testes |
| Bloqueio por conta e idempotência, inclusive sob envio simultâneo | Implementado, com testes |
| Posição corrente com snapshot amortizado, e posição em instante passado | Implementado, com testes, inclusive do snapshot e do limite de 99 lançamentos somados (card 30.1). A posição histórica não usa snapshot e soma todo o histórico até o instante (card 32) |
| Estorno por lançamento compensatório | Implementado, com testes |
| Extrato paginado por cursor | Implementado, com testes |
| Porta de entrada e os 5 endpoints de negócio | Implementado |
| Gravação de eventos na outbox, na transação do lançamento | Implementado |
| Ambiente local em um comando | Implementado |
| **Autenticação e autorização por titularidade (RF-009)** | **Pendente: hoje qualquer chamador opera qualquer conta** |
| Despachante de outbox, com publicação em log no lugar do barramento | Implementado, com testes |
| Barramento de eventos real | Pendente: plataforma não definida (ADR-0008) |
| Migrações versionadas (DbUp), em passo separado com o papel de migração | Implementado, com testes |
| Documento OpenAPI e teste de contrato por instantâneo | Implementado |
| Testes de arquitetura (NetArchTest), 6 regras de dependência entre módulos e camadas | Implementado |
| Analisadores do .NET em modo `Recommended`, com aviso tratado como erro | Implementado |
| CI no GitHub Actions: build sem avisos, todas as camadas de teste, coleção do Insomnia, varredura de segredos | Implementado |
| Auditoria de dependências vulneráveis, transitivas inclusive: alta e crítica reprovam o build (RNF-026) | Implementado |
| Cobertura de linha do domínio ≥ 85% (RNF-036), medida pelos testes de domínio, com limite no CI | Implementado: 86,4% |
| Painel de evidência, quatro demonstrações na raiz da API | Implementado, sem teste automatizado próprio ([ADR-0011](./docs/adr/ADR-0011-painel-de-evidencia.md)) |

---

## O que seria feito com mais tempo

Em ordem de prioridade, com o motivo de cada posição.

1. **Autenticação e autorização por titularidade (RF-009).** É a maior distância entre o especificado e o implementado, e a única que impediria uso real. O desenho está pronto no ADR-0009: JWT validado contra o provedor de identidade, titularidade conferida no domínio e não só na borda, `404` para conta de terceiro para não revelar existência.
2. **Barramento de eventos real e expurgo da outbox (ADR-0008).** O despachante já lê a outbox com `FOR UPDATE SKIP LOCKED` e publica com recuo exponencial, mas publica em log: falta ligar a plataforma de mensageria do banco, expurgar as mensagens publicadas e conciliar ledger e outbox (RNF-033).
3. **Regra de compatibilidade entre migração e versão da API.** As migrações já rodam em passo separado (card 27), mas com várias instâncias a migração precisa ser compatível com a versão anterior enquanto as duas coexistem (expandir antes, contrair depois). Hoje há uma instância só, e a regra não está escrita.
4. **Teste de mutação (Stryker) no domínio.** O CI já aplica todo o critério de bloqueio do ADR-0010, inclusive cobertura mínima de 85% (cards 31, 31.1 e 31.2); cobertura mede o que roda, não se a asserção pegaria o erro.
5. **Interface de exploração do OpenAPI**, que já é gerado: custo baixo, um pacote a mais.
6. **Executar os `.feature` do BDD com Reqnroll.** Os cenários foram traduzidos para testes xUnit; a tradução pode divergir da especificação. O ADR-0010 previa a execução direta.
7. **Observabilidade (OpenTelemetry, Serilog),** com uma métrica de maior valor diagnóstico: a taxa de consultas calculadas pelo ledger em vez do snapshot, que cresce antes de a latência degradar.
8. **Teste de carga** para medir os RNF de desempenho, hoje especificados e não verificados.
9. **Itens com gatilho declarado, que não devem ser feitos antes dele:** posição diária pré-calculada para consulta histórica (quando o p99 passar do alvo), particionamento do ledger por tempo (depende da política de retenção, QA-004), transferência entre contas (exige novo ADR e bloqueio em ordem determinística).

As questões de negócio que mudariam o desenho, como limite de cheque especial, lançamento retroativo e multimoeda, estão na [EF §10](./docs/specs/EF-especificacao-funcional.md), cada uma com a conduta provisória adotada.

---

## Rodar a API fora do Docker

Útil para depurar. Pré-requisitos: SDK do .NET 10 e Docker, só para o banco.

```bash
docker compose up -d pacioli-migrations    # PostgreSQL e migrações; o migrador termina sozinho
docker compose stop pacioli-api            # libera a porta 8080, se a API em container estiver no ar
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/PacioliBank.Api --urls http://localhost:8080
```

O ambiente `Development` é o que carrega a connection string local (`appsettings.Development.json`). Sem ele, a API recusa a partida por falta da connection string `Ledger`, de propósito.

### Mudar o esquema

Mudança de esquema é uma migração nova em `db/migrations/`, com o número seguinte (`0002_descricao.sql`). Script já aplicado não se edita. O próximo `docker compose up --build` roda o migrador, que aplica só o que falta, sem perder dados. O que já rodou fica em `public.schema_versions`.

Para recomeçar com o banco vazio, de propósito:

```bash
docker compose down -v && docker compose up --build -d
```

Bancos locais criados antes do card 27 não têm o diário de migrações e precisam desse recomeço uma vez.

---

## Convenções de build

`Directory.Build.props` aplica a toda a solution `TreatWarningsAsErrors` (aviso de compilação bloqueia o build), `Nullable=enable` e os analisadores do .NET em modo `Recommended` (`latest-recommended`, card 28): regra recomendada violada reprova o build. Há duas supressões, ambas com o motivo escrito ao lado: CA1707 nos projetos de teste (nomes de teste em português, com sublinhado, lidos por quem não programa) e CA1031 no despachante de outbox (falha de qualquer tipo no publicador vira nova tentativa, nunca derruba o lote).

Código em inglês, documentação e nomes de teste em português. A assimetria é deliberada: o código segue a convenção da plataforma, e a especificação e os testes precisam ser lidos por negócio e compliance sem tradução.

---

## Licença

MIT. Ver [LICENSE](./LICENSE).
