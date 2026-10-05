# C2: Contêineres

Processos e armazenamentos que compõem o sistema. Convenções em [README](./README.md).

```mermaid
flowchart TB
    orig["Sistemas Originadores<br/>[Sistema externo]<br/>Pix, cartão, folha, tarifas"]:::externo
    canais["Canais Digitais<br/>[Sistema externo]<br/>App e internet banking"]:::externo
    iam["Provedor de Identidade<br/>[Sistema externo]<br/>Assina os tokens"]:::externo
    cad["Cadastro de Contas<br/>[Sistema externo]<br/>Status, moeda e titular"]:::externo
    bus["Barramento de Eventos<br/>[Sistema externo]<br/>Entrega ao menos uma vez"]:::externo

    subgraph sistema["Sistema: PacioliBank Ledger"]
        api["API do Ledger<br/>[ASP.NET Core 10]<br/>Valida invariantes, serializa escritas<br/>por conta e calcula a posição"]:::impl
        db[("Banco do Ledger<br/>[PostgreSQL 17]<br/>Lançamentos imutáveis, snapshots,<br/>idempotência e outbox")]:::impl
        disp["Despachante de Outbox<br/>[Serviço hospedado, no processo da API]<br/>Publica eventos pendentes<br/>com SKIP LOCKED"]:::impl
        mig["Migrador<br/>[Console .NET 10, DbUp]<br/>Aplica as migrações pendentes<br/>com o papel de migração e termina"]:::impl
        painel["Painel de Evidência<br/>[HTML e JS estático, servido pela API]<br/>Demonstra concorrência, idempotência,<br/>posição temporal e snapshot"]:::impl
    end

    orig -->|"escreve<br/>HTTPS/JSON"| api
    canais -->|"consulta<br/>HTTPS/JSON"| api
    api -->|"SQL, uma transação por comando"| db
    api -.->|"valida JWT"| iam
    api -.->|"lê conta"| cad
    api -->|"serve, na raiz"| painel
    disp -->|"lê e marca a outbox"| db
    mig -->|"DDL e GRANT, antes da API"| db
    disp -.->|"publica<br/>(hoje: registra em log)"| bus

    classDef externo fill:#999999,stroke:#6b6b6b,color:#fff
    classDef impl fill:#1168bd,stroke:#0b4884,color:#fff
    classDef espec fill:#ffffff,stroke:#6b6b6b,color:#333,stroke-dasharray:5 5
```

## Estado de cada elemento

| Elemento | Estado | Evidência |
|---|---|---|
| API do Ledger | Implementado, sem autenticação | `src/PacioliBank.Api`; `docker compose up --build` serve em `:8080` |
| Banco do Ledger | Implementado | `db/migrations/` (0001 e 0002): 6 tabelas, 3 papéis, constraints por regra |
| Migrador | Implementado | Card 27: `src/PacioliBank.Migrations`, serviço `pacioli-migrations` do `docker compose`. Único processo com a credencial de migração; a API só sobe depois que ele termina com sucesso (ADR-0002, revisão; ADR-0009) |
| Escrita na outbox | Implementado | `PostgresLedgerStore.WriteAsync`, na transação do lançamento (ADR-0008) |
| Despachante de Outbox | Implementado | Card 24: `PacioliBank.Events/OutboxDispatcher.cs`, executado por `OutboxDispatcherService` no processo da API. Rodar no mesmo processo é decisão operacional (ADR-0008); várias instâncias convivem pelo `SKIP LOCKED` |
| Despachante → Barramento | Especificado | O publicador atual registra o evento em log (`LoggingEventPublisher`), como o ADR-0008 prevê enquanto a plataforma de mensageria não é conhecida |
| Painel de Evidência | Implementado | Card 25: `src/PacioliBank.Api/wwwroot/`, quatro demonstrações que usam só a API pública. Sem teste automatizado próprio, por decisão do ADR-0011; verificado em Chrome headless |
| API → Provedor de Identidade | Especificado | RF-009 pendente |
| API → Cadastro de Contas | Especificado | Contas vêm de massa local |
| Barramento de Eventos | Externo, não escolhido | ADR-0008 decide o padrão, não o produto |

## Decisões que o diagrama torna visíveis

- **Um único armazenamento transacional.** Lançamento, sequência, idempotência e outbox na mesma transação local (EF CU-01, passo 8). Separar em contêineres distintos abriria janela de inconsistência em dado financeiro.
- **A publicação é assíncrona e fora do caminho crítico.** O lançamento não depende do barramento estar disponível (RF-011): o despachante publica depois, com entrega ao menos uma vez.
