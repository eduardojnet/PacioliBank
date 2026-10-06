# Diagramas de arquitetura

Modelo C4 do PacioliBank Ledger em [Mermaid](https://mermaid.js.org/), renderizado nativamente pelo GitHub. Texto versionado: o diagrama muda no mesmo commit que o código, e a mudança aparece no diff.

| Nível | Diagrama | Pergunta que responde |
|---|---|---|
| C1 | [Contexto](./c1-contexto.md) | Quem usa o sistema e com quem ele conversa? |
| C2 | [Contêineres](./c2-conteineres.md) | Que processos e armazenamentos compõem o sistema? |
| C3 | [Componentes da API](./c3-componentes.md) | Como a API se organiza por dentro, e onde cada regra vive? |
| C4 | [Registro de débito sob concorrência](./c4-sequencia-debito.md) | Em que ordem bloqueio, decisão e gravação acontecem? |
| Dados | [Esquema do ledger (ERD)](./ERD-esquema-ledger.md) | Onde os dados e as regras de negócio moram no banco? |

## Implementado ou especificado

Cada diagrama mostra a **arquitetura-alvo** e declara o **estado atual** de cada elemento. Desenhar como existente o que ainda não existe seria apresentar o especificado como implementado ([regra 5](../REGRAS.md)).

| Convenção | Significado |
|---|---|
| Caixa azul, borda contínua | Implementado e coberto por teste |
| Caixa azul-clara | Parcial: existe, com parte especificada pendente |
| Caixa branca, borda tracejada | Especificado, não implementado |
| Caixa cinza | Sistema externo |
| Seta contínua | Interação implementada |
| Seta tracejada | Interação especificada, não implementada |

Cada diagrama traz, abaixo do desenho, uma tabela com o estado de cada elemento e a evidência no código. A fonte do estado é o [`ESTADO.md`](../ESTADO.md) §4 e §5; havendo divergência, vale o `ESTADO.md`, e o diagrama está desatualizado.

## Relação com o Lucid

Os diagramas nasceram em três documentos do Lucid (C4 detalhado, C4 consolidado e sequência nível 4), que continuam servindo para apresentação. **A fonte de verdade passa a ser esta pasta.** C1 e C2 são transcrição fiel do Lucid, com o estado acrescentado. C3 e C4 foram redesenhados a partir do código, porque o Lucid descreve componentes que o código organizou de outra forma: a tabela de correspondência está em [c3-componentes.md](./c3-componentes.md).

**Atualizado em:** 2026-10-02, após os cards 19 e 19.1.
