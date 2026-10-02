# PacioliBank Ledger

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

O banco é criado, o esquema é aplicado, os papéis com privilégio mínimo são provisionados e duas contas de exemplo, ativas e em BRL, já existem:

| Conta | Uso sugerido |
|---|---|
| `11111111-1111-1111-1111-111111111111` | Exemplos abaixo |
| `22222222-2222-2222-2222-222222222222` | Testes livres |

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

## Usar a API

Contrato completo na [EF §8](./docs/specs/EF-especificacao-funcional.md). As mesmas chamadas estão em [`requests.http`](./requests.http), executável no VS Code ou no Rider.

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
dotnet test        # 103 testes, sem erro e sem aviso
```

Pré-requisitos: SDK do .NET 10 e **Docker em execução**. Os testes de integração sobem um PostgreSQL real por execução (Testcontainers) e aplicam o mesmo script de esquema do ambiente local, com os mesmos papéis e privilégios.

| Projeto | Testes | O que verifica |
|---|---|---|
| `PacioliBank.Domain.Tests` | 59 | Invariantes do agregado, `Money`, validações da porta de entrada. Sem I/O, menos de um segundo |
| `PacioliBank.Integration.Tests` | 44 | Transação, bloqueio, idempotência, estorno, extrato, privilégio negado, concorrência real e despachante de outbox |

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
tests/
  PacioliBank.Domain.Tests/        59 testes, sem I/O
  PacioliBank.Integration.Tests/   44 testes, PostgreSQL real, inclui concorrência, estorno, extrato e outbox
db/init/                           esquema, papéis, privilégios e contas de exemplo
docs/                              diagramas, ADRs, especificações, estado do projeto
requests.http                      chamadas prontas para todos os endpoints
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
| Posição corrente com snapshot amortizado, e posição em instante passado | Implementado, com testes |
| Estorno por lançamento compensatório | Implementado, com testes |
| Extrato paginado por cursor | Implementado, com testes |
| Porta de entrada e os 5 endpoints de negócio | Implementado |
| Gravação de eventos na outbox, na transação do lançamento | Implementado |
| Ambiente local em um comando | Implementado |
| **Autenticação e autorização por titularidade (RF-009)** | **Pendente: hoje qualquer chamador opera qualquer conta** |
| Despachante de outbox, com publicação em log no lugar do barramento | Implementado, com testes |
| Barramento de eventos real | Pendente: plataforma não definida (ADR-0008) |
| Migrações versionadas (DbUp) | Pendente: o esquema só é aplicado na criação do volume |
| Testes de arquitetura e de contrato | Pendentes |
| Painel de evidência | Condicional, [ADR-0011](./docs/adr/ADR-0011-painel-de-evidencia.md) |

---

## O que seria feito com mais tempo

Em ordem de prioridade, com o motivo de cada posição.

1. **Autenticação e autorização por titularidade (RF-009).** É a maior distância entre o especificado e o implementado, e a única que impediria uso real. O desenho está pronto no ADR-0009: JWT validado contra o provedor de identidade, titularidade conferida no domínio e não só na borda, `404` para conta de terceiro para não revelar existência.
2. **Barramento de eventos real e expurgo da outbox (ADR-0008).** O despachante já lê a outbox com `FOR UPDATE SKIP LOCKED` e publica com recuo exponencial, mas publica em log: falta ligar a plataforma de mensageria do banco, expurgar as mensagens publicadas e conciliar ledger e outbox (RNF-033).
3. **Migrações com DbUp (RNF-038).** Hoje mudar o esquema exige recriar o volume. Inaceitável fora do ambiente local.
4. **CI no GitHub Actions** com build sem avisos, as camadas de teste e varredura de segredos. Transforma o critério de bloqueio do ADR-0010 de declarado em verificado a cada commit.
5. **Testes de arquitetura (NetArchTest) e de contrato (instantâneo do OpenAPI).** Hoje a direção das dependências é garantida pelo compilador só no domínio; o resto depende de revisão.
6. **Executar os `.feature` do BDD com Reqnroll.** Os cenários foram traduzidos para testes xUnit; a tradução pode divergir da especificação. O ADR-0010 previa a execução direta.
7. **Observabilidade (OpenTelemetry, Serilog),** com uma métrica de maior valor diagnóstico: a taxa de consultas calculadas pelo ledger em vez do snapshot, que cresce antes de a latência degradar.
8. **Teste de carga** para medir os RNF de desempenho, hoje especificados e não verificados.
9. **Itens com gatilho declarado, que não devem ser feitos antes dele:** posição diária pré-calculada para consulta histórica (quando o p99 passar do alvo), particionamento do ledger por tempo (depende da política de retenção, QA-004), transferência entre contas (exige novo ADR e bloqueio em ordem determinística), painel de evidência (condicional).

As questões de negócio que mudariam o desenho, como limite de cheque especial, lançamento retroativo e multimoeda, estão na [EF §10](./docs/specs/EF-especificacao-funcional.md), cada uma com a conduta provisória adotada.

---

## Rodar a API fora do Docker

Útil para depurar. Pré-requisitos: SDK do .NET 10 e Docker, só para o banco.

```bash
docker compose up -d pacioli-db            # só o PostgreSQL
docker compose stop pacioli-api            # libera a porta 8080, se a API em container estiver no ar
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/PacioliBank.Api --urls http://localhost:8080
```

O ambiente `Development` é o que carrega a connection string local (`appsettings.Development.json`). Sem ele, a API recusa a partida por falta da connection string `Ledger`, de propósito.

### Recriar o banco do zero

O esquema é aplicado pelo entrypoint do container PostgreSQL, que só executa na **primeira** criação do volume:

```bash
docker compose down -v && docker compose up --build -d
```

---

## Convenções de build

`Directory.Build.props` aplica a toda a solution `TreatWarningsAsErrors` (aviso de compilação bloqueia o build), `Nullable=enable` e os analisadores do .NET em modo `Default`, a ser elevado a `Recommended` em commit próprio.

Código em inglês, documentação e nomes de teste em português. A assimetria é deliberada: o código segue a convenção da plataforma, e a especificação e os testes precisam ser lidos por negócio e compliance sem tradução.

---

## Licença

MIT. Ver [LICENSE](./LICENSE).
