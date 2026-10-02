# Quadro Kanban: PacioliBank

Espelho em texto do quadro mantido no TickTick. Atualizado a cada entrega, junto de [`ESTADO.md`](./ESTADO.md). Política do quadro e convenção de numeração em [`PROCESSO-KANBAN.md`](./PROCESSO-KANBAN.md).

**Data:** 2026-10-02 · **Cartões:** 41 · **Sincronizado com o TickTick em:** 2026-10-02, a partir de leitura direta do quadro

## Distribuição

| Coluna | Cartões | Números |
|---|---|---|
| Não Classificado | 0 | Vazia por decisão. Cartão aqui é falha de triagem, não trabalho pendente. |
| Backlog/Ideias | 10 | 24, 25, 31 a 38 |
| A Fazer | 6 | 19.2, 19.3, 23, 26, 27, 28 |
| Em Andamento | 0 | Limite de 1 em curso, por decisão. |
| Em Revisão | 0 | |
| Bloqueado | 2 | 29, 30 |
| Concluído | 23 | 01 a 22 |

---

## Não Classificado

_Vazia, e esse é o estado correto. Todo item do projeto foi triado para uma coluna com critério. Um cartão aparecer aqui significa que alguém registrou trabalho sem classificar, e a ação é triá-lo, não executá-lo._

---

## Backlog/Ideias

### 24. Implementar o despachante de outbox

`prioridade: Média` · `codigo`

ADR-0008. A tabela e a gravação transacional já existem; falta o processo que lê e publica.

Consumo com FOR UPDATE SKIP LOCKED permite múltiplos despachantes em paralelo sem coordenação externa. Recuo exponencial em falha de publicação.

O barramento concreto não é decidido: a decisão arquitetural é o padrão, não o produto.

### 25. Implementar o painel de evidência

`prioridade: Média` · `codigo` · `condicional`

ADR-0011, escopo condicional. Página estática no wwwroot, sem dependência de toolchain fora do .NET.

Quatro painéis que PROVAM decisões, não fazem CRUD: disparo concorrente (ADR-0005), linha do tempo da posição (ADR-0003), replay idempotente (ADR-0006) e origem do cálculo (ADR-0007).

CRITÉRIO DE CORTE JÁ FIXADO: se na hora 16 o teste de concorrência não estiver verde ou o README não estiver completo, o painel é cortado integralmente e vira item de com-mais-tempo.md.

### 31. Configurar CI no GitHub Actions

`prioridade: Média` · `infra`

Pipeline executando build sem avisos, as cinco camadas de teste, cobertura e varredura de segredos.

VALOR NO DESAFIO: badge verde no README é evidência visível de que o critério de bloqueio do ADR-0010 é real e não apenas declarado.

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

### 19.2. Testar a API via Insomnia

`prioridade: Média` · `teste`

DESBLOQUEADO em 02/10/2026: o card 19 foi concluído. Antes: BLOQUEADO POR 'Implementar a porta de entrada e os endpoints HTTP'.

Hoje a API tem apenas /health/live e /health/ready. Nenhum endpoint de negócio existe, a connection string não é lida e o store não está registrado na injeção de dependência.

QUANDO DESBLOQUEAR: a coleção sai por importação direta do OpenAPI, e o requests.http que já está no repositório vira útil.

ATUALIZAÇÃO 02/10: `requests.http` cobre os 5 endpoints. O documento OpenAPI ainda não é gerado, então a coleção do Insomnia não sai por importação direta: sai do `requests.http` ou é montada à mão.

### 19.3. Escrever os testes de contrato da API

`prioridade: Baixa` · `teste`

Previstos no ADR-0010. Comparação por instantâneo do OpenAPI gerado, para que alteração acidental de contrato seja detectada antes da publicação (RNF-039).

DEPENDÊNCIA: só faz sentido depois dos endpoints.

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

### 26. Escrever os testes de arquitetura com NetArchTest

`prioridade: Média` · `arquitetura` · `teste`

Previstos no ADR-0010 e no ADR-0001, ainda não escritos. São a defesa automatizada contra o risco R-07, a erosão de fronteiras que originou o sistema legado descrito no enunciado.

REGRAS A VERIFICAR: domínio sem referência a infraestrutura, ASP.NET Core ou acesso a dados; módulos comunicando-se por contrato explícito; Ledger não conhecendo Api, Balances nem Events.

CRITÉRIO: o teste reprova se alguém introduzir uma dependência fora do grafo declarado.

### 27. Substituir o initdb do PostgreSQL por migrações DbUp

`prioridade: Média` · `codigo` · `infra`

Hoje o esquema é aplicado pelo entrypoint do container, que só roda na PRIMEIRA criação do volume. Qualquer mudança de esquema exige docker compose down -v, o que não é aceitável fora do ambiente local e não atende RNF-038.

CRITÉRIO: migrações idempotentes aplicadas na subida da aplicação, com teste de integração partindo de banco vazio.

### 28. Elevar o AnalysisMode para latest-recommended

`prioridade: Baixa` · `codigo`

LACUNA L-06. O ADR-0002 previu latest-recommended após o primeiro build limpo. O build está limpo há vários ciclos.

CRITÉRIO: build continua sem avisos no modo elevado, ou os avisos novos são corrigidos. Commit próprio e pequeno.

---

## Em Andamento

_Vazia. O próximo da fila é o 23, que entra aqui ao abrir o bloco de trabalho (PROCESSO-KANBAN §5)._

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
