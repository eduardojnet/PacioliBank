# Registros de Decisão Arquitetural (ADR)

**Projeto:** Sistema de Movimentações Financeiras e Posição Consolidada

Este diretório contém as decisões arquiteturais do projeto, em formato [MADR](https://adr.github.io/madr/). Cada decisão é um arquivo imutável. Decisão superada não é editada: cria-se um novo ADR que a substitui, e o anterior passa a `Superado por ADR-xxxx`.

## Como ler

Comece por [ADR-0001](./ADR-0001-estilo-arquitetural.md). As decisões seguintes dependem dela.

| ADR | Decisão | Status | Dirigido por |
|---|---|---|---|
| [0001](./ADR-0001-estilo-arquitetural.md) | Monolito modular com fronteiras verificadas | Aceito | RNF-035, RNF-037, R-07 |
| [0002](./ADR-0002-plataforma-e-armazenamento.md) | .NET 10 LTS, PostgreSQL, Dapper e DbUp; migrador em passo separado (revisão do card 27) | Aceito, revisado em 2026-10-02 | RNF-001, RNF-037, RNF-038, R-06 |
| [0003](./ADR-0003-ledger-append-only.md) | Ledger append-only como única fonte da verdade | Aceito | RN-003, RNF-003, RNF-016 |
| [0004](./ADR-0004-representacao-monetaria.md) | `decimal` e `numeric(19,4)`, com Value Object `Money` | Aceito | RN-002, EF §8.2 |
| [0005](./ADR-0005-controle-de-concorrencia.md) | Bloqueio pessimista de linha por conta; duas contas em ordem crescente de identificador (revisão do card 38) | Aceito, revisado em 2026-10-05 | RN-001, RNF-004, CQ-02 |
| [0006](./ADR-0006-idempotencia.md) | Chave obrigatória com unicidade no banco | Aceito, revisado em 2026-10-02 | RN-005, RNF-011, CQ-04 |
| [0007](./ADR-0007-snapshot-e-projecao.md) | Snapshot inline amortizado por sequência | Aceito | RNF-002, RNF-003, RNF-006 |
| [0008](./ADR-0008-outbox-transacional.md) | Outbox transacional com entrega ao menos uma vez | Aceito | RF-011, RNF-010, CQ-03 |
| [0009](./ADR-0009-seguranca-e-privilegio-minimo.md) | Privilégio mínimo no banco e resposta opaca | Aceito | RNF-020 a RNF-025, CQ-05 |
| [0010](./ADR-0010-estrategia-de-testes.md) | Pirâmide com banco real e critério de bloqueio | Aceito | RNF-036, BDD §7 |
| [0011](./ADR-0011-painel-de-evidencia.md) | Painel de evidência como página estática | Aceito, condicional | RNF-037, demonstração de RN-001, RN-005, RN-009 |
| [0012](./ADR-0012-observabilidade.md) | Log mascarado no formatador, correlação fora do adaptador HTTP, telemetria por OTLP | Aceito e implementado, revisado em 2026-10-05 | RNF-021, RNF-030, RNF-031, RNF-032 |
| [0013](./ADR-0013-particionamento-do-ledger.md) | Ledger particionado por mês de registro, chaves de unicidade numa tabela não particionada | Aceito e implementado | R-04, RN-004, RN-005, RN-006 |
| [0014](./ADR-0014-transferencia-entre-contas.md) | Transferência numa transação local, contas bloqueadas em ordem de identificador, pernas amarradas pelo banco | Aceito e implementado | RF-012, RN-013, RN-001, RN-005 |

## Especificações de origem

- [BDD: Especificação por Comportamento](../specs/BDD-comportamento.md)
- [Especificação Funcional](../specs/EF-especificacao-funcional.md)
- [Especificação Não Funcional](../specs/ENF-especificacao-nao-funcional.md)

## Regras deste diretório

1. **Uma decisão por arquivo.** Se o título precisa de "e", provavelmente são dois ADRs.
2. **ADR aceito é imutável.** Correções de redação são permitidas; mudança de decisão exige novo ADR.
3. **Alternativas rejeitadas são obrigatórias.** ADR sem alternativa rejeitada é documentação de implementação, não registro de decisão.
4. **Toda decisão rastreia a um requisito.** Decisão sem requisito que a justifique é preferência pessoal disfarçada.
5. **Escopo condicional é declarado como tal.** Decisão com critério de corte objetivo tem status `Aceito, com escopo condicional` e registra o gatilho que a cancela.
6. **Novo escopo exige ADR.** Esta é a defesa contra o risco R-07, a erosão de fronteiras que originou o sistema legado.

Template em [template.md](./template.md).
