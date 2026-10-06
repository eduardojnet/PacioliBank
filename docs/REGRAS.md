# Regras invioláveis

As dez regras que governam o trabalho neste repositório. Os documentos do projeto citam cada uma pelo número ("regra 5").

O texto é o da última versão do `CLAUDE.md` que esteve no repositório (commit `6d2e2d8~1`). Desde 2026-10-05 o `CLAUDE.md` fica só no ambiente local, e as regras passaram a morar aqui (card 43.1, lacuna L-17 no [`ESTADO.md`](./ESTADO.md)).

---

1. **Aviso de compilação é erro.** `TreatWarningsAsErrors` está ativo. Nunca suprimir um aviso para "destravar"; corrigir ou justificar com `NoWarn` comentado.
2. **Nenhuma decisão arquitetural sem ADR**, com alternativas rejeitadas e gatilho de revisão. Um ADR sem alternativa rejeitada é documentação de implementação, não registro de decisão.
3. **Não preencher lacuna de negócio com suposição silenciosa.** Vira questão aberta em `EF §10`, com conduta provisória declarada.
4. **Marcar `[NVI]`** o que não foi verificado diretamente.
5. **Separar sempre implementado de especificado.** Apresentar um como o outro é informação incorreta prestada ao cliente.
6. **Invariante financeira é garantida por constraint e privilégio no banco**, não por disciplina de código. Controle que depende de ninguém errar não é controle.
7. **Teste de invariante de persistência roda contra PostgreSQL real** (Testcontainers). Repositório em memória passa na implementação ingênua, o que é pior que não testar.
8. **Uma entrega por vez.** Build verde antes do próximo passo.
9. **Ao concluir qualquer entrega, atualizar `docs/ESTADO.md` E o `README.md`** antes de começar a próxima. No ESTADO.md: seções 4 (implementado), 5 (não implementado), 6 (lacunas), 7 (fila) e 11 (histórico). No README: a tabela "Estado atual da implementação", que precisa ficar coerente com a §4 do ESTADO.md. Esse par é o contrato de sincronização com o ambiente de gestão do projeto, onde vive o quadro Kanban. ESTADO.md desatualizado significa quadro errado; README desatualizado significa entrega existente avaliada como ausente (ver L-09).
10. **Nenhum trabalho começa sem cartão no quadro.** O quadro no TickTick é a fonte de toda atividade e da ordem; trabalho descoberto vira cartão, numerado pelo [`PROCESSO-KANBAN.md`](./PROCESSO-KANBAN.md) §4, antes de ser feito. Commits adicionam só os arquivos da entrega, nunca `git add -A`: o usuário edita arquivos em paralelo.
