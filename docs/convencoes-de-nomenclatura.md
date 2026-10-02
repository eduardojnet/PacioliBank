# Convenções de Nomenclatura

**Projeto:** PacioliBank Ledger
**Versão:** 1.0
**Data:** 2026-10-02

Este documento não é um ADR. A regra 4 do [índice de ADRs](./adr/README.md) exige que toda decisão arquitetural rastreie a um requisito, e nomenclatura não rastreia: é convenção. Registrá-la como ADR seria incoerente com as próprias regras do diretório.

O que justifica o documento é outra coisa: nome escolhido depois do código custa refatoração de namespace, de esquema, de imagem e de identificador de evento. Fixar antes custa dez minutos.

---

## 1. Nome do produto

**PacioliBank**, em referência a Luca Pacioli, que codificou o método das partidas dobradas em *Summa de Arithmetica* (1494).

A escolha não é ornamental. O [ADR-0003](./adr/ADR-0003-ledger-append-only.md) adota ledger append-only com correção por lançamento compensatório, que é exatamente o método que Pacioli descreveu: registro imutável, erro corrigido por contrapartida, nunca por rasura. O nome e a arquitetura central dizem a mesma coisa.

**Nome não utilizado:** a marca da empresa avaliadora. O repositório é público, a marca pertence a terceiro e o enunciado do desafio é material dela. Usar a marca exporia o processo seletivo e criaria risco desnecessário de uso indevido de sinal distintivo alheio.

---

## 2. Repositório

| Item | Valor |
|---|---|
| Nome | `pacioli-bank-ledger` |
| Visibilidade | Pública |
| Licença | MIT |

O sufixo `-ledger` nomeia o sistema, não o ecossistema, deixando espaço para outros repositórios sob o mesmo nome de produto sem renomeação futura.

---

## 3. Estrutura da solution

```
pacioli-bank-ledger/
├── src/
│   ├── PacioliBank.Ledger/          # modulo Ledger: lancamentos e invariantes
│   ├── PacioliBank.Balances/        # modulo Balances: posicao e snapshot
│   ├── PacioliBank.Accounts/        # modulo Accounts: dados de referencia
│   ├── PacioliBank.Events/          # modulo de outbox e publicacao
│   ├── PacioliBank.Api/             # exposicao HTTP e wwwroot
│   └── PacioliBank.Migrations/      # scripts SQL versionados (DbUp)
├── tests/
│   ├── PacioliBank.Domain.Tests/
│   ├── PacioliBank.Integration.Tests/
│   ├── PacioliBank.Concurrency.Tests/
│   ├── PacioliBank.Architecture.Tests/
│   └── PacioliBank.Contract.Tests/
├── docs/
│   ├── adr/
│   ├── specs/
│   └── convencoes-de-nomenclatura.md
├── Directory.Build.props
├── docker-compose.yml
├── PacioliBank.sln
├── requests.http
└── README.md
```

> **Estado em 2026-10-02.** A árvore acima é a alvo. Existem hoje `PacioliBank.Ledger`, `PacioliBank.Ledger.Persistence` (ausente da árvore: o adaptador de dados virou projeto próprio para que a inversão de dependência seja garantida pelo compilador), `PacioliBank.Api`, `PacioliBank.Domain.Tests` e `PacioliBank.Integration.Tests`. `PacioliBank.Events` passou a existir no card 24. `Balances`, `Accounts`, `Migrations`, `Concurrency.Tests`, `Architecture.Tests` e `Contract.Tests` não existem; ver [ADR-0001](./adr/ADR-0001-estilo-arquitetural.md) e [ADR-0010](./adr/ADR-0010-estrategia-de-testes.md), notas de estado.

### Correções aos ADRs decorrentes desta convenção

Duas, registradas aqui e **aplicadas em 2026-10-02** nos documentos afetados (lacuna L-05), cada uma com nota de revisão no próprio ADR:

1. **O [ADR-0001](./adr/ADR-0001-estilo-arquitetural.md) nomeia o módulo de publicação como `Integration`.** Renomeado para `Events`, porque `PacioliBank.Integration` colidiria semanticamente com `PacioliBank.Integration.Tests`, que são testes de integração e não testes do módulo de integração. Colisão de vocabulário dentro da mesma solution é fonte permanente de confusão.

2. **O [ADR-0010](./adr/ADR-0010-estrategia-de-testes.md) nomeia os projetos de teste com prefixo `Ledger.`.** Substituído por `PacioliBank.`, já que `Ledger` passou a designar um módulo específico, não o sistema.

Registrar a correção em vez de editar silenciosamente preserva a rastreabilidade de por que o nome mudou.

---

## 4. Namespaces

Raiz: `PacioliBank`. Um namespace por módulo, com subdivisão interna por responsabilidade.

```csharp
namespace PacioliBank.Ledger.Domain;          // entidades, Value Objects, invariantes
namespace PacioliBank.Ledger.Application;     // casos de uso
namespace PacioliBank.Ledger.Persistence;     // adaptadores de dados
namespace PacioliBank.Balances.Domain;
namespace PacioliBank.Api.Endpoints;
```

A camada `Domain` de cada módulo é a fronteira verificada pelo teste de arquitetura ([ADR-0001](./adr/ADR-0001-estilo-arquitetural.md)): não referencia `Persistence`, `Api`, ASP.NET Core nem Npgsql.

---

## 5. Banco de dados

| Item | Valor |
|---|---|
| Base | `pacioli` |
| Esquema | `ledger` |
| Papel de migração | `pacioli_migrator` |
| Papel de execução | `pacioli_runtime` |
| Papel de leitura | `pacioli_readonly` |

Substitui `app_migrator`, `app_runtime` e `app_readonly` usados nos exemplos do [ADR-0009](./adr/ADR-0009-seguranca-e-privilegio-minimo.md), corrigidos lá em 2026-10-02. O prefixo evita colisão em instância compartilhada com outros sistemas.

Tabelas em `snake_case` plural: `ledger_entries`, `balance_snapshots`, `idempotency_records`, `outbox_messages`, `accounts`.

---

## 6. Containers e imagem

| Item | Valor |
|---|---|
| Serviço de aplicação | `pacioli-api` |
| Serviço de banco | `pacioli-db` |
| Imagem | `pacioli-bank/ledger:local` |
| Rede | `pacioli-net` |

---

## 7. API

| Item | Valor |
|---|---|
| Título OpenAPI | `PacioliBank Ledger API` |
| Prefixo de rota | `/api/v1` |
| Cabeçalho de correlação | `X-Correlation-Id` |
| Cabeçalho de idempotência | `Idempotency-Key` |
| Cabeçalho de repetição | `Idempotency-Replayed` |

### Identificadores de problema (RFC 9457)

```
urn:pacioli:problem:insufficient-funds
urn:pacioli:problem:idempotency-key-conflict
urn:pacioli:problem:account-inactive
```

**URN, não URL.** A [EF](./specs/EF-especificacao-funcional.md) §8.4 usava `https://api.banco.example/problems/...` como ilustração, corrigida para URN na versão 1.1. Adotar URL exigiria um domínio real e resolvível, sob pena de o campo `type` apontar para lugar nenhum, o que é pior que não apontar. A RFC admite qualquer URI, e URN é estável sem depender de infraestrutura.

---

## 8. Eventos de integração

Formato: `pacioli.ledger.<evento>.v<versão>`

```
pacioli.ledger.entry-recorded.v1
pacioli.ledger.entry-reversed.v1
```

A versão no próprio nome do tipo permite coexistência de versões durante migração de consumidores, sem negociação de esquema em tempo de execução.

---

## 9. Código

| Elemento | Convenção |
|---|---|
| Idioma do código | Inglês |
| Idioma da documentação e dos cenários BDD | Português |
| Tipos e membros públicos | `PascalCase` |
| Campos privados | `_camelCase` |
| Arquivos de migração | `NNNN_descricao_em_snake_case.sql` |
| Arquivos `.feature` | Nome da funcionalidade do [BDD](./specs/BDD-comportamento.md) |

A assimetria entre código em inglês e documentação em português é deliberada: o código segue a convenção universal da plataforma, e a especificação precisa ser lida por negócio e compliance sem tradução.
