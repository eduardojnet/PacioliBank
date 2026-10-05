# Quadro Kanban: PacioliBank

Espelho em texto do quadro mantido no TickTick, que é a **fonte** de toda atividade e da ordem ([`PROCESSO-KANBAN.md`](./PROCESSO-KANBAN.md) 2.0). Em divergência, vale o quadro. Atualizado a cada entrega, junto de [`ESTADO.md`](./ESTADO.md). Política do quadro e convenção de numeração em [`PROCESSO-KANBAN.md`](./PROCESSO-KANBAN.md).

**Data:** 2026-10-04 · **Cartões:** 51 · **Sincronizado com o TickTick em:** 2026-10-04, a partir de leitura direta do quadro

## Distribuição

| Coluna | Cartões | Números |
|---|---|---|
| Não Classificado | 0 | Vazia por decisão. Cartão aqui é falha de triagem, não trabalho pendente. |
| Backlog/Ideias | 7 | 32 a 38 |
| A Fazer | 1 | 31.2 |
| Em Andamento | 0 | Limite de 1 em curso, por decisão. |
| Em Revisão | 0 | |
| Bloqueado | 2 | 29, 30 |
| Concluído | 41 | 01 a 28, 31 e 31.1, mais 18.1, 19.2, 19.3, 19.4, 19.5, 20.1, 21.1, 21.2, 24.1 e 24.2 |

---

## Não Classificado

_Vazia, e esse é o estado correto. Todo item do projeto foi triado para uma coluna com critério. Um cartão aparecer aqui significa que alguém registrou trabalho sem classificar, e a ação é triá-lo, não executá-lo._

---

## Backlog/Ideias

### 32. Implementar daily_balances para consulta histórica

`prioridade: Baixa` · `codigo` · `desempenho`

ADR-0007, evolução especificada e deliberadamente não implementada.

O snapshot é ancorado em sequência (ordem de registro) e a consulta histórica usa occurred_at (ordem do fato). Com lançamento retroativo, as duas divergem, então hoje a consulta histórica agrega pelo índice.

GATILHO DE ADOÇÃO: p99 da consulta histórica ultrapassar o alvo de RNF-002. Construir antes disso é construir contra premissa não validada.

### 33. Adicionar OpenTelemetry e Serilog

`prioridade: Baixa` · `codigo` · `observabilidade`

RNF-030 a RNF-032. Rastreamento distribuído, log estruturado com correlação e métricas de negócio.

MÉTRICA DE MAIOR VALOR DIAGNÓSTICO: a taxa de computedFrom=ledger nas consultas. É indicador ANTECEDENTE: seu crescimento precede degradação de latência em horas ou dias, permitindo agir antes que o cliente perceba.

### 34. Atualizar as dependências NuGet

`prioridade: Baixa` · `codigo`

Versões congeladas porque estão VERIFICADAS com build limpo e suíte verde, não porque são as mais recentes.

Defasagem conhecida: Microsoft.NET.Test.Sdk 17.12.0 contra 18.10.1, coverlet 6.0.2 contra 10.1.0, xunit 2.9.2 contra xunit.v3 4.0.1.

ATENÇÃO: xunit v3 muda o modelo de execução (projeto de teste vira executável). Atualizar é item próprio, com verificação própria, nunca no meio de outra entrega.

### 35. Avaliar a migração para PostgreSQL 18

`prioridade: Nenhuma` · `infra`

O ADR-0002 fixou a 17. A 18 tem suporte até 2030 contra 2029 e traz uuidv7() nativo, que melhoraria a localidade de inserção no índice primário de uma tabela append-only.

[NVI] A disponibilidade de uuidv7() na 18 precisa de confirmação na documentação oficial antes de virar decisão.

EXIGE: revisão do ADR-0002 e uma linha do docker-compose.

### 36. Adicionar teste de mutação com Stryker

`prioridade: Nenhuma` · `teste`

Mede a força das asserções, que é exatamente a lacuna que a métrica de cobertura não cobre. Cobertura alta com asserção fraca é autoengano.

Fora do escopo atual por custo de execução. Evolução natural do ADR-0010.

### 37. Particionar o ledger por tempo

`prioridade: Nenhuma` · `infra` · `desempenho`

Risco R-04 da ENF: o ledger cresce de forma monotônica, nada é apagado. Com as premissas da ENF 3, a ordem de grandeza é de bilhões de linhas por ano.

DEPENDE DE: QA-004 (política de retenção), que é decisão de compliance e jurídico, não de engenharia.

### 38. Incluir transferência entre contas no escopo

`prioridade: Nenhuma` · `arquitetura`

Deliberadamente FORA do escopo atual (EF 3.2). Exige transação abrangendo dois agregados.

POSIÇÃO REGISTRADA (ENF 5.2): quando entrar, a recomendação é saga com lançamento em duas pernas e conta transitória de liquidação, que é o modelo contábil padrão e o único que permanece correto sob particionamento.

EXIGE: novo ADR e revisão do ADR-0001 e do ADR-0005 (ordenação determinística de bloqueios para evitar deadlock).

---

## A Fazer

### 31.2. Tratar a cobertura do domínio abaixo da RNF-036 (L-13)

`prioridade: Média` · `codigo` · `doc`

DESCOBERTO NO CARD 31 (04/10/2026). LACUNA L-13. A ENF §11 declara a RNF-036 (cobertura de linha >= 85% no projeto de domínio) como 'realizada no código', e o ADR-0010 a põe no critério de bloqueio. Nunca tinha sido medida. Medida em 04/10/2026 no projeto PacioliBank.Ledger: 56,8% só com os testes de domínio, 49,9% só com os de integração, 74,9% somando os dois.

DECISÃO DO USUÁRIO, antes de executar:
(a) escrever testes até atingir 85% e ligar o limite no CI; ou
(b) revisar a meta no ADR-0010 e na ENF, com alternativa rejeitada, e ligar o limite revisado; ou
(c) manter a cobertura informativa e declarar a RNF-036 como especificada, não realizada.

CRITÉRIO (qualquer opção): ENF §11 deixa de afirmar o que não é verdade; o número medido fica registrado com o método; se houver limite, o CI reprova abaixo dele (medido).

---

## Em Andamento

_Vazia. Os cards 31 e 31.1 foram concluídos; o próximo é o 31.2._

---

## Em Revisão

_Vazia. Os dois cartões que ocupavam a coluna (21 e 22) foram concluídos em 2026-10-02._

---

## Bloqueado

### 29. Responder às 6 questões de negócio em aberto

`prioridade: Média` · `spec` · `risco`

BLOQUEADO POR: ausência de interlocutor de negócio. No desafio não há cliente para perguntar, então cada uma tem conduta provisória declarada e isolada em ponto de extensão.

QA-001 limite ou cheque especial por conta
QA-002 política de lançamento retroativo
QA-004 retenção e arquivamento do ledger
QA-005 exigência multimoeda
QA-006 volume real: lançamentos por segundo, contas, taxa de leitura
QA-007 modelo corporativo de identidade e escopos

JÁ DECIDIDAS: QA-003 (estorno não negativa a conta) e RN-012 (bitemporalidade mantida).

VALOR NO DESAFIO: este card É a entrega. Demonstra que as lacunas foram identificadas e tratadas como risco declarado, em vez de preenchidas com suposição silenciosa.

### 30. Executar testes de carga e validar os RNF de desempenho

`prioridade: Nenhuma` · `teste`

BLOQUEADO POR: ausência de ambiente de carga e de dados reais de volume (QA-006).

RNF-001, RNF-002, RNF-005 e RNF-007 estão especificados com métrica na ENF, mas declarados como NÃO verificados na seção 11 da ENF.

DECISÃO JÁ TOMADA: fora do escopo do desafio. Declarar como especificado e não implementado é parte da entrega.

---

## Concluído

### 01. Analisar o enunciado e definir a estratégia de entrega

`prioridade: Média` · `analise`

Leitura do desafio, identificação do que de fato é avaliado e calibração do escopo ao teto de 16h a 24h.

ENTREGUE: análise completa com riscos, premissas e alternativas. Conclusão central: o enunciado avalia capacidade de identificar o ponto de contenção e eliminá-lo, não volume de tecnologia.

### 02. Especificar o comportamento em Gherkin (BDD)

`prioridade: Média` · `doc` · `spec`

10 funcionalidades, ~55 cenários em português, com tags de rastreabilidade para RF, RN e RNF.

ARQUIVO: docs/specs/BDD-comportamento.md
CRITÉRIO ATENDIDO: todo requisito tem ao menos um cenário de aceite.

### 03. Escrever a Especificação Funcional

`prioridade: Média` · `doc` · `spec`

Modelo de domínio, 12 regras de negócio, 11 requisitos funcionais, 3 casos de uso, contrato de API e catálogo de erros.

ARQUIVO: docs/specs/EF-especificacao-funcional.md
DISCIPLINA: tudo além dos 3 requisitos do enunciado está marcado [INFERIDO] com justificativa.

### 04. Escrever a Especificação Não Funcional

`prioridade: Média` · `doc` · `spec`

7 atributos de qualidade priorizados, 30 RNF com métrica e método de verificação, 7 cenários SEI, 7 riscos.

ARQUIVO: docs/specs/ENF-especificacao-nao-funcional.md
PREMISSAS de capacidade declaradas como premissas, não como fato (QA-006).

### 05. Registrar as decisões arquiteturais (11 ADRs em MADR)

`prioridade: Alta` · `doc` · `arquitetura`

ADR-0001 a ADR-0011, cada um com contexto, critérios definidos antes da avaliação, alternativas rejeitadas com a condição que as reabriria, validação e gatilho de revisão.

DIRETÓRIO: docs/adr/
DESTAQUE: ADR-0005 (concorrência) registra uma revisão de posição da análise preliminar, com justificativa.

### 06. Definir nome do produto e convenções de nomenclatura

`prioridade: Baixa` · `doc`

PacioliBank, em referência a Luca Pacioli (partidas dobradas, 1494), alinhado ao ledger append-only do ADR-0003.

ARQUIVO: docs/convencoes-de-nomenclatura.md
DECISÃO: não usar a marca da empresa avaliadora em repositório público.

### 07. Criar o esqueleto da solution e o ambiente local

`prioridade: Média` · `codigo` · `infra`

Solution .NET 10 com 5 projetos, Directory.Build.props com TreatWarningsAsErrors, docker-compose, esquema do banco com 5 tabelas e 3 papéis de privilégio mínimo.

CRITÉRIO ATENDIDO: docker compose up em um comando, build sem avisos.

### 08. Implementar Money e Currency com testes

`prioridade: Média` · `codigo` · `dominio`

Value Object com moeda embutida, construtor privado, escala validada na criação. Operação entre moedas distintas é impossível por construção.

10 métodos de teste, 18 casos. ADR-0004.

### 09. Implementar o agregado Account e as invariantes

`prioridade: Alta` · `codigo` · `dominio`

Account, LedgerEntry, PostingRequest, enums e as exceções de domínio. O agregado não carrega os lançamentos da conta: é reidratado sob bloqueio com a posição já calculada.

21 testes cobrindo RN-001, RN-002, RN-004, RN-005, RN-006, RN-007 e RN-008.

CORREÇÃO 02/10: este cartão dizia '6 exceções'. São 12, contadas por leitura direta de LedgerDomainException.cs: UnsupportedCurrency, InvalidMoneyScale, CurrencyMismatch, AccountInactive, InvalidEntryAmount, MissingIdempotencyKey, InsufficientFunds, EntryNotFromThisAccount, CannotReverseReversal, AccountNotFound, IdempotencyConflict, LedgerUnavailable.

AINDA AUSENTES, previstas pela EF 8.6 e pendentes no item de endpoints: EntryNotFoundException, PageSizeExceededException, InvalidPointInTimeException.

### 10. Implementar a persistência com bloqueio por conta

`prioridade: Alta` · `codigo` · `persistencia`

PostgresLedgerStore e LedgerSql. A ordem dos passos dentro da transação é a arquitetura: bloqueia a linha da conta, depois lê a posição, o agregado decide, e lançamento, sequência, idempotência e outbox são gravados juntos.

Projeto separado do domínio: a inversão de dependência é física, garantida pelo compilador.

### 11. Escrever os testes de integração e concorrência

`prioridade: Alta` · `teste`

18 testes contra PostgreSQL real via Testcontainers. Inclui o cenário F07 (50 débitos simultâneos sobre saldo para 10) e a verificação de que o papel da aplicação não consegue alterar nem excluir lançamento.

Barreira de sincronização para liberar as tarefas no mesmo instante.
RESULTADO: 57 testes verdes no total.

### 12. Produzir os diagramas C4 no Lucid

`prioridade: Média` · `doc` · `diagrama`

Três documentos: C4 detalhado (C1, C2, C3 em 3 páginas), diagrama de sequência nível 4 e C4 consolidado (C1 a C4 em uma página).

Validados antes da criação: zero erros estruturais.
ATENÇÃO: estão FORA do repositório. Ver card 'Converter diagramas para Mermaid'.

### 13. Criar o documento de estado do projeto

`prioridade: Média` · `doc`

docs/ESTADO.md: documento vivo com o que existe, o que falta, 6 lacunas por severidade, fila de execução e bloco de retomada para nova sessão.

Seção 5 lista explicitamente o que NÃO está implementado.

### 14. Migrar o ciclo de desenvolvimento para Claude Code

`prioridade: Alta` · `processo`

CONCLUÍDO em 02/10/2026.

MOTIVO: no ambiente anterior não havia shell na máquina, então todo build dependia de execução manual e cada erro custava um ciclo completo.

ENTREGUE: Claude Code autenticado pela assinatura, CLAUDE.md na raiz com comandos, arquitetura, 9 regras invioláveis e armadilhas conhecidas.

GANHO JÁ COMPROVADO: a primeira verificação no novo ambiente detectou que dois arquivos dados como escritos NÃO existiam no disco (lacuna L-07) e identificou a lacuna L-08. Nenhuma das duas teria sido vista sem leitura direta do repositório.

### 15. Inicializar o repositório git com histórico incremental

`prioridade: Média` · `infra` · `processo`

CONCLUÍDO em 02/10/2026. Histórico agrupado por decisão, não por arquivo.

BASE (7):

1. chore: configuracao base da solucao
2. feat(db): esquema do ledger, papeis e privilegios
3. feat(domain): Money, Account e lancamento imutavel
4. feat(persistence): adaptador PostgreSQL com bloqueio por conta e idempotencia
5. feat(api): host HTTP com verificacoes de vida e prontidao
6. docs: ADRs 0001 a 0011, especificacoes e convencoes
7. docs: README, estado do projeto e quadro kanban

ACRESCENTADOS:

8. docs: registra o repositorio publico e encerra L-01
9. docs: corrige o estado declarado no README e amplia a regra 9 (L-09)
10. docs: versiona o CLAUDE.md com a regra 9 ampliada (L-09)
11. Atualizando CLAUDE.md no .gitignore
12. docs: define a politica do quadro kanban e as decisoes de processo
13. docs: registra a convencao de numeracao do quadro kanban

DECISÃO REVERTIDA em 02/10: o CLAUDE.md SAIU do .gitignore e passou a ser versionado. Registrado na §3 do ESTADO.md. `.claude/` permanece ignorado: contém permissões locais de execução.

DÍVIDA ACEITA: a mensagem do commit 11 está fora da convenção (Conventional Commits). NÃO reescrever: histórico publicado é imutável, pelo mesmo princípio do ADR-0003.

ATUALIZAÇÃO 02/10: a pendência de push registrada no quadro foi resolvida. Commits 12 e 13 enviados; `origin/main` em `99924c4`, igual ao HEAD local.

### 16. Criar o repositório público no GitHub

`prioridade: Alta` · `risco` · `requisito-obrigatorio`

CONCLUÍDO em 02/10/2026. Antecipado ao item 1 da fila.

ENTREGUE: <https://github.com/eduardojnet/PacioliBank>, público, branch main, README renderizando.

[NVI] Que cada um dos 7 commits iniciais compile isoladamente não foi verificado. Risco baixo no desafio: o avaliador lê o agrupamento, não faz bisect.

ATUALIZAÇÕES (append-only): o `CLAUDE.md` saiu do `.gitignore` e está versionado; a divergência do README foi corrigida e publicada (L-09, card 17). Repositório público sincronizado com o disco em `99924c4`.

### 17. Corrigir a tabela de estado do README

`prioridade: Alta` · `doc` · `risco` · `requisito-obrigatorio`

CONCLUÍDO E PUBLICADO em 02/10/2026. Lacuna L-09, registrada e encerrada no mesmo ciclo.

O README publicado declarava como PENDENTE o que estava implementado e testado: agregado Conta, idempotência, controle de concorrência e testes de integração. A árvore de estrutura omitia Persistence e Integration.Tests.

APLICADO E VERSIONADO:

1. Tabela de estado reescrita com 9 linhas separando implementado de pendente
2. Árvore de estrutura atualizada para os 5 projetos reais
3. Comando de teste declarando 57 testes e o pré-requisito de Docker
4. Promessa de DbUp 'na próxima entrega' trocada por item da fila
5. docs/ESTADO.md: L-09 em §6, histórico em §11
6. CLAUDE.md regra 9 ampliada: agora cobre README, não só ESTADO.md

CONTROLE ADOTADO: nenhuma entrega fecha com README divergente da §4 do ESTADO.md. É correção de causa, não de sintoma.

### 18. Definir a política do quadro Kanban

`prioridade: Média` · `doc` · `processo`

CONCLUÍDO em 02/10/2026. Artefato: docs/PROCESSO-KANBAN.md, versionado no repositório.

MOTIVO: o quadro é a fila de execução do projeto, e fila sem critério de entrada e saída é lista de desejos. As duas lacunas mais caras do projeto nasceram exatamente aqui: L-07 (cartão concluído com código ausente do disco) e L-09 (documento público divergente do estado real).

REGRAS QUE PASSAM A VALER:

- Nenhum cartão vai para Concluído sem verificação no disco
- Concluído é append-only: erro corrige-se acrescentando, nunca apagando (mesmo princípio do ADR-0003)
- Não Classificado permanece vazia
- Falta de tempo não é bloqueio: é priorização, vai para Backlog
- Antecipação de item da fila exige motivo registrado no cartão

COMMITS: 12 (política, `4e28487`) e 13 (convenção de numeração, `99924c4`), ambos publicados.

### 19. Implementar a porta de entrada e os endpoints HTTP

`prioridade: Alta` · `codigo` · `arquitetura` · `requisito-obrigatorio`

LACUNA L-03. O hexágono existe só no lado dirigido: ILedgerStore é porta de saída e está bem feita, mas NÃO EXISTE porta de entrada. Quem chama o store é o teste de integração, diretamente.

ESCOPO AMPLIADO em 02/10 (absorve L-07):

1. Registrar NpgsqlDataSource e ILedgerStore na injeção de dependência (a API hoje nem lê a connection string).
2. Criar a porta de entrada, deixando o endpoint como adaptador HTTP fino. O cálculo do fingerprint é regra do ADR-0006 e não pertence ao adaptador.
3. Escrever os tipos de comando: PostingCommand, ReversalCommand, StatementQuery, StatementEntry, StatementPage. Foram dados como escritos na revisão anterior e NÃO existem no disco (L-07).
4. Escrever as exceções faltantes: EntryNotFoundException, PageSizeExceededException, InvalidPointInTimeException. Mesma situação.
5. Endpoints: credits, debits, reversals, balance, entries.
6. Mapear exceções de domínio para ProblemDetails com os códigos da EF 8.6.
7. Ler o header Idempotency-Key.

CRITÉRIO: build sem avisos, 57 testes ainda verdes, docker compose up servindo os endpoints, e um crédito seguido de consulta de posição funcionando via curl.

ENTREGA (02/10/2026, commit `feat(api)` deste card):

1. Injeção de dependência em `Program.cs`, a raiz de composição; connection string lida de `ConnectionStrings:Ledger`
2. Porta de entrada `ILedgerService` / `LedgerService`; endpoint como adaptador fino
3. `Commands.cs` com os 5 tipos (L-07)
4. As 3 exceções faltantes, mais `EntryAlreadyReversedException`
5. Endpoints credits, debits, reversals, balance, entries
6. `LedgerProblems`: um único mapa de exceção para problem+json com `code` da EF §8.6
7. `Idempotency-Key` lido; repetição responde 200 com `Idempotency-Replayed: true`

VERIFICAÇÃO: build sem avisos; 94 testes verdes (eram 57); `docker compose up --build` servindo os endpoints; crédito seguido de consulta de posição via curl, mais 20 cenários de erro.

ACHADOS DO CURL, que a suíte não via: corpo da repetição diferente do original (precisão de instante) e estorno duplicado saindo como saldo insuficiente (BDD F06). Corrigidos, com teste que reprova sem a correção.

REGISTRADO: L-11 (6 decisões de contrato provisórias, sem respaldo na EF). Sem autenticação: RF-009 segue pendente.

### 19.1. Implementar o caminho de persistência do estorno

`prioridade: Alta` · `codigo` · `risco` · `persistencia`

LACUNA L-08, ALTA. Descoberta na primeira verificação feita pelo Claude Code, por leitura direta do código.

O domínio tem Account.Reverse, que valida titularidade e proíbe estorno de estorno (RN-004). A PERSISTÊNCIA NÃO O USA: PostgresLedgerStore só expõe PostAsync, que chama Account.Post.

CONSEQUÊNCIAS VERIFICADAS NA LEITURA:

1. Não há leitura do lançamento original dentro da transação, então EntryNotFromThisAccountException e CannotReverseReversalException nunca são lançadas fora dos testes de domínio.
2. Um PostingRequest com ReversalOf preenchido seria gravado SEM essas validações. A FK reversal_of garante apenas que o original existe: não que pertence à conta, não que não é estorno, não que sentido e valor são os opostos.
3. A violação de uq_entries_reversal (estorno duplicado) sai como PostgresException crua. Pela EF 8.6 deveria ser ENTRY_ALREADY_REVERSED (409).

Nenhum teste de integração cobre estorno hoje.

CRITÉRIO: ReverseAsync no store, dentro da mesma transação e sob o mesmo bloqueio do ADR-0005, com teste contra PostgreSQL real cobrindo os três casos acima.

Pode ser feito junto do item de endpoints, mas é verificável separadamente.

ENTREGA (02/10/2026, commit `4c71b3f`): `ReverseAsync` no store, sob o mesmo bloqueio por conta. 9 testes de integração (os três casos do critério, F06 completo e 10 estornos simultâneos do mesmo lançamento, dos quais exatamente 1 é aceito). L-08 encerrada.

ACHADO NA ENTREGA: L-10, reenvio idempotente recusado quando o saldo mudou. Verificado por execução e registrado; exige revisão do ADR-0006.

### 20. Converter os diagramas C4 para Mermaid no repositório

`prioridade: Alta` · `doc` · `requisito-obrigatorio`

LACUNA L-02, BLOQUEANTE PARCIAL. O enunciado exige toda a documentação no próprio repositório. Hoje quem clona o repo não vê diagrama algum: eles vivem no Lucid.

SOLUÇÃO: recriar em Mermaid sob docs/diagrams/. O GitHub renderiza nativamente, o arquivo é versionável e diffável, e não depende de conta no Lucid.

SEGUNDA PARTE: os diagramas mostram componentes que ainda não existem (painel, despachante, endpoints, autorização). Legítimo como arquitetura-alvo, desonesto como estado atual. Cada diagrama precisa de nota distinguindo implementado de especificado.

CRITÉRIO: diagramas visíveis ao clonar o repositório, com o estado de cada componente declarado.

ENTREGA (02/10/2026): quatro diagramas em `docs/diagrams/` (C1 contexto, C2 contêineres, C3 componentes da API, sequência do débito), mais um README com a convenção visual. Cada diagrama tem tabela de estado por elemento, com evidência no código; o especificado aparece com borda e seta tracejadas.

VERIFICAÇÃO: os quatro renderizados localmente com `mermaid-cli` e conferidos visualmente antes do commit; o C3 foi reorganizado duas vezes até eliminar sobreposição.

DIVERGÊNCIA REGISTRADA: o C3 do Lucid não corresponde ao código. O do repositório segue o código, com tabela de correspondência. L-02 encerrada; o requisito obrigatório "documentação no repositório" passa a atendido.

### 21. Aplicar as correções documentais pendentes

`prioridade: Média` · `doc` · `risco`

LACUNA L-05. Quatro divergências já identificadas e registradas, ainda não aplicadas nos documentos de origem:

1. EF 8.6 e BDD F09: 403 para conta de terceiro revela a existência da conta; deve ser 404 para cliente final (registrado no ADR-0009).
2. ADR-0001: módulo Integration renomeado para Events, por colisão com os testes de integração.
3. ADR-0010: projetos de teste com prefixo Ledger., substituído por PacioliBank.
4. ADR-0009: papéis app_* nos exemplos, substituídos por pacioli_*.

CRITÉRIO: os quatro documentos coerentes entre si e com o código.

ESCOPO AMPLIADO em 02/10, com motivo: o critério pede coerência "com o código", e a verificação achou mais 3 divergências da mesma natureza (ADR-0010 sem Reqnroll nem projeto de concorrência no código; ADR-0001 e convenções com módulos inexistentes; EF §8.4 com URL em vez de URN). Mais a L-11, cujo destino proposto era este card. Total de 7 itens, dentro do limite da política.

ENTREGA (02/10/2026): as 4 correções aplicadas, cada uma com nota de revisão no documento de origem. As 3 divergências novas tratadas com nota "Estado da implementação", sem alterar decisão. L-11 incorporada à EF: §8.7, códigos `ENTRY_NOT_FOUND` e `INVALID_REQUEST`, QA-008 (tamanho de página). EF e BDD passam à versão 1.1. L-05 e L-11 encerradas.

VERIFICAÇÃO: busca no repositório por `app_`, `FORBIDDEN`, `Integration` como módulo, prefixo `Ledger.` e `api.banco.example`; as ocorrências restantes são as próprias notas de revisão.

### 22. Verificar o poder de detecção do teste de concorrência

`prioridade: Alta` · `teste` · `risco`

LACUNA L-04. O ADR-0010 e o próprio ConcurrencyTests afirmam que o teste deve falhar contra implementação sem bloqueio. Isso NUNCA foi medido.

EXPERIMENTO: remover FOR NO KEY UPDATE do LedgerSql, rodar só a suíte de concorrência, restaurar.

DUAS HIPÓTESES DECLARADAS ANTES DO RESULTADO:
(a) falha: o bloqueio sustenta RN-001 e o teste enxerga;
(b) passa: a constraint mais a nova tentativa funcionam como controle otimista, a defesa em profundidade é real, e o critério que escrevi está errado e precisa ser corrigido.

CRITÉRIO: saber qual hipótese é verdadeira e registrar o resultado no ADR-0005 como validação empírica. Custo: 2 minutos.

ENTREGA (02/10/2026): o teste REPROVA sem o bloqueio, mas por perda de disponibilidade (24% a 78% dos comandos com `503` por tentativas esgotadas), não por saldo negativo. Sem o bloqueio, a constraint de sequência mais a nova tentativa preservaram a invariante: nenhuma posição negativa, nenhuma lacuna. Nenhuma das duas hipóteses estava certa como escrita. Registrado no ADR-0005 como validação empírica.

### 19.4. Corrigir a repetição idempotente recusada quando o estado da conta mudou

`prioridade: Alta` · `codigo` · `risco` · `arquitetura`

ÂNCORA: lacuna L-10 (ALTA), RN-005, ADR-0006. Criado em 02/10 como subnível do 19, porque o defeito está no caminho de idempotência que o card 19 expôs pela API; a faixa da fila ativa não tinha número livre. Antecipado ao 23 por decisão do usuário.

SITUAÇÃO: o reenvio de um débito já efetivado, depois de o saldo cair, recebia 422 em vez do resultado original.

ENTREGA (02/10/2026): repetição reconhecida sob o bloqueio da conta, antes do agregado; violação de chave mantida como segunda barreira. ADR-0006 revisado com 3 alternativas rejeitadas e gatilho de revisão. 3 testes de integração novos, que reprovaram antes da correção. 97 verdes. Cenário original reproduzido via curl: agora 200 com corpo idêntico. Diagrama de sequência redesenhado.

### 23. Finalizar o README

`prioridade: Alta` · `doc` · `requisito-obrigatorio`

Requisito obrigatório. Item 6 da fila: revisão final, DEPOIS dos endpoints.

ESCOPO SEPARADO em 02/10: a correção urgente da tabela de estado saiu deste card e virou 'Corrigir a tabela de estado do README', priorizada à frente porque o repositório já está público com informação errada. Este card fica com o que só faz sentido no fim.

PENDENTE AQUI:

1. Endpoints documentados com exemplos curl executáveis
2. Decisões resumidas com link para os ADRs
3. Seção 'o que seria feito com mais tempo', que o enunciado pede explicitamente
4. Instrução de execução dos testes de integração e seu pré-requisito (Docker)

CRITÉRIO: alguém que nunca viu o projeto sobe o ambiente e faz um lançamento seguindo apenas o README.

ENTREGA (02/10/2026): os 4 itens pendentes feitos (endpoints com curl executável, decisões com link para os ADRs, "o que seria feito com mais tempo", testes e o pré-requisito Docker), mais início em cinco minutos e tabela de erros. Resumos do ADR-0005 e do ADR-0006 atualizados para o que foi medido e revisado.

VERIFICAÇÃO DO CRITÉRIO: README seguido literalmente num clone limpo: subida, crédito, posição, débito, reenvio, recusa por saldo, posição histórica, extrato e estorno, todos com a resposta descrita. Achado e corrigido: "rodar sem Docker" falharia como estava escrito (sem `ASPNETCORE_ENVIRONMENT=Development` não há connection string); a instrução nova foi executada e funciona.

### 20.1. Gerar o Diagrama de Entidade e Relacionamento em Mermaid

`prioridade: Alta` · `doc` · `requisito-obrigatorio`

ÂNCORA: mesmo item 3 da fila (ESTADO.md 7, diagramas no repositório) que o cartão 20. Complementa a L-02: o C4 descreve a arquitetura, o ERD descreve o esquema de dados, e nenhum dos dois substitui o outro.

SITUAÇÃO: o esquema existe e está implementado em db/init/001_roles_and_schema.sql, com 5 tabelas, mas não há representação visual dele em lugar algum. Nem no Lucid, nem no repositório. Quem lê o desafio precisa abrir o SQL e montar o modelo de cabeça.

ESCOPO, as 5 tabelas do schema ledger:

1. accounts: PK account_id; CHECK ck_accounts_status (1,2,3) e ck_accounts_sequence (>= 0)
2. ledger_entries: PK entry_id; FK account_id -> accounts; FK reversal_of -> ledger_entries (AUTORRELACIONAMENTO, o estorno aponta para o lançamento original); UNIQUE uq_entries_sequence (account_id, sequence) por RN-006; UNIQUE uq_entries_idempotency (account_id, idempotency_key) por RN-005; UNIQUE uq_entries_reversal (reversal_of) por RN-004; CHECK amount > 0 por RN-002 e direction IN (1,-1)
3. balance_snapshots: PK COMPOSTA (account_id, up_to_sequence); FK account_id -> accounts
4. idempotency_records: PK COMPOSTA (account_id, idempotency_key); FK account_id -> accounts; FK entry_id -> ledger_entries
5. outbox_messages: PK message_id; sem FK, por desenho (ADR-0008: a outbox não conhece o domínio que a alimenta)

CADA ENTIDADE com atributos, tipo, PK, FK e cardinalidade declarada. Sintaxe erDiagram do Mermaid, renderizada nativamente pelo GitHub.

QUATRO PONTOS QUE O ERD TEM DE TORNAR VISÍVEIS, e que são o valor real deste cartão:

1. O autorrelacionamento de ledger_entries, com UNIQUE em reversal_of: é o que materializa 'cada lançamento admite no máximo um estorno' (RN-004) como garantia estrutural, não como regra de código
2. A PK composta de balance_snapshots: snapshot é ancorado em SEQUÊNCIA, não em data. Isso explica por que a consulta histórica por occurred_at não o usa (ADR-0007)
3. A ausência de FK em outbox_messages é DELIBERADA, não esquecimento. Precisa de nota no diagrama, ou será lida como defeito
4. A redundância aparente entre uq_entries_idempotency e a tabela idempotency_records: são controles em níveis diferentes (ADR-0006), e um ERD sem nota faz parecer duplicação

ARQUIVO: docs/diagrams/ERD-esquema-ledger.md

CRITÉRIO DE CONCLUSÃO: o diagrama renderiza no GitHub; as 5 tabelas, todas as PK, todas as FK e as 3 constraints UNIQUE estão representadas; cada um dos 4 pontos acima tem nota explicativa; e o conteúdo confere com db/init/001_roles_and_schema.sql lido linha por linha, não de memória.

RISCO DE NÃO EXECUTAR: o esquema é onde as invariantes financeiras moram de fato (ADR-0009: imutabilidade por privilégio, não por disciplina). Sem ERD, o avaliador precisa ler SQL para ver que as regras de negócio estão no banco, e a decisão arquitetural mais forte do projeto fica ilegível para quem não abre o arquivo.

NOTA SOBRE A NUMERAÇÃO: este cartão é 20.1 por compartilhar o item 3 da fila e o diretório de destino com o 20, não por depender dele. Pode ser executado antes, depois ou junto. A regra de subnível do PROCESSO-KANBAN 4 dizia apenas 'dependência real': fica emendada aqui para admitir também 'mesmo item da fila'.

ATUALIZAÇÃO 02/10/2026: criado no Cowork e incluído neste espelho e no ESTADO.md §7 na sincronização seguinte. A emenda de subnível que ele cita já está no PROCESSO-KANBAN.md 1.2 §4.

ENTREGA (02/10/2026): `docs/diagrams/ERD-esquema-ledger.md`, ligado no índice da pasta. 5 tabelas, 40 colunas com tipo, 5 PK (duas compostas), 5 FK, 3 UNIQUE, 5 CHECK, mais índices e privilégios do papel da aplicação. As 4 notas pedidas.

VERIFICAÇÃO: além da leitura do script, comparação automática com o catálogo do PostgreSQL (`information_schema.columns`, `pg_constraint`): nome, tipo e ordem das 40 colunas idênticos. A comparação pegou dois erros de contagem na primeira versão (39 colunas, 4 PK), corrigidos. Renderização validada com mermaid-cli; o primeiro layout fazia a outbox parecer ligada a `accounts` e `ledger_entries`, exatamente a leitura que a nota 3 quer evitar, e foi corrigido.

ACHADO: a nota 3 do cartão atribuía ao ADR-0008 a justificativa da ausência de FK na outbox; o ADR não a contém. O ERD declara o fato e marca a justificativa como `[INFERIDO]`. Registrado como L-12, para decisão.

### 24. Implementar o despachante de outbox

`prioridade: Média` · `codigo`

ADR-0008. A tabela e a gravação transacional já existem; falta o processo que lê e publica.

Consumo com FOR UPDATE SKIP LOCKED permite múltiplos despachantes em paralelo sem coordenação externa. Recuo exponencial em falha de publicação.

O barramento concreto não é decidido: a decisão arquitetural é o padrão, não o produto.

ATUALIZAÇÃO 02/10/2026: movido de Backlog para A Fazer por decisão do usuário. É o item 7 da fila do ESTADO.md §7, e o número está na faixa da fila ativa. Próximo a executar depois do 20.1.

CRITÉRIO ACRESCENTADO AO INICIAR (o cartão não tinha): despachante hospedado na API, `SKIP LOCKED`, publicação por abstração com implementação em log, recuo exponencial, limite de tentativas com alerta; testes contra PostgreSQL real de publicação, retomada com `message_id` estável (F08), paralelismo sem duplicidade e estacionamento; no Docker, crédito via curl publicado. Fora do escopo: expurgo, barramento real, conciliação (RNF-033).

ENTREGA (02/10/2026): módulo `PacioliBank.Events` (sem referência ao `Ledger`), `OutboxDispatcherService` e `LoggingEventPublisher` na API. 5 testes de integração, 102 verdes. O teste de paralelismo reprova quando `FOR UPDATE SKIP LOCKED` é removido (medido). No Docker, crédito via curl publicado em menos de 3 s, com `published_at` preenchido e registro no log. ADR-0008 com nota de implementação (ordem por sequência, estacionamento sem coluna nova); C1, C2 e C3 atualizados.

### 24.1. Decidir e registrar a ausência de chave estrangeira na outbox

`prioridade: Baixa` · `arquitetura` · `doc`

ÂNCORA: lacuna L-12 (BAIXA), ADR-0008, regra 2 do CLAUDE.md. Criado em 02/10 no card 20.1, quando a justificativa atribuída ao ADR-0008 não foi encontrada nele.

ESCOPO: decisão do usuário (manter sem FK ou acrescentar); registrar no ADR-0008 com alternativa rejeitada; ajustar a nota 3 do ERD.

CRITÉRIO: ADR-0008 com a decisão e a alternativa rejeitada; ERD e ADR coerentes; L-12 encerrada.

BLOQUEIO: depende de decisão do usuário.

ENTREGA (02/10/2026): decisão do usuário, acrescentar a chave. Aplicada como `fk_outbox_entry (account_id, sequence)` para `ledger_entries`, que garante o lançamento e, por ele, a conta. ADR-0008 com a decisão e três alternativas rejeitadas. Teste novo com o papel da aplicação: mensagem sem lançamento é recusada (`23503`); reprovou antes da mudança. 103 verdes. ERD reconferido contra o catálogo, com 6 FK. Verificado no Docker após `down -v`: crédito gravado e publicado. L-12 encerrada.

### 18.1. Inverter a política: o quadro passa a ser a fonte da fila

`prioridade: Média` · `processo` · `doc`

ÂNCORA: decisão do usuário em 02/10/2026, "todas as atividades deveriam estar no kanban, sendo ele a fonte de tudo". Altera o card 18. Escopo: PROCESSO-KANBAN 2.0 com o quadro como fonte da fila e de toda atividade; ESTADO §7 e KANBAN.md como espelhos; o disco continua fonte da verdade do código. CRITÉRIO: nenhum documento afirma mais que a ordem vem do ESTADO.md.

ENTREGA (02/10/2026): `PROCESSO-KANBAN.md` 2.0: o quadro é a fonte de toda atividade e da ordem; `ESTADO.md §7` e este espelho em divergência perdem para o quadro. Regras novas: nenhum trabalho sem cartão; o bloco de trabalho começa lendo o quadro; commits só com os arquivos da entrega. Histórico de versões da política acrescentado. Ajustados: `ESTADO.md` §6-A, §7 e bloco de retomada; cabeçalho deste espelho; README; ordem de leitura e regra 10 do `CLAUDE.md`. VERIFICAÇÃO: busca no repositório por afirmações de que a ordem vem do `ESTADO.md`; as restantes são histórico de cartão, preservado pela regra append-only.

### 21.1. Alinhar os nomes dos eventos da EF §9 ao código

`prioridade: Média` · `doc` · `risco`

ÂNCORA: EF §9, convenções §8, ADR-0008; achado no card 24. A EF nomeia `LedgerEntryRecorded`, `EntryReversed` e `eventId`; o código grava `pacioli.ledger.entry-recorded.v1`, `pacioli.ledger.entry-reversed.v1` e usa `message_id`. Subnível do 21 por ser correção documental; à frente do 25 por prioridade. CRITÉRIO: EF, convenções, ADR-0008 e código com os mesmos nomes.

ESCOPO AMPLIADO, com motivo: a ENF R-06 descrevia acoplamento por bloqueio consultivo, opção rejeitada no ADR-0005; mesma natureza e mesma tabela que a R-05, que este cartão já corrigia.

ENTREGA (02/10/2026): EF 1.2 (§9 com `pacioli.ledger.entry-recorded.v1`, `pacioli.ledger.entry-reversed.v1` e `message_id`), ENF 1.1 (R-05 e R-06), convenções §8 (o identificador é `message_id`; não existe `eventId`). Cada mudança com nota de revisão. VERIFICAÇÃO: busca no repositório pelos nomes antigos; restam só as notas de revisão.

ACHADO, item 2 do escopo: lendo eventos reais na outbox, o payload é o resultado da API serializado como está, com cinco defeitos (valor como número JSON, internos do `Money`, sentido numérico, `+00:00` em vez de `Z`, estorno sem `reversalOf`). Declarados na EF §9 e transformados no card 24.2.

### 24.2. Definir e implementar o contrato do payload dos eventos

`prioridade: Média` · `codigo` · `risco` · `arquitetura`

ÂNCORA: EF §9, EF §8.2, ADR-0008; achado no card 21.1. O payload é o resultado da API serializado como está: valor como número JSON, internos do `Money` (`isZero`, `scale`...), sentido como número, instantes com `+00:00`, estorno sem `reversalOf`. Impacto hoje baixo (nenhum consumidor externo). ESCOPO: payload definido na EF §9; tipo de evento próprio no código; teste que confere o payload gravado; ajustar a v1, sem consumidor a migrar. CRITÉRIO: payload gravado igual ao definido, verificado contra PostgreSQL real.

ENTREGA (02/10/2026): tipo `LedgerEntryEvent` na aplicação do Ledger, separado do resultado da API; `WireFormat` como dono único do formato de instante, usado pela API e pelos eventos. Payload: valores como string na escala da moeda, instantes em `Z`, sentido `Credit`/`Debit`, `reversalOf` só no estorno, nenhum outro campo. EF 1.3, §9: tabela de campos, exemplo e o motivo de manter `v1`. VERIFICAÇÃO: 2 testes leem o payload gravado na outbox e conferem o conjunto exato de campos e o formato; reprovaram antes da mudança. 105 verdes. No Docker, crédito e estorno reais gravados e publicados no formato especificado.

ACHADO: o ADR-0006 afirma devolver a resposta gravada em `response_body`, "não uma reconstrução"; o código nunca lê a coluna e reconstrói do ledger. Virou o card 19.5.

### 19.5. Resolver a divergência entre o ADR-0006 e o uso de response_body

`prioridade: Baixa` · `arquitetura` · `doc`

ÂNCORA: ADR-0006, consequências; achado no card 24.2. `idempotency_records.response_body` é gravado e nunca lido: a repetição é reconstruída de `ledger_entries`. O corpo devolvido é idêntico ao original (há teste), mas por reconstrução, contra o que o ADR afirma. OPÇÕES: (a) corrigir o ADR; (b) devolver `response_body` de fato. BLOQUEIO: decisão do usuário.

ENTREGA (02/10/2026), opção (b) por decisão do usuário: `PostingResponse` na aplicação monta o corpo da resposta de escrita uma vez; o store o grava em `response_body` e a API o devolve como texto, sem reserializar, na primeira resposta e na repetição. Coluna de `jsonb` para `json`, porque `jsonb` reordena chaves e normaliza espaços. ADR-0006 com a revisão e a alternativa rejeitada; ERD atualizado. VERIFICAÇÃO: teste que altera o `response_body` por fora e recebe o texto alterado na repetição; teste que compara a primeira resposta com o gravado; 107 verdes; no Docker, primeira resposta, reenvio e coluna com o mesmo SHA-256.

### 21.2. Corrigir as afirmações desatualizadas do CLAUDE.md

`prioridade: Baixa` · `doc`

ÂNCORA: `CLAUDE.md`, achado ao retomar a sessão. A linha de arquitetura do ADR-0006 dizia "nunca por consulta prévia", superada pelas revisões dos cards 19.4 e 19.5; a pendência citava "EF e BDD na versão 1.1", com a EF na 1.3.

ENTREGA (02/10/2026): as duas afirmações corrigidas e coerentes com o ADR-0006 e o histórico das especificações.

### 25. Implementar o painel de evidência

`prioridade: Média` · `codigo` · `condicional`

ADR-0011, escopo condicional. Página estática no wwwroot, sem dependência de toolchain fora do .NET.

Quatro painéis que PROVAM decisões, não fazem CRUD: disparo concorrente (ADR-0005), linha do tempo da posição (ADR-0003), replay idempotente (ADR-0006) e origem do cálculo (ADR-0007).

CRITÉRIO DE CORTE JÁ FIXADO: se na hora 16 o teste de concorrência não estiver verde ou o README não estiver completo, o painel é cortado integralmente e vira item de com-mais-tempo.md.

ATUALIZAÇÃO 02/10/2026: movido de Backlog para A Fazer por decisão do usuário. Item 8 da fila. As duas condições do critério de corte estão satisfeitas (concorrência verde, README final concluído), então o corte não se aplica. `com-mais-tempo.md` não existe; o destino equivalente é a seção "O que seria feito com mais tempo" do README.

CRITÉRIO ACRESCENTADO AO INICIAR (o cartão não tinha): quatro painéis em `wwwroot` servidos na raiz; executados em navegador real com resultado capturado e sem erro de console; disparo concorrente reproduzindo F07; só API pública; README e diagramas atualizados. Fora do escopo: OpenAPI (card 19.3) e teste automatizado do painel (ADR-0011).

ENTREGA (02/10/2026): `index.html`, `painel.css`, `painel.js`, sem dependência externa. Contas `3333…`, `4444…`, `5555…` no script de massa (não há endpoint de criação de conta); `entriesReplayed` na posição (EF 1.4). VERIFICAÇÃO em Chrome headless, repetida: 10 aceitos, 40 recusados, mínimo 0,00; posição em três instantes passados; reenvio `201`/`200` com o mesmo SHA-256 e `409` com outro valor; âncora de snapshot com `entriesReplayed` 0. Sem erro de JavaScript; um `404` de favicon achado e eliminado. ADR-0011 com nota de implementação; README, C2 e ESTADO atualizados. Fila ativa 19 a 25 concluída.

### 19.2. Testar a API via Insomnia

`prioridade: Média` · `teste`

DESBLOQUEADO em 02/10/2026: o card 19 foi concluído. Antes: BLOQUEADO POR 'Implementar a porta de entrada e os endpoints HTTP'.

Hoje a API tem apenas /health/live e /health/ready. Nenhum endpoint de negócio existe, a connection string não é lida e o store não está registrado na injeção de dependência.

QUANDO DESBLOQUEAR: a coleção sai por importação direta do OpenAPI, e o requests.http que já está no repositório vira útil.

ATUALIZAÇÃO 02/10: `requests.http` cobre os 5 endpoints. O documento OpenAPI ainda não é gerado, então a coleção do Insomnia não sai por importação direta: sai do `requests.http` ou é montada à mão.

CRITÉRIO ACRESCENTADO AO INICIAR: coleção versionada, importável, com testes embutidos, que roda inteira pelo `inso` contra o `docker compose`.

ENTREGA (02/10/2026): `insomnia/pacioli-ledger.insomnia.json`, 15 requisições e 43 testes: saúde; crédito, reenvio, conflito, débito, saldo insuficiente, chave ausente, estorno e estorno duplicado; posição corrente, histórica e futura, extrato e conta inexistente. Chave de idempotência gerada antes de cada escrita; reenvio e estorno encadeados ao resultado anterior. VERIFICAÇÃO com `inso` 13.3.0 (binário oficial da release do Insomnia): 43 de 43 em três execuções seguidas, código de saída 0; apontada para conta inexistente, 26 testes reprovam e o código de saída é 1. README com importação e execução pela linha de comando.

### 19.3. Escrever os testes de contrato da API

`prioridade: Baixa` · `teste`

Previstos no ADR-0010. Comparação por instantâneo do OpenAPI gerado, para que alteração acidental de contrato seja detectada antes da publicação (RNF-039).

DEPENDÊNCIA: só faz sentido depois dos endpoints.

CRITÉRIO ACRESCENTADO AO INICIAR: OpenAPI servido com os 5 endpoints, corpos e erros; teste de contrato verde que reprova quando o contrato muda; build sem avisos. Fora do escopo: interface de exploração.

ENTREGA (02/10/2026): `Microsoft.AspNetCore.OpenApi` e `Microsoft.AspNetCore.Mvc.Testing` 10.0.12, iguais ao runtime instalado. Endpoints com nome, resumo, corpo de resposta e problemas declarados. Três imprecisões do documento gerado corrigidas antes de aprovar o instantâneo: título fora da convenção; inteiro declarado como "inteiro ou string" (leitura de números passou a estrita); `reversalOf` declarado obrigatório embora omitido quando nulo. Projeto `PacioliBank.Contract.Tests`, comparação por instantâneo sem a biblioteca Verify (registrado no ADR-0010). VERIFICAÇÃO: acrescentar um campo à resposta de posição reprova o teste, com o diff exato; 109 verdes; `/openapi/v1.json` servido no Docker; coleção do Insomnia segue 43/43.

### 26. Escrever os testes de arquitetura com NetArchTest

`prioridade: Média` · `arquitetura` · `teste`

Previstos no ADR-0010 e no ADR-0001, ainda não escritos. São a defesa automatizada contra o risco R-07, a erosão de fronteiras que originou o sistema legado descrito no enunciado.

REGRAS A VERIFICAR: domínio sem referência a infraestrutura, ASP.NET Core ou acesso a dados; módulos comunicando-se por contrato explícito; Ledger não conhecendo Api, Balances nem Events.

CRITÉRIO: o teste reprova se alguém introduzir uma dependência fora do grafo declarado.

ESCOPO AMPLIADO, com motivo: a regra dos endpoints apontou, já na primeira execução, uma violação real (`LedgerProblems` capturava `NpgsqlException`). Corrigida na causa, neste cartão.

ENTREGA (02/10/2026): `PacioliBank.Architecture.Tests`, NetArchTest 1.3.2, 5 regras: Ledger sem banco, HTTP nem outros módulos; domínio sem aplicação; Persistence sem HTTP nem Events; Events sem Ledger; endpoints sem banco. Correção: `PostgresLedgerStore` traduz falha transitória do driver para `LedgerUnavailableException`, nas escritas e nas leituras; `LedgerProblems` deixou de conhecer o Npgsql. VERIFICAÇÃO: regra dos endpoints reprovou o código antigo; regra do domínio reprova dependência inserida de propósito; 2 testes de banco inalcançável reprovaram antes da correção; 116 verdes; no Docker, banco parado dá `503` com `Retry-After`, e religado volta a `200`.

### 27. Substituir o initdb do PostgreSQL por migrações DbUp

`prioridade: Média` · `codigo` · `infra`

Hoje o esquema é aplicado pelo entrypoint do container, que só roda na PRIMEIRA criação do volume. Qualquer mudança de esquema exige docker compose down -v, o que não é aceitável fora do ambiente local e não atende RNF-038.

CRITÉRIO: migrações idempotentes aplicadas na subida da aplicação, com teste de integração partindo de banco vazio.

--- ATUALIZAÇÃO 02/10/2026, ao iniciar (append-only) ---

CRITÉRIO REVISADO por decisão do usuário (opção b). 'Aplicadas na subida da aplicação' colide com o ADR-0009: a API roda com o papel que só lê e insere, e o papel de migração 'nunca é usado pela aplicação em execução'. Aplicar migrações na API exigiria dar a ela a credencial capaz de alterar e apagar o ledger.

ESCOPO: projeto PacioliBank.Migrations com DbUp; scripts versionados (o esquema atual vira a migração 0001, a massa local vira script separado); passo de migração separado no docker compose, com o papel de migração, antes da API; o entrypoint do banco deixa de aplicar o esquema; testes de integração pelas mesmas migrações, com idempotência e evolução; revisão do ADR-0002.

CRITÉRIO: do zero, migração termina com sucesso e API atende; de novo sobre o mesmo volume, nada reaplicado e dados preservados; testes partindo de banco vazio; a API continua sem privilégio de alteração de esquema; build sem avisos e suíte verde.

--- ENTREGA 02/10/2026 (append-only) ---

ENTREGUE:
- src/PacioliBank.Migrations: console com DbUp (dbup-postgresql 7.0.1). SchemaMigrator aplica só o que falta, uma transação por script, variáveis do DbUp desligadas; diário public.schema_versions. Massa local com diário próprio (public.seed_versions), só com PACIOLI_SEED_LOCAL=true
- db/init/ substituído por db/migrations/0001_esquema_inicial.sql (esquema sem alteração) e db/seed/0001_contas_locais.sql
- docker compose: serviço pacioli-migrations (pacioli_migrator, termina); a API depende de service_completed_successfully e segue com pacioli_runtime; o entrypoint do banco não aplica mais nada
- LedgerFixture monta o banco de teste pelo mesmo SchemaMigrator
- Revisão do ADR-0002 (onde a migração roda), com 4 alternativas rejeitadas: API migrando com a credencial de migração (contraria o ADR-0009); API migrando com o papel da aplicação; manter o initdb; ferramenta externa (Flyway, Liquibase, sqitch)
- C2 com o contêiner do migrador (renderizado com mermaid-cli); referências a db/init atualizadas no README, CLAUDE.md, ERD, C1, convenções e ADR-0006, 0008, 0009

CRITÉRIO ATENDIDO:
- Do zero: migrador sai com código 0, API saudável
- Migrador e API recriados sobre o mesmo volume: "No new scripts need to be executed"; crédito gravado antes continua na posição
- 7 testes de integração novos (MigrationTests), que reprovaram com o esboço: banco vazio, reexecução, evolução sem perda de dado, falha sem alteração parcial nem registro, delimitador nomeado intacto, massa só quando pedida, diário fora do alcance do papel da aplicação
- Poder de detecção: sem a transação por script, o teste de falha reprova; sem desligar as variáveis, o do delimitador reprova
- 6ª regra de arquitetura: o migrador não depende do Ledger, de Events, da API nem de ASP.NET
- Testes de privilégio continuam verdes; painel (4 demonstrações) e Insomnia (43/43) verdes sobre o banco migrado
- Build sem avisos; 124 testes verdes (59 + 57 + 6 + 2)

PENDENTE, declarado: regra de compatibilidade entre migração e versão da API para várias instâncias (expandir antes, contrair depois), [NVI], registrada no ADR-0002 e no ESTADO §5.
TRANSIÇÃO: um último docker compose down -v local, feito.

### 28. Elevar o AnalysisMode para latest-recommended

`prioridade: Baixa` · `codigo`

LACUNA L-06. O ADR-0002 previu latest-recommended após o primeiro build limpo. O build está limpo há vários ciclos.

CRITÉRIO: build continua sem avisos no modo elevado, ou os avisos novos são corrigidos. Commit próprio e pequeno.

--- ENTREGA 03/10/2026 (append-only) ---

ENTREGUE: AnalysisMode=Recommended no Directory.Build.props (equivale a latest-recommended, previsto no ADR-0002). Elevado o modo, o build reprovou com 4 regras, todas corrigidas no código, nenhuma suprimida:
- CA1716 (palavra reservada como nome de parâmetro): 'to' em ILedgerStore.GetStatementAsync passou a 'until'; o parâmetro SQL (@to) e o da API não mudaram
- CA1862 (comparação sem distinguir maiúsculas): Currency.TryFromCode compara com OrdinalIgnoreCase. Antes da troca, teste novo fixou o comportamento (7 casos), porque a porta de entrada valida a moeda por esse método e nenhum teste o cobria
- CA1711 (sufixo reservado no nome do tipo): LedgerCollection dos testes passou a LedgerCollectionDefinition
- CA1859 (tipo concreto para desempenho): dublê de publicador dos testes expõe List

Supressões que já existiam, com motivo escrito ao lado, continuam: CA1707 nos testes (nomes em português com sublinhado) e CA1031 no OutboxDispatcher. Nenhuma nova.

CRITÉRIO ATENDIDO:
- Build sem avisos no modo elevado; as duas imagens Docker compilam nele
- 131 testes verdes (66 + 57 + 6 + 2); o contrato OpenAPI não mudou
- Filtro de período do extrato e moeda em minúsculas conferidos contra a API no Docker
- L-06 encerrada; ESTADO, README, CLAUDE.md, ADR-0002 e KANBAN atualizados

ACHADO NO CAMINHO: as contagens de teste do ADR-0010 tinham ficado desatualizadas no card 27 (50 de integração, 5 regras). Corrigidas neste commit e registradas no histórico do ESTADO.

### 31. Configurar CI no GitHub Actions

`prioridade: Média` · `infra`

Pipeline executando build sem avisos, as cinco camadas de teste, cobertura e varredura de segredos.

VALOR NO DESAFIO: badge verde no README é evidência visível de que o critério de bloqueio do ADR-0010 é real e não apenas declarado.

--- ATUALIZAÇÃO 04/10/2026, ao entrar em A Fazer (append-only) ---

MOTIVO DA ENTRADA: decisão do usuário, com A Fazer vazia após o card 28. Primeiro item da lista 'com mais tempo' do README que não depende de terceiros.

CRITÉRIO:
- Workflow em .github/workflows/, disparado em push e pull request para main
- Build com TreatWarningsAsErrors e AnalysisMode=Recommended: aviso reprova o pipeline
- As cinco camadas: domínio, arquitetura, contrato, integração (Testcontainers com o Docker do runner) e a coleção do Insomnia pelo inso contra o docker compose
- Cobertura coletada (coverlet, já no projeto) e publicada como artefato. Sem limite mínimo: o número é informativo, e a força das asserções é assunto do card 36
- Varredura de segredos no histórico; ações de terceiros fixadas por SHA
- Badge no README
- PODER DE DETECÇÃO medido: o pipeline fica vermelho num ramo com um aviso de compilação introduzido de propósito, e verde de novo sem ele
- Execução verde registrada em main, com o link da execução no cartão

[NVI] Tempo de execução no runner e disponibilidade do Docker para o Testcontainers no ubuntu-latest: confirmar na primeira execução.

--- ENTREGA 04/10/2026 (append-only) ---

ENTREGUE: .github/workflows/ci.yml com três jobs, em push de qualquer ramo e pull request para main:
- Build em Release (aviso é erro, modo Recommended) e testes de domínio, arquitetura, contrato e integração (Testcontainers no Docker do runner); resultados e cobertura publicados como artefato
- Coleção do Insomnia pelo inso 13.3.0 contra o docker compose, com logs do ambiente em caso de falha
- gitleaks 8.30.1 no histórico completo
- Ações fixadas por SHA; inso e gitleaks conferidos por sha256 (digest oficial de cada release)
- .gitleaksignore com 1 falso positivo (UUID de exemplo de Idempotency-Key na EF §8, em 2 commits), ignorado pela impressão digital e não pela regra
- Badge no README

CRITÉRIO ATENDIDO:
- Verde em main: https://github.com/eduardojnet/PacioliBank/actions/runs/37245543804 (cerca de 1 minuto: build e testes 42 s, Insomnia 52 s, segredos 4 s, em paralelo)
- Poder de detecção: ramo ci/prova-de-deteccao com CS0219 plantado ficou vermelho (https://github.com/eduardojnet/PacioliBank/actions/runs/37245626646), com a anotação na linha certa; o job do Insomnia também reprovou, porque a imagem compila com a mesma regra. Ramo apagado depois
- gitleaks: acusou um segredo plantado num repositório temporário (código de saída 1)
- Verde de novo em main depois da correção abaixo
- [NVI] resolvido: Docker do ubuntu-latest atende o Testcontainers; tempo total por volta de 1 minuto

CORRIGIDO NO CAMINHO: na prova, o passo de publicar cobertura acusava uma segunda falha por falta de arquivos quando o build reprovava; passou a rodar só quando os testes rodaram.

DESCOBERTO, virou card (regra 10):
- 31.1: auditoria de dependências vulneráveis (RNF-026, ADR-0009, critério de bloqueio do ADR-0010), que o CI ainda não faz
- 31.2 / L-13: a ENF §11 dizia a RNF-036 (cobertura >= 85% no domínio) 'realizada'; nunca tinha sido medida. Medida: 56,8% (testes de domínio), 49,9% (integração), 74,9% (os dois). ENF corrigida para 1.2; a resolução depende de decisão do usuário

### 31.1. Auditar dependências vulneráveis no CI

`prioridade: Média` · `infra`

DESCOBERTO NO CARD 31 (04/10/2026). O ADR-0010 lista 'vulnerabilidade de severidade alta em dependência' no critério de bloqueio, o ADR-0009 pede auditoria de pacotes no pipeline e a RNF-026 exige zero alertas altos ou críticos. O CI do card 31 não faz essa auditoria.

CRITÉRIO:
- Passo no CI que lista pacotes vulneráveis, inclusive transitivos (dotnet list package --vulnerable --include-transitive), e reprova com severidade alta ou crítica
- Poder de detecção medido: reprova com um pacote sabidamente vulnerável, num ramo de prova apagado depois
- Resultado atual registrado (quais alertas existem hoje, se existirem)
- ENF §11 e ESTADO atualizados: RNF-026 passa a realizada no CI

--- ENTREGA 04/10/2026 (append-only) ---

ACHADO AO COMEÇAR: o SDK 10 já auditava no restore e, com TreatWarningsAsErrors, um alerta alto (NU1903) já reprovava. Mas por padrão implícito do SDK, e reprovando também baixa e moderada, mais rígido que a RNF-026.

ENTREGUE:
- Directory.Build.props com NuGetAudit explícito: modo all (transitivas inclusive) e nível high (alta e crítica reprovam). Vale localmente, no CI e na imagem Docker
- CI: passo 'Restaurar pacotes e auditar dependências' (reprova) e passo informativo que lista todas as severidades (dotnet list package --vulnerable --include-transitive)

CRITÉRIO ATENDIDO:
- Resultado atual: nenhum pacote vulnerável, em nenhuma severidade, nos 9 projetos
- Poder de detecção medido localmente: nível high reprova o Newtonsoft.Json 12.0.1 (alerta alto) e critical deixa passar; modo all pega a mesma vulnerabilidade vinda por dependência transitiva (Newtonsoft.Json.Bson 1.0.2) e direct deixa passar
- No CI: ramo ci/prova-auditoria com a dependência transitiva vulnerável ficou vermelho no passo de restore (https://github.com/eduardojnet/PacioliBank/actions/runs/37246291115); o job do Insomnia também, porque a imagem restaura com a mesma regra. Causa confirmada reproduzindo o mesmo commit localmente: um único erro, NU1903. Ramo apagado
- ENF §11, ADR-0010, README, ESTADO e CLAUDE.md atualizados: RNF-026 realizada

OBSERVADO: o GitHub avisa que o rótulo ubuntu-latest migra para o Ubuntu 26 a partir de 19/10/2026. Não fixei a versão do runner neste card; fica registrado para decisão.
