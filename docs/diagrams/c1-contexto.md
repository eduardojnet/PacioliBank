# C1: Contexto

O PacioliBank Ledger no seu entorno: quem o usa e com quais sistemas ele conversa. Convenções em [README](./README.md).

```mermaid
flowchart TB
    cliente["Cliente<br/>[Pessoa]<br/>Titular de conta corrente no banco"]:::pessoa
    canais["Canais Digitais<br/>[Sistema externo]<br/>App e internet banking"]:::externo
    orig["Sistemas Originadores<br/>[Sistema externo]<br/>Pix, cartão, folha, tarifas"]:::externo
    ledger["PacioliBank Ledger<br/>[Sistema em foco]<br/>Registra movimentações e responde à<br/>posição consolidada em qualquer instante"]:::parcial
    cad["Cadastro de Contas<br/>[Sistema externo]<br/>Dados de referência da conta"]:::externo
    iam["Provedor de Identidade<br/>[Sistema externo]<br/>Emite e assina tokens"]:::externo
    cons["Consumidores de Evento<br/>[Sistema externo]<br/>Extrato, notificação, antifraude"]:::externo

    cliente -->|usa| canais
    canais -->|"consulta posição e extrato<br/>HTTPS/JSON"| ledger
    orig -->|"registra crédito, débito, estorno<br/>HTTPS/JSON"| ledger
    ledger -.->|"lê conta<br/>(especificado)"| cad
    ledger -.->|"valida JWT<br/>(especificado)"| iam
    ledger -.->|"publica eventos<br/>(especificado)"| cons

    classDef pessoa fill:#08427b,stroke:#052e56,color:#fff
    classDef externo fill:#999999,stroke:#6b6b6b,color:#fff
    classDef parcial fill:#85bbf0,stroke:#1168bd,color:#0b2545
```

## Estado de cada elemento

| Elemento | Estado | Evidência |
|---|---|---|
| PacioliBank Ledger | **Parcial** | Os 5 endpoints da EF §8.3 respondem; autenticação, autorização e publicação de eventos não existem |
| Canais Digitais → Ledger (consulta) | Implementado | `GET /balance` e `GET /entries` em `Endpoints/LedgerEndpoints.cs` |
| Originadores → Ledger (registro) | Implementado | `POST /credits`, `/debits`, `/entries/{id}/reversals` |
| Ledger → Cadastro de Contas | Especificado | Hoje a tabela `ledger.accounts` é preenchida por massa local (`db/init/002_seed_local.sql`); a replicação do Cadastro não existe |
| Ledger → Provedor de Identidade | Especificado | Nenhuma validação de token. Qualquer chamador opera qualquer conta (ESTADO §5, RF-009) |
| Ledger → Consumidores de Evento | Especificado | O evento é **gravado** na outbox, na mesma transação do lançamento (ADR-0008); nada o **publica** (card 24) |

## Fronteira que o diagrama fixa

O ciclo de vida da conta pertence ao Cadastro, não ao Ledger. O privilégio no banco reflete isso: o papel da aplicação só pode alterar `last_sequence` em `ledger.accounts` (ADR-0009, EF §3.2).
