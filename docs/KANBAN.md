# Quadro Kanban: PacioliBank

Espelho em texto do quadro mantido no TickTick. Atualizado a cada entrega, junto de [`ESTADO.md`](./ESTADO.md).

**Data:** 2026-10-02 · **Cartões:** 37

## Distribuição

| Coluna | Cartões | Leitura |
|---|---|---|
| Não Classificado | 0 | Vazia por decisão. Cartão aqui é falha de triagem, não trabalho pendente. |
| Backlog/Ideias | 10 | Evoluções com gatilho de adoção declarado. Nenhuma entra sem ADR. |
| A Fazer | 7 | Fila ordenada. Duas são requisito obrigatório do enunciado. |
| Em Andamento | 1 | Limite de 1 em curso, por decisão. |
| Em Revisão | 2 | Entregue, porém sob verificação ou com divergência conhecida. |
| Bloqueado | 3 | Dois bloqueios legítimos e um que é, ele próprio, parte da entrega. |
| Concluído | 14 | Especificação, decisões, domínio, persistência, testes e repositório público. |

---

## Não Classificado

_Vazia, e esse é o estado correto. Todo item do projeto foi triado para uma coluna com critério. Um cartão aparecer aqui significa que alguém registrou trabalho sem classificar, e a ação é triá-lo, não executá-lo._

---

## Backlog/Ideias

### Implementar o painel de evidência

`prioridade: Média` · `codigo` · `condicional`

ADR-0011, escopo condicional. Página estática no wwwroot, sem dependência de toolchain fora do .NET.

Quatro painéis que PROVAM decisões, não fazem CRUD: disparo concorrente (ADR-0005), linha do tempo da posição (ADR-0003), replay idempotente (ADR-0006) e origem do cálculo (ADR-0007).

CRITÉRIO DE CORTE JÁ FIXADO: se na hora 16 o teste de concorrência não estiver verde ou o README não estiver completo, o painel é cortado integralmente e vira item de com-mais-tempo.md.

### Implementar o despachante de outbox

`prioridade: Média` · `codigo`

ADR-0008. A tabela e a gravação transacional já existem; falta o processo que lê e publica.

Consumo com FOR UPDATE SKIP LOCKED permite múltiplos despachantes em paralelo sem coordenação externa. Recuo exponencial em falha de publicação.

O barramento concreto não é decidido: a decisão arquitetural é o padrão, não o produto.

### Implementar daily_balances para consulta histórica

`prioridade: Baixa` · `codigo` · `desempenho`

ADR-0007, evolução especificada e deliberadamente não implementada.

O snapshot é ancorado em sequência (ordem de registro) e a consulta histórica usa occurred_at (ordem do fato). Com lançamento retroativo, as duas divergem, então hoje a consulta histórica agrega pelo índice.

GATILHO DE ADOÇÃO: p99 da consulta histórica ultrapassar o alvo de RNF-002. Construir antes disso é construir contra premissa não validada.

### Incluir transferência entre contas no escopo

`prioridade: Nenhuma` · `arquitetura`

Deliberadamente FORA do escopo atual (EF 3.2). Exige transação abrangendo dois agregados.

POSIÇÃO REGISTRADA (ENF 5.2): quando entrar, a recomendação é saga com lançamento em duas pernas e conta transitória de liquidação, que é o modelo contábil padrão e o único que permanece correto sob particionamento.

EXIGE: novo ADR e revisão do ADR-0001 e do ADR-0005 (ordenação determinística de bloqueios para evitar deadlock).

### Configurar CI no GitHub Actions

`prioridade: Média` · `infra`

Pipeline executando build sem avisos, as cinco camadas de teste, cobertura e varredura de segredos.

VALOR NO DESAFIO: badge verde no README é evidência visível de que o critério de bloqueio do ADR-0010 é real e não apenas declarado.

### Atualizar as dependências NuGet

`prioridade: Baixa` · `codigo`

Versões congeladas porque estão VERIFICADAS com build limpo e suíte verde, não porque são as mais recentes.

Defasagem conhecida: Microsoft.NET.Test.Sdk 17.12.0 contra 18.10.1, coverlet 6.0.2 contra 10.1.0, xunit 2.9.2 contra xunit.v3 4.0.1.

ATENÇÃO: xunit v3 muda o modelo de execução (projeto de teste vira executável). Atualizar é item próprio, com verificação própria, nunca no meio de outra entrega.

### Avaliar a migração para PostgreSQL 18

`prioridade: Nenhuma` · `infra`

O ADR-0002 fixou a 17. A 18 tem suporte até 2030 contra 2029 e traz uuidv7() nativo, que melhoraria a localidade de inserção no índice primário de uma tabela append-only.

[NVI] A disponibilidade de uuidv7() na 18 precisa de confirmação na documentação oficial antes de virar decisão.

EXIGE: revisão do ADR-0002 e uma linha do docker-compose.

### Adicionar OpenTelemetry e Serilog

`prioridade: Baixa` · `codigo` · `observabilidade`

RNF-030 a RNF-032. Rastreamento distribuído, log estruturado com correlação e métricas de negócio.

MÉTRICA DE MAIOR VALOR DIAGNÓSTICO: a taxa de computedFrom=ledger nas consultas. É indicador ANTECEDENTE: seu crescimento precede degradação de latência em horas ou dias, permitindo agir antes que o cliente perceba.

### Adicionar teste de mutação com Stryker

`prioridade: Nenhuma` · `teste`

Mede a força das asserções, que é exatamente a lacuna que a métrica de cobertura não cobre. Cobertura alta com asserção fraca é autoengano.

Fora do escopo atual por custo de execução. Evolução natural do ADR-0010.

### Particionar o ledger por tempo

`prioridade: Nenhuma` · `infra` · `desempenho`

Risco R-04 da ENF: o ledger cresce de forma monotônica, nada é apagado. Com as premissas da ENF 3, a ordem de grandeza é de bilhões de linhas por ano.

DEPENDE DE: QA-004 (política de retenção), que é decisão de compliance e jurídico, não de engenharia.

---

## A Fazer

### Implementar a porta de entrada e os endpoints HTTP

`prioridade: Alta` · `codigo` · `arquitetura` · `requisito-obrigatorio`

LACUNA L-03. Hoje o hexágono existe só no lado dirigido: ILedgerStore é porta de saída e está bem feita, mas NÃO EXISTE porta de entrada. Quem chama o store é o teste de integração, diretamente.

ESCOPO:
1. Registrar NpgsqlDataSource e ILedgerStore na injeção de dependência (a API hoje nem lê a connection string).
2. Criar IPostEntryHandler como porta de entrada, deixando o endpoint como adaptador HTTP fino. O cálculo do fingerprint é regra do ADR-0006 e não pertence ao adaptador.
3. Endpoints: credits, debits, reversals, balance, entries.
4. Mapear exceções de domínio para ProblemDetails com os códigos da EF 8.6.
5. Ler o header Idempotency-Key.

CRITÉRIO: build sem avisos, 57 testes ainda verdes, docker compose up servindo os endpoints, e um crédito seguido de consulta de posição funcionando via curl.

### Converter os diagramas C4 para Mermaid no repositório

`prioridade: Alta` · `doc` · `requisito-obrigatorio`

LACUNA L-02, BLOQUEANTE PARCIAL. O enunciado exige toda a documentação no próprio repositório. Hoje quem clona o repo não vê diagrama algum: eles vivem no Lucid.

SOLUÇÃO: recriar em Mermaid sob docs/diagrams/. O GitHub renderiza nativamente, o arquivo é versionável e diffável, e não depende de conta no Lucid.

SEGUNDA PARTE: os diagramas mostram componentes que ainda não existem (painel, despachante, endpoints, autorização). Legítimo como arquitetura-alvo, desonesto como estado atual. Cada diagrama precisa de nota distinguindo implementado de especificado.

CRITÉRIO: diagramas visíveis ao clonar o repositório, com o estado de cada componente declarado.

### Finalizar o README

`prioridade: Alta` · `doc` · `requisito-obrigatorio`

Requisito obrigatório. O README existe e precisa da revisão final: estado atual honesto, endpoints documentados com exemplos curl, decisões resumidas com link para os ADRs, e a seção do que seria feito com mais tempo, que o enunciado pede explicitamente.

CRITÉRIO: alguém que nunca viu o projeto sobe o ambiente e faz um lançamento seguindo apenas o README.

### Substituir o initdb do PostgreSQL por migrações DbUp

`prioridade: Média` · `codigo` · `infra`

Hoje o esquema é aplicado pelo entrypoint do container, que só roda na PRIMEIRA criação do volume. Qualquer mudança de esquema exige docker compose down -v, o que não é aceitável fora do ambiente local e não atende RNF-038.

CRITÉRIO: migrações idempotentes aplicadas na subida da aplicação, com teste de integração partindo de banco vazio.

### Escrever os testes de arquitetura com NetArchTest

`prioridade: Média` · `teste` · `arquitetura`

Previstos no ADR-0010 e no ADR-0001, ainda não escritos. São a defesa automatizada contra o risco R-07, a erosão de fronteiras que originou o sistema legado descrito no enunciado.

REGRAS A VERIFICAR: domínio sem referência a infraestrutura, ASP.NET Core ou acesso a dados; módulos comunicando-se por contrato explícito; Ledger não conhecendo Api, Balances nem Events.

CRITÉRIO: o teste reprova se alguém introduzir uma dependência fora do grafo declarado.

### Elevar o AnalysisMode para latest-recommended

`prioridade: Baixa` · `codigo`

LACUNA L-06. O ADR-0002 previu latest-recommended após o primeiro build limpo. O build está limpo há vários ciclos.

CRITÉRIO: build continua sem avisos no modo elevado, ou os avisos novos são corrigidos. Commit próprio e pequeno.

### Escrever os testes de contrato da API

`prioridade: Baixa` · `teste`

Previstos no ADR-0010. Comparação por instantâneo do OpenAPI gerado, para que alteração acidental de contrato seja detectada antes da publicação (RNF-039).

DEPENDÊNCIA: só faz sentido depois dos endpoints.

---

## Em Andamento

### Migrar o ciclo de desenvolvimento para o terminal local

`prioridade: Alta` · `processo`

MOTIVO: no ambiente anterior não havia shell na máquina, então todo build e teste dependia de execução manual e cada erro custava um ciclo completo. Um aviso de compilação consumiu 3 turnos.

GANHO: ciclo fechado de código, git nativo com commits incrementais, Docker e Testcontainers executados diretamente.

PROCEDIMENTO: abrir o terminal no repositório e retomar pelo bloco da seção 10 do ESTADO.md.

ANDAMENTO (2026-10-02): build, suíte e push ao GitHub já executados no terminal local. Falta o critério abaixo, que exige uma alteração de código.

CRITÉRIO: primeira alteração de código compilada e testada sem intervenção manual.

---

## Em Revisão

### Verificar o poder de detecção do teste de concorrência

`prioridade: Alta` · `teste` · `risco`

LACUNA L-04. O ADR-0010 e o próprio ConcurrencyTests afirmam que o teste deve falhar contra implementação sem bloqueio. Isso NUNCA foi medido.

EXPERIMENTO: remover FOR NO KEY UPDATE do LedgerSql, rodar só a suíte de concorrência, restaurar.

DUAS HIPÓTESES DECLARADAS ANTES DO RESULTADO:
(a) falha: o bloqueio sustenta RN-001 e o teste enxerga;
(b) passa: a constraint mais a nova tentativa funcionam como controle otimista, a defesa em profundidade é real, e o critério que escrevi está errado e precisa ser corrigido.

CRITÉRIO: saber qual hipótese é verdadeira e registrar o resultado no ADR-0005 como validação empírica. Custo: 2 minutos.

### Aplicar as correções documentais pendentes

`prioridade: Média` · `doc` · `risco`

LACUNA L-05. Quatro divergências já identificadas e registradas, ainda não aplicadas nos documentos de origem:

1. EF 8.6 e BDD F09: 403 para conta de terceiro revela a existência da conta; deve ser 404 para cliente final (registrado no ADR-0009).
2. ADR-0001: módulo Integration renomeado para Events, por colisão com os testes de integração.
3. ADR-0010: projetos de teste com prefixo Ledger., substituído por PacioliBank.
4. ADR-0009: papéis app_* nos exemplos, substituídos por pacioli_*.

CRITÉRIO: os quatro documentos coerentes entre si e com o código.

---

## Bloqueado

### Responder às 6 questões de negócio em aberto

`prioridade: Média` · `risco` · `spec`

BLOQUEADO POR: ausência de interlocutor de negócio. No desafio não há cliente para perguntar, então cada uma tem conduta provisória declarada e isolada em ponto de extensão.

QA-001 limite ou cheque especial por conta
QA-002 política de lançamento retroativo
QA-004 retenção e arquivamento do ledger
QA-005 exigência multimoeda
QA-006 volume real: lançamentos por segundo, contas, taxa de leitura
QA-007 modelo corporativo de identidade e escopos

JÁ DECIDIDAS: QA-003 (estorno não negativa a conta) e RN-012 (bitemporalidade mantida).

VALOR NO DESAFIO: este card É a entrega. Demonstra que as lacunas foram identificadas e tratadas como risco declarado, em vez de preenchidas com suposição silenciosa.

### Testar a API via Insomnia

`prioridade: Média` · `teste`

BLOQUEADO POR: 'Implementar a porta de entrada e os endpoints HTTP'.

Hoje a API tem apenas /health/live e /health/ready. Nenhum endpoint de negócio existe, a connection string não é lida e o store não está registrado na injeção de dependência.

QUANDO DESBLOQUEAR: a coleção sai por importação direta do OpenAPI, e o requests.http que já está no repositório vira útil.

### Executar testes de carga e validar os RNF de desempenho

`prioridade: Nenhuma` · `teste`

BLOQUEADO POR: ausência de ambiente de carga e de dados reais de volume (QA-006).

RNF-001, RNF-002, RNF-005 e RNF-007 estão especificados com métrica na ENF, mas declarados como NÃO verificados na seção 11 da ENF.

DECISÃO JÁ TOMADA: fora do escopo do desafio. Declarar como especificado e não implementado é parte da entrega.

---

## Concluído

### Analisar o enunciado e definir a estratégia de entrega

`prioridade: Média` · `analise`

Leitura do desafio, identificação do que de fato é avaliado e calibração do escopo ao teto de 16h a 24h.

ENTREGUE: análise completa com riscos, premissas e alternativas. Conclusão central: o enunciado avalia capacidade de identificar o ponto de contenção e eliminá-lo, não volume de tecnologia.

### Especificar o comportamento em Gherkin (BDD)

`prioridade: Média` · `doc` · `spec`

10 funcionalidades, ~55 cenários em português, com tags de rastreabilidade para RF, RN e RNF.

ARQUIVO: docs/specs/BDD-comportamento.md
CRITÉRIO ATENDIDO: todo requisito tem ao menos um cenário de aceite.

### Escrever a Especificação Funcional

`prioridade: Média` · `doc` · `spec`

Modelo de domínio, 12 regras de negócio, 11 requisitos funcionais, 3 casos de uso, contrato de API e catálogo de erros.

ARQUIVO: docs/specs/EF-especificacao-funcional.md
DISCIPLINA: tudo além dos 3 requisitos do enunciado está marcado [INFERIDO] com justificativa.

### Escrever a Especificação Não Funcional

`prioridade: Média` · `doc` · `spec`

7 atributos de qualidade priorizados, 30 RNF com métrica e método de verificação, 7 cenários SEI, 7 riscos.

ARQUIVO: docs/specs/ENF-especificacao-nao-funcional.md
PREMISSAS de capacidade declaradas como premissas, não como fato (QA-006).

### Registrar as decisões arquiteturais (11 ADRs em MADR)

`prioridade: Alta` · `arquitetura` · `doc`

ADR-0001 a ADR-0011, cada um com contexto, critérios definidos antes da avaliação, alternativas rejeitadas com a condição que as reabriria, validação e gatilho de revisão.

DIRETÓRIO: docs/adr/
DESTAQUE: ADR-0005 (concorrência) registra uma revisão de posição da análise preliminar, com justificativa.

### Definir nome do produto e convenções de nomenclatura

`prioridade: Baixa` · `doc`

PacioliBank, em referência a Luca Pacioli (partidas dobradas, 1494), alinhado ao ledger append-only do ADR-0003.

ARQUIVO: docs/convencoes-de-nomenclatura.md
DECISÃO: não usar a marca da empresa avaliadora em repositório público.

### Produzir os diagramas C4 no Lucid

`prioridade: Média` · `doc` · `diagrama`

Três documentos: C4 detalhado (C1, C2, C3 em 3 páginas), diagrama de sequência nível 4 e C4 consolidado (C1 a C4 em uma página).

Validados antes da criação: zero erros estruturais.
ATENÇÃO: estão FORA do repositório. Ver card 'Converter diagramas para Mermaid'.

### Criar o esqueleto da solution e o ambiente local

`prioridade: Média` · `codigo` · `infra`

Solution .NET 10 com 5 projetos, Directory.Build.props com TreatWarningsAsErrors, docker-compose, esquema do banco com 5 tabelas e 3 papéis de privilégio mínimo.

CRITÉRIO ATENDIDO: docker compose up em um comando, build sem avisos.

### Implementar Money e Currency com testes

`prioridade: Média` · `codigo` · `dominio`

Value Object com moeda embutida, construtor privado, escala validada na criação. Operação entre moedas distintas é impossível por construção.

10 métodos de teste, 18 casos. ADR-0004.

### Implementar o agregado Account e as invariantes

`prioridade: Alta` · `codigo` · `dominio`

Account, LedgerEntry, PostingRequest, enums e 6 exceções. O agregado não carrega os lançamentos da conta: é reidratado sob bloqueio com a posição já calculada.

21 testes cobrindo RN-001, RN-002, RN-004, RN-005, RN-006, RN-007 e RN-008.

### Implementar a persistência com bloqueio por conta

`prioridade: Alta` · `codigo` · `persistencia`

PostgresLedgerStore e LedgerSql. A ordem dos passos dentro da transação é a arquitetura: bloqueia a linha da conta, depois lê a posição, o agregado decide, e lançamento, sequência, idempotência e outbox são gravados juntos.

Projeto separado do domínio: a inversão de dependência é física, garantida pelo compilador.

### Escrever os testes de integração e concorrência

`prioridade: Alta` · `teste`

18 testes contra PostgreSQL real via Testcontainers. Inclui o cenário F07 (50 débitos simultâneos sobre saldo para 10) e a verificação de que o papel da aplicação não consegue alterar nem excluir lançamento.

Barreira de sincronização para liberar as tarefas no mesmo instante.
RESULTADO: 57 testes verdes no total.

### Criar o documento de estado do projeto

`prioridade: Média` · `doc`

docs/ESTADO.md: documento vivo com o que existe, o que falta, 6 lacunas por severidade, fila de execução e bloco de retomada para nova sessão.

Seção 5 lista explicitamente o que NÃO está implementado.

### Criar o repositório público no GitHub

`prioridade: Alta` · `requisito-obrigatorio` · `risco`

LACUNA L-01, BLOQUEANTE. Requisito obrigatório explícito do enunciado: sem repositório público, o teste é desconsiderado.

ATENÇÃO AO PROCESSO: o enunciado avalia como se pensa e prioriza. Um push único com todo o código sinaliza ausência de processo. Preferir commits incrementais com mensagens descritivas, agrupados por decisão.

CRITÉRIO: repositório público acessível, README renderizando, histórico legível.

RESULTADO (2026-10-02): https://github.com/eduardojnet/PacioliBank, público, branch main, histórico em 7 commits agrupados por área. Os commits foram criados no mesmo dia, ao versionar o trabalho já existente; não refletem a cronologia original. Compilação de cada commit isolado não verificada [NVI].
