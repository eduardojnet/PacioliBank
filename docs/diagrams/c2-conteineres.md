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
        disp["Despachante de Outbox<br/>[Serviço hospedado]<br/>Publica eventos pendentes<br/>com SKIP LOCKED"]:::espec
        painel["Painel de Evidência<br/>[HTML e JS estático]<br/>Demonstra concorrência, idempotência<br/>e posição temporal"]:::espec
    end

    orig -->|"escreve<br/>HTTPS/JSON"| api
    canais -->|"consulta<br/>HTTPS/JSON"| api
    api -->|"SQL, uma transação por comando"| db
    api -.->|"valida JWT"| iam
    api -.->|"lê conta"| cad
    api -.->|"serve"| painel
    disp -.->|"lê outbox"| db
    disp -.->|"publica"| bus

    classDef externo fill:#999999,stroke:#6b6b6b,color:#fff
    classDef impl fill:#1168bd,stroke:#0b4884,color:#fff
    classDef espec fill:#ffffff,stroke:#6b6b6b,color:#333,stroke-dasharray:5 5
```

## Estado de cada elemento

| Elemento | Estado | Evidência |
|---|---|---|
| API do Ledger | Implementado, sem autenticação | `src/PacioliBank.Api`; `docker compose up --build` serve em `:8080` |
| Banco do Ledger | Implementado | `db/init/001_roles_and_schema.sql`: 5 tabelas, 3 papéis, constraints por regra |
| Escrita na outbox | Implementado | `PostgresLedgerStore.WriteAsync`, na transação do lançamento (ADR-0008) |
| Despachante de Outbox | Especificado | Card 24. A tabela e o índice parcial `ix_outbox_pending` já existem |
| Painel de Evidência | Especificado, condicional | Card 25, ADR-0011, com critério de corte |
| API → Provedor de Identidade | Especificado | RF-009 pendente |
| API → Cadastro de Contas | Especificado | Contas vêm de massa local |
| Barramento de Eventos | Externo, não escolhido | ADR-0008 decide o padrão, não o produto |

## Decisões que o diagrama torna visíveis

- **Um único armazenamento transacional.** Lançamento, sequência, idempotência e outbox na mesma transação local (EF CU-01, passo 8). Separar em contêineres distintos abriria janela de inconsistência em dado financeiro.
- **A publicação é assíncrona e fora do caminho crítico.** O lançamento não depende do barramento estar disponível (RF-011): o despachante publica depois, com entrega ao menos uma vez.
