# PacioliBank Ledger

Livro-razão de contas correntes em C#: registra movimentações financeiras e responde à posição consolidada de um cliente em qualquer instante, com consistência forte por conta.

Entrega de um desafio técnico de Arquiteto de Software. O avaliador lê o código **e** as decisões.

---

## Comandos

```bash
dotnet build                  # deve terminar sem erro E SEM AVISO
dotnet test                   # 124 testes; os de integração exigem Docker
docker compose up --build     # migrador roda e termina; API em http://localhost:8080
docker compose down -v        # só para recomeçar do zero, de propósito
```

O esquema é aplicado pelo `PacioliBank.Migrations` (DbUp), num passo separado, com o papel de migração, antes de a API subir (ADR-0002, revisão do card 27). Mudança de esquema é **migração nova** em `db/migrations/NNNN_descricao.sql`; script aplicado não se edita.

---

## Ordem de leitura

1. **O quadro no TickTick** (projeto PacioliBank) — fonte de toda atividade e da ordem. `docs/KANBAN.md` e `ESTADO.md §7` o espelham
2. `docs/ESTADO.md` — estado atual, lacunas por severidade
3. `docs/adr/README.md` — índice das 11 decisões arquiteturais
4. `docs/specs/EF-especificacao-funcional.md` — domínio, regras (RN-xxx) e contratos
5. `docs/convencoes-de-nomenclatura.md`
6. `docs/PROCESSO-KANBAN.md` — política do quadro (versão 2.0)

---

## Arquitetura em cinco linhas

- **Monolito modular**, Ports and Adapters, DDD tático no domínio (ADR-0001)
- **Ledger append-only é a única fonte da verdade.** Posição é derivada; snapshot e cache são descartáveis (ADR-0003)
- **A conta é a unidade de serialização.** `FOR NO KEY UPDATE` na linha da conta, antes de ler a posição (ADR-0005)
- **Idempotência obrigatória.** A repetição é reconhecida sob o bloqueio da conta, antes do agregado; a chave primária é a barreira estrutural contra duplicidade; o reenvio recebe o `response_body` gravado, byte a byte (ADR-0006, revisões dos cards 19.4 e 19.5)
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
10. **Nenhum trabalho começa sem cartão no quadro.** O quadro no TickTick é a fonte de toda atividade e da ordem; trabalho descoberto vira cartão, numerado pelo `PROCESSO-KANBAN.md` §4, antes de ser feito. Commits adicionam só os arquivos da entrega, nunca `git add -A`: o usuário edita arquivos em paralelo.

---

## Estrutura

```
src/
  PacioliBank.Ledger/              dominio + aplicacao (portas). ZERO pacotes externos
    Domain/                        Money, Currency, Account, LedgerEntry, invariantes
    Application/                   ILedgerStore (porta de saida), comandos, resultados
  PacioliBank.Ledger.Persistence/  adaptador PostgreSQL: Dapper, SQL, transacao, bloqueio
  PacioliBank.Events/              despachante de outbox (SKIP LOCKED); nao referencia o Ledger
  PacioliBank.Api/                 adaptador HTTP; hospeda o despachante; wwwroot = painel de evidencia
  PacioliBank.Migrations/          migrador DbUp: passo separado, papel de migracao, termina
tests/
  PacioliBank.Domain.Tests/        59 testes, sem I/O
  PacioliBank.Architecture.Tests/  6 regras de dependencia (NetArchTest)
  PacioliBank.Contract.Tests/      2 testes, instantaneo do OpenAPI (openapi.v1.approved.json)
  PacioliBank.Integration.Tests/   57 testes, PostgreSQL real, inclui concorrencia, estorno, extrato, outbox e migracoes
db/migrations/                     esquema, papeis e privilegios, em migracoes numeradas
db/seed/                           contas de exemplo, so no ambiente local
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

Fila ativa 19 a 25 concluída, com os subníveis 18.1, 19.1, 19.2, 19.3, 19.4, 19.5, 20.1, 21.1, 21.2, 24.1 e 24.2, e todos os requisitos obrigatórios do enunciado atendidos: endpoints de negócio no ar, modelo C4 em Mermaid em `docs/diagrams/`, especificações e ADRs coerentes com o código (EF 1.4, ENF 1.1, BDD 1.1), e painel de evidência na raiz da API. Cards 26 (NetArchTest) e 27 (migrações DbUp em passo separado, revisão do ADR-0002) concluídos. Resta em A Fazer o 28 (AnalysisMode); a ordem é a do quadro. O quadro no TickTick é a fonte de toda atividade: trabalho sem cartão não começa. Ver `docs/ESTADO.md §7`.

Nenhuma lacuna ALTA aberta: a L-10 foi corrigida no card 19.4, com revisão do ADR-0006.

Antes de começar, rode `dotnet test`. Esperado: 124 passando, sem avisos.
