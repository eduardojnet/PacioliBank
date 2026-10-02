# PacioliBank Ledger

Livro-razão de contas correntes em C#: registra movimentações financeiras e responde à posição consolidada de um cliente em qualquer instante, com consistência forte por conta.

Entrega de um desafio técnico de Arquiteto de Software. O avaliador lê o código **e** as decisões.

---

## Comandos

```bash
dotnet build                  # deve terminar sem erro E SEM AVISO
dotnet test                   # 97 testes; os de integração exigem Docker
docker compose up --build     # API em http://localhost:8080
docker compose down -v        # obrigatório após qualquer mudança de esquema
```

O esquema é aplicado pelo entrypoint do PostgreSQL, que **só roda na primeira criação do volume**. Mudou `db/init/*.sql`? Então `down -v` antes de subir.

---

## Ordem de leitura

1. `docs/ESTADO.md` — estado atual, lacunas por severidade, fila de execução
2. `docs/adr/README.md` — índice das 11 decisões arquiteturais
3. `docs/specs/EF-especificacao-funcional.md` — domínio, regras (RN-xxx) e contratos
4. `docs/convencoes-de-nomenclatura.md`
5. `docs/KANBAN.md` — espelho do quadro de projeto

---

## Arquitetura em cinco linhas

- **Monolito modular**, Ports and Adapters, DDD tático no domínio (ADR-0001)
- **Ledger append-only é a única fonte da verdade.** Posição é derivada; snapshot e cache são descartáveis (ADR-0003)
- **A conta é a unidade de serialização.** `FOR NO KEY UPDATE` na linha da conta, antes de ler a posição (ADR-0005)
- **Idempotência obrigatória**, detectada pela violação de chave primária, nunca por consulta prévia (ADR-0006)
- **Imutabilidade por privilégio**: o papel da aplicação tem `SELECT, INSERT` no ledger e nada mais (ADR-0009)

Inversão de dependência é física: `PacioliBank.Ledger` não declara nenhum `PackageReference`, então referenciar Npgsql no domínio é erro de compilação.

---

## Regras invioláveis

1. **Aviso de compilação é erro.** `TreatWarningsAsErrors` está ativo. Nunca suprimir um aviso para "destravar"; corrigir ou justificar com `NoWarn` comentado.
2. **Nenhuma decisão arquitetural sem ADR**, com alternativas rejeitadas e gatilho de revisão. Um ADR sem alternativa rejeitada é documentação de implementação, não registro de decisão.
3. **Não preencher lacuna de negócio com suposição silenciosa.** Vira questão aberta em `EF §10`, com conduta provisória declarada.
4. **Marcar `[NVI]`** o que não foi verificado diretamente.
5. **Separar sempre implementado de especificado.** Apresentar um como o outro é informação incorreta prestada ao cliente.
6. **Invariante financeira é garantida por constraint e privilégio no banco**, não por disciplina de código. Controle que depende de ninguém errar não é controle.
7. **Teste de invariante de persistência roda contra PostgreSQL real** (Testcontainers). Repositório em memória passa na implementação ingênua, o que é pior que não testar.
8. **Uma entrega por vez.** Build verde antes do próximo passo.
9. **Ao concluir qualquer entrega, atualizar `docs/ESTADO.md` E o `README.md`** antes de começar a próxima. No ESTADO.md: seções 4 (implementado), 5 (não implementado), 6 (lacunas), 7 (fila) e 11 (histórico). No README: a tabela "Estado atual da implementação", que precisa ficar coerente com a §4 do ESTADO.md. Esse par é o contrato de sincronização com o ambiente de gestão do projeto, onde vive o quadro Kanban. ESTADO.md desatualizado significa quadro errado; README desatualizado significa entrega existente avaliada como ausente (ver L-09).

---

## Estrutura

```
src/
  PacioliBank.Ledger/              dominio + aplicacao (portas). ZERO pacotes externos
    Domain/                        Money, Currency, Account, LedgerEntry, invariantes
    Application/                   ILedgerStore (porta de saida), comandos, resultados
  PacioliBank.Ledger.Persistence/  adaptador PostgreSQL: Dapper, SQL, transacao, bloqueio
  PacioliBank.Api/                 adaptador HTTP
tests/
  PacioliBank.Domain.Tests/        59 testes, sem I/O
  PacioliBank.Integration.Tests/   38 testes, PostgreSQL real, inclui concorrencia, estorno e extrato
db/init/                           esquema, papeis e privilegios
docs/                              ESTADO, ADRs, diagramas, specs, convencoes, kanban
```

---

## Convenções

| Item | Regra |
|---|---|
| Código | Inglês |
| Documentação e nomes de teste | Português (o teste descreve a regra e é lido por quem não programa) |
| Valor monetário | `decimal` no código, `numeric(19,4)` no banco, **string** na API |
| Instantes | `DateTimeOffset`, UTC, ISO 8601 |
| `occurredAt` vs `recordedAt` | Data do fato vs data do registro. A consulta histórica usa a primeira; a validação de saldo usa a posição corrente (RN-012) |
| Commits | Incrementais, agrupados por decisão. Push único sinaliza ausência de processo, e o enunciado avalia processo |

---

## Armadilhas já encontradas

- `PostgreSqlBuilder()` sem parâmetros é obsoleto no Testcontainers 4.15: a imagem vai no construtor
- `decimal` não é constante válida em `[InlineData]`: passar como string e converter
- Dapper materializa por setter; `init` em DTO privado é risco desnecessário
- Versões de pacote estão **congeladas porque verificadas**, não porque são as mais recentes. Atualizar é item próprio, com verificação própria

---

## Pendência imediata

Cards 19, 19.1, 19.4, 20, 20.1, 21, 22 e 23 concluídos, e todos os requisitos obrigatórios do enunciado atendidos: endpoints de negócio no ar, modelo C4 em Mermaid em `docs/diagrams/`, especificações e ADRs coerentes com o código (EF e BDD na versão 1.1). O próximo da fila é o **24** (despachante de outbox). O quadro no TickTick é a fonte de toda atividade: trabalho sem cartão não começa. Ver `docs/ESTADO.md §7`.

Nenhuma lacuna ALTA aberta: a L-10 foi corrigida no card 19.4, com revisão do ADR-0006.

Antes de começar, rode `dotnet test`. Esperado: 97 passando, sem avisos.
