# PacioliBank Ledger

Livro-razão de contas correntes: registra movimentações financeiras de clientes e responde à posição consolidada em qualquer instante, com consistência forte por conta.

O nome é uma referência a Luca Pacioli, que codificou o método das partidas dobradas em 1494. A escolha não é ornamental: o sistema adota ledger append-only com correção por lançamento compensatório, que é exatamente o método que Pacioli descreveu. Registro imutável, erro corrigido por contrapartida, nunca por rasura.

---

## Como rodar localmente

Pré-requisito único: Docker com Compose.

```bash
docker compose up --build
```

A API sobe em `http://localhost:8080`. O banco é criado, o esquema é aplicado e os papéis com privilégio mínimo são provisionados na primeira subida.

```bash
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready
```

O arquivo [`requests.http`](./requests.http) executa as chamadas direto no VS Code ou no Rider.

### Rodar sem Docker

Pré-requisito: SDK do .NET 10 e um PostgreSQL acessível.

```bash
dotnet build          # deve terminar sem erro e sem aviso
dotnet test           # 66 testes; os de integração exigem Docker
dotnet run --project src/PacioliBank.Api
```

### Recriar o banco do zero

O esquema é aplicado pelo entrypoint do container PostgreSQL, que só executa na **primeira** criação do volume. Para reaplicar:

```bash
docker compose down -v && docker compose up --build
```

> Esta é uma limitação conhecida deste estágio, aceita no ambiente local e registrada como item próprio da fila: substituir o mecanismo por migrações DbUp aplicadas na subida da aplicação, atendendo RNF-038.

---

## Decisões de arquitetura

A documentação é parte da entrega, não um anexo. As decisões estão registradas antes do código, com alternativas rejeitadas e gatilho de revisão.

| Documento | Conteúdo |
|---|---|
| [`docs/adr/`](./docs/adr/) | 11 decisões arquiteturais em formato MADR |
| [`docs/specs/EF-especificacao-funcional.md`](./docs/specs/EF-especificacao-funcional.md) | Domínio, regras de negócio, requisitos funcionais, contrato de API |
| [`docs/specs/ENF-especificacao-nao-funcional.md`](./docs/specs/ENF-especificacao-nao-funcional.md) | Atributos de qualidade, SLOs, riscos |
| [`docs/specs/BDD-comportamento.md`](./docs/specs/BDD-comportamento.md) | Cenários executáveis em Gherkin |
| [`docs/convencoes-de-nomenclatura.md`](./docs/convencoes-de-nomenclatura.md) | Nomes de projeto, esquema, papéis e eventos |

### As quatro decisões que definem o sistema

**[ADR-0003] O ledger é a única fonte da verdade.** A posição consolidada é dado derivado, nunca armazenado como verdade primária. É o que permite responder "qual era o saldo em 20 de janeiro" sem nenhuma estrutura adicional, e é o que torna cada alteração auditável por construção. Snapshot e cache são descartáveis: apagá-los não altera nenhuma resposta do sistema, apenas o tempo de resposta.

**[ADR-0005] A conta é a unidade de serialização.** A invariante de posição não negativa é garantida por bloqueio pessimista da linha da conta, dentro da transação de escrita, com `UNIQUE (account_id, sequence)` como garantia estrutural independente. Contas distintas não se bloqueiam, e é daí que vem a escala horizontal.

**[ADR-0006] A chave de idempotência é obrigatória.** A detecção acontece pela violação da chave primária, não por consulta prévia: consulta prévia abre uma janela de corrida, e é exatamente nessa janela que as requisições simultâneas chegam. Tornar a chave opcional faria do caminho inseguro o padrão.

**[ADR-0009] A imutabilidade é garantida por privilégio, não por disciplina.** O usuário de aplicação recebe `GRANT SELECT, INSERT` sobre o ledger e nada mais. Alterar um lançamento gravado é impossível, qualquer que seja o código. Controle que depende de ninguém errar não é controle.

---

## Estrutura

```
src/
  PacioliBank.Ledger/              dominio e portas. ZERO pacotes externos
    Domain/                        Money, Currency, Account, LedgerEntry, invariantes
    Application/                   ILedgerStore (porta de saida), resultados, fingerprint
  PacioliBank.Ledger.Persistence/  adaptador PostgreSQL: Dapper, SQL, transacao, bloqueio
  PacioliBank.Api/                 adaptador HTTP
tests/
  PacioliBank.Domain.Tests/        39 testes, sem I/O
  PacioliBank.Integration.Tests/   27 testes, PostgreSQL real, inclui concorrencia e estorno
db/init/                           esquema, papeis e privilegios
docs/adr/                          decisoes arquiteturais
docs/specs/                        especificacoes funcional, nao funcional e BDD
```

---

## Estado atual da implementação

Honestidade sobre o que existe é parte da entrega. Apresentar requisito especificado como implementado seria, em contrato real, informação incorreta prestada ao cliente.

| Item | Estado |
|---|---|
| Especificações e ADRs | Completos |
| Diagramas C4 (contexto, contêiner, componentes) | Completos |
| Esquema, papéis e privilégio mínimo | Implementado |
| `Money` e `Currency`, com testes | Implementado |
| Ambiente local em um comando | Implementado |
| Agregado Conta, invariantes e lançamento imutável, com testes | Implementado |
| Idempotência e bloqueio pessimista por conta, com testes | Implementado |
| Consulta de posição e snapshot inline amortizado | Implementado |
| Gravação transacional na outbox | Implementado |
| Testes de integração e concorrência contra PostgreSQL real | Implementados, 27 testes |
| Porta de entrada e endpoints de negócio | Pendente |
| Caminho de persistência do estorno, com testes | Implementado |
| Despachante de outbox | Pendente |
| Testes de arquitetura e de contrato | Pendentes |
| Painel de evidência | Condicional, ver ADR-0011 |

---

## Convenções de build

`Directory.Build.props` aplica a toda a solution:

- `TreatWarningsAsErrors`: aviso de compilação bloqueia o build
- `Nullable=enable`
- `AnalysisMode=Default`, a ser elevado a `Recommended` em commit próprio, após o primeiro build limpo

Código em inglês, documentação e cenários de teste em português. A assimetria é deliberada: o código segue a convenção da plataforma, e a especificação precisa ser lida por negócio e compliance sem tradução.

---

## Licença

MIT. Ver [LICENSE](./LICENSE).
