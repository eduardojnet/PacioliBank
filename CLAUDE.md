# PacioliBank Ledger

Livro-razão de contas correntes em C#: registra movimentações financeiras e responde à posição consolidada de um cliente em qualquer instante, com consistência forte por conta.

Entrega de um desafio técnico de Arquiteto de Software. O avaliador lê o código **e** as decisões.

---

## Comandos

```bash
dotnet build                  # deve terminar sem erro E SEM AVISO
dotnet test                   # 177 testes; os de integração exigem Docker
docker compose up --build     # migrador roda e termina; API em :8080, painel de observabilidade em :18888
docker compose down -v        # só para recomeçar do zero, de propósito
dotnet tool restore && (cd tests/PacioliBank.Domain.Tests && dotnet stryker)   # mutação, ~20 s
```

O esquema é aplicado pelo `PacioliBank.Migrations` (DbUp), num passo separado, com o papel de migração, antes de a API subir (ADR-0002, revisão do card 27). Mudança de esquema é **migração nova** em `db/migrations/NNNN_descricao.sql`; script aplicado não se edita.

---

## Ordem de leitura

1. **O quadro no TickTick** (projeto PacioliBank) — fonte de toda atividade e da ordem. `docs/KANBAN.md` e `ESTADO.md §7` o espelham
2. `docs/ESTADO.md` — estado atual, lacunas por severidade
3. `docs/adr/README.md` — índice das 12 decisões arquiteturais
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
  PacioliBank.Domain.Tests/        98 testes, sem I/O; cobertura e mutacao (Stryker) do dominio >= 85% exigidas no CI
  PacioliBank.Architecture.Tests/  6 regras de dependencia (NetArchTest)
  PacioliBank.Contract.Tests/      2 testes, instantaneo do OpenAPI (openapi.v1.approved.json)
  PacioliBank.Integration.Tests/   71 testes, PostgreSQL real, inclui concorrencia, estorno, extrato, outbox, migracoes, snapshot, log e traco
db/migrations/                     esquema, papeis e privilegios, em migracoes numeradas
db/seed/                           contas de exemplo, so no ambiente local
docs/                              ESTADO, ADRs, diagramas, specs, convencoes, kanban
.github/workflows/ci.yml           CI: build, testes, Insomnia, gitleaks; acoes fixadas por SHA
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

Fila ativa 19 a 25 concluída, com seus subníveis, e todos os requisitos obrigatórios do enunciado atendidos: endpoints de negócio no ar, modelo C4 em Mermaid em `docs/diagrams/`, especificações e ADRs coerentes com o código (EF 1.5, ENF 1.7, BDD 1.2), e painel de evidência na raiz da API. Depois dela, concluídos: 26 (NetArchTest), 27 (migrações DbUp em passo separado), 28 (analisadores em `Recommended`), 29 e 30 (encerrados como decisão registrada), 30.1 (teste do snapshot), 31 (CI no GitHub Actions), 31.1 (auditoria de dependências), 31.2 (cobertura do domínio com limite de 85%), 34 (ferramentas de teste atualizadas; xunit v3 no 34.1, Backlog), 36 (teste de mutação com Stryker, limite de 85%) 33 (log JSON com correlação e mascaramento, ADR-0012) e 33.1 (rastreamento ponta a ponta, painel em :18888). Em A Fazer: 33.2 (métricas). Depois, proposta: 35 (PostgreSQL 18). Ficam no Backlog até o gatilho: 32, 34.1, 37 e 38. A ordem é a do quadro. O quadro no TickTick é a fonte de toda atividade: trabalho sem cartão não começa. Ver `docs/ESTADO.md §7`.

Nenhuma lacuna aberta: a L-13 (RNF-036 declarada sem medição) foi encerrada no card 31.2, a L-14 (ponto de extensão declarado e inexistente na EF §10) no card 29, a L-15 (RNF-003 e RNF-006 dados como realizados sem ressalva) no card 30, e a L-16 (observabilidade dada como realizada sem existir) no card 33.

Antes de começar, rode `dotnet test`. Esperado: 177 passando, sem avisos.
