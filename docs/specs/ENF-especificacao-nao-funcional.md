# Especificação Não Funcional

**Projeto:** Sistema de Movimentações Financeiras e Posição Consolidada
**Documento:** 3 de 3 do pacote de especificação
**Versão:** 1.9
**Data:** 2026-10-05
**Status:** Proposto

**Documentos relacionados:**

- [BDD: Especificação por Comportamento](./BDD-comportamento.md): cenários que verificam os RNF com comportamento observável
- [Especificação Funcional](./EF-especificacao-funcional.md): `RF-xxx`, `RN-xxx`, domínio e contratos
- `docs/adr/` (a produzir): decisões arquiteturais que realizam estes atributos de qualidade

---

## 1. Propósito

Este documento define **quão bem** o sistema deve operar. É o documento que direciona a arquitetura: requisitos funcionais quase sempre admitem muitas arquiteturas, requisitos não funcionais eliminam quase todas.

O enunciado do desafio é explícito sobre este ponto: o sistema legado atende a função e falha na qualidade, apresentando "lentidão, instabilidade em horários de pico e dificuldade de manutenção". O problema a resolver é não funcional.

**Regra vinculante:** requisito não funcional sem métrica e sem forma de verificação não é requisito, é intenção. Todo item abaixo tem metrica e método de verificação. Onde o dado para calibrar a métrica não existe, a métrica é declarada como **premissa**, não como fato.

---

## 2. Priorização dos atributos de qualidade

Nem todo atributo pode ser maximizado. Esta ordem é a decisão explícita de o que sacrificar quando houver conflito.

| Ordem | Atributo | Justificativa | Sacrifica |
|---|---|---|---|
| 1 | **Integridade e consistência** | Dado financeiro errado gera dano ao cliente e passivo ao banco. Não há compensação aceitável. | Latência e disponibilidade de escrita |
| 2 | **Segurança e conformidade** | LGPD, sigilo bancário e regulação. Violação é risco jurídico, não técnico. | Simplicidade e conveniência de integração |
| 3 | **Disponibilidade de leitura** | O cliente consultando saldo é o caso de uso de maior volume e maior visibilidade. | Frescor absoluto do dado |
| 4 | **Auditabilidade** | Exigência regulatória e condição de investigação de incidente. | Espaço em disco |
| 5 | **Desempenho** | Resolve a dor declarada do legado. | Custo de infraestrutura |
| 6 | **Manutenibilidade** | O legado falhou por aqui; é o que determina o custo dos próximos cinco anos. | Velocidade da primeira entrega |
| 7 | **Elasticidade** | Necessária em pico, mas subordinada à consistência. | Custo |

**Decisão de conflito fundamental:** diante de escolha entre responder rápido e responder certo, o sistema responde certo ou não responde. Em livro-razão de banco, resposta errada é pior que ausência de resposta, porque a ausência é detectável e o erro não.

---

## 3. Premissas de capacidade

O enunciado não informa volume, e nenhum número foi inventado como fato. Os valores abaixo são **premissas declaradas de projeto**, usadas para dimensionar e para escrever testes. Devem ser substituídos pela telemetria real do legado assim que disponível (ver [EF](./EF-especificacao-funcional.md) QA-006).

| Dimensão | Premissa de projeto | Como validar |
|---|---|---|
| Lançamentos por segundo, média | 500/s | Telemetria de escrita do legado, 30 dias |
| Lançamentos por segundo, pico | 3.000/s | Percentil 99 por minuto no pico histórico |
| Consultas de posição por segundo, pico | 15.000/s | Logs de acesso dos canais |
| Razão leitura/escrita | 5:1 | Telemetria |
| Contas ativas | 5.000.000 | Base cadastral |
| Lançamentos por conta ao ano | 400 | Contagem no legado |
| Concorrência típica por conta | 1 a 2 operações simultâneas | Análise de colisão temporal |
| Concorrência em conta quente | até 50 operações simultâneas | Contas de maior movimento (conta de liquidação, parceiros) |

**Observação determinante para a arquitetura:** a concorrência agregada é alta, mas a concorrência **por conta** é baixa na quase totalidade das contas. Como a conta é a unidade de serialização ([EF](./EF-especificacao-funcional.md) §4.2), o sistema escala horizontalmente quase sem limite, e o gargalo real se restringe a um punhado de contas quentes. Tratar o problema como se toda a base fosse quente levaria a uma arquitetura muito mais cara e mais lenta.

**Risco desta premissa:** se a base real tiver grande número de contas quentes, a estratégia de serialização precisa mudar (fila dedicada por conta ou particionamento de lançamentos com consolidação posterior). Este risco está registrado em §9, R-02.

---

## 4. Requisitos de desempenho e escala

| ID | Requisito | Métrica | Verificação |
|---|---|---|---|
| **RNF-001** | Registro de lançamento responde dentro do alvo sob carga de pico | p95 ≤ 150 ms, p99 ≤ 400 ms, medido no servidor | Teste de carga (NBomber ou k6) no perfil de pico de §3 |
| **RNF-002** | Consulta de posição responde dentro do alvo, tanto atual quanto histórica | p95 ≤ 80 ms, p99 ≤ 200 ms | Teste de carga com distribuição realista de idade de conta |
| **RNF-003** | O custo de leitura não cresce com o histórico da conta | Variação de latência ≤ 20% entre conta com 100 e conta com 100.000 lançamentos | Teste comparativo com massa sintética |
| **RNF-004** | Nova tentativa por conflito de concorrência é invisível ao chamador | Taxa de `SERVICE_UNAVAILABLE` por conflito < 0,01% das escritas | Teste de concorrência ([BDD](./BDD-comportamento.md) F07) e métrica em produção |
| **RNF-005** | Capacidade de escrita escala por adição de instâncias | Crescimento ≥ 70% da vazão ao dobrar instâncias, com contas distribuídas | Teste de carga em duas topologias |
| **RNF-006** | Snapshot limita o volume de replay por consulta | Máximo de 1.000 lançamentos somados após o snapshot, em p99 | Métrica de `entriesReplayed` por consulta |
| **RNF-007** | Degradação sob sobrecarga é controlada, não catastrófica | A 150% da carga de pico, latência cresce no máximo 3x e a taxa de erro permanece < 1% | Teste de carga com rampa além do pico |

**RNF-003 é o requisito que caracteriza a solução.** É a tradução direta da dor do legado: um sistema cuja consulta de saldo fica mais lenta conforme o cliente usa a conta está condenado a degradar com o sucesso do banco. Snapshot mais ledger append-only elimina essa relação.

---

## 5. Requisitos de confiabilidade, consistência e resiliência

| ID | Requisito | Métrica | Verificação |
|---|---|---|---|
| **RNF-010** | Indisponibilidade de componente acessório (cache, projeção, mensageria) não impede operação | 100% das operações concluídas corretamente com cache e projeção derrubados | [BDD](./BDD-comportamento.md) F08, executado com falha injetada |
| **RNF-011** | Nenhuma falha produz gravação parcial | Zero lançamentos órfãos, zero eventos sem lançamento correspondente, zero lançamentos sem evento enfileirado | Teste de injeção de falha entre etapas; conciliação periódica |
| **RNF-012** | O sistema se protege de sobrecarga e abuso | Limite por chamador e por conta, com resposta 429 e tempo de espera informado | [BDD](./BDD-comportamento.md) F09; teste de carga abusiva |
| **RNF-013** | O estado de saúde é verificável e distingue vivo de pronto | `/health/live` independente de dependências; `/health/ready` reflete o armazenamento primário | Teste de integração com dependência derrubada |
| **RNF-014** | Falha de dependência não se propaga por esgotamento de recursos | Disjuntor abre antes do esgotamento do pool de conexões; tempo limite inferior ao do chamador | Teste de caos com dependência lenta |
| **RNF-015** | Encerramento de instância não perde requisição em andamento | Zero requisições abortadas em encerramento controlado com drenagem | Teste de encerramento sob carga |
| **RNF-016** | O estado é reconstruível a partir do ledger | Reconstrução completa de snapshots sem divergência | [BDD](./BDD-comportamento.md) F10 |
| **RNF-017** | Objetivo de disponibilidade do serviço | Premissa de projeto: 99,95% mensal para leitura, 99,9% para escrita | Medição por sonda sintética |
| **RNF-018** | Objetivos de recuperação | Premissa de projeto: RPO = 0 para lançamentos confirmados; RTO ≤ 15 min | Exercício de restauração documentado |

### 5.1 Modelo de consistência declarado

Declarar o modelo de consistência é obrigação de qualquer sistema financeiro, porque consumidor que presume garantia inexistente constrói defeito.

| Fronteira | Garantia | Justificativa |
|---|---|---|
| Dentro de uma conta | **Forte, linearizável.** Lançamento confirmado é imediatamente visível a qualquer leitura subsequente da mesma conta. | É o escopo da invariante de saldo não negativo |
| Entre contas distintas | **Sem garantia de ordenação global.** Não há transação atômica abrangendo duas contas no escopo atual. | Transferência entre contas é caso de uso de contexto superior; ver §5.2 |
| Entre o ledger e os eventos de integração | **Eventual, ao menos uma vez.** O evento é gravado na mesma transação do lançamento e publicado depois. | Padrão Outbox; evita perda e evita commit distribuído |
| Entre o ledger e o snapshot | **Eventual, com correção garantida.** Snapshot atrasado aumenta o custo da leitura, nunca altera o resultado. | Snapshot é derivado ([EF](./EF-especificacao-funcional.md) RN-010) |
| Entre réplicas de leitura | **Eventual.** Consulta que exige leitura própria da escrita deve ser direcionada ao primário. | Decisão explícita; a resposta informa `computedAtSequence` para o consumidor detectar atraso |

### 5.2 Transferência entre contas: decisão de escopo

Transferência não está em escopo ([EF](./EF-especificacao-funcional.md) §3.2) e esta ausência é deliberada. Débito em uma conta e crédito em outra, atomicamente, exige transação abrangendo dois agregados, o que:

- com banco único, é viável em uma transação local, ao custo de ampliar a janela de bloqueio e acoplar as duas contas;
- com particionamento, exige saga com compensação e estado intermediário visível.

**Posição:** quando a transferência entrar em escopo, a recomendação é saga com lançamento em duas pernas e conta transitória de liquidação, que é o modelo contábil padrão e o único que permanece correto sob particionamento. Formalizar em ADR no momento da inclusão, nunca improvisar sobre a API atual.

---

## 6. Requisitos de segurança, privacidade e conformidade

| ID | Requisito | Métrica | Verificação |
|---|---|---|---|
| **RNF-020** | Autenticação em toda requisição e autorização por titularidade | Zero endpoints de negócio acessíveis sem credencial válida; zero acessos cruzados entre titulares | [BDD](./BDD-comportamento.md) F09; teste automatizado de matriz de autorização |
| **RNF-021** | Dado pessoal e financeiro não aparece em log, traço ou mensagem de erro | Zero ocorrências de CPF, número completo de conta ou token em qualquer saída de log | Varredura automatizada por padrão sobre a saída de log em teste |
| **RNF-022** | Toda escrita é auditável e a trilha é inalterável | 100% das escritas com autor, instante, origem e correlação; ausência de privilégio de alteração no armazenamento | Inspeção de privilégios; [BDD](./BDD-comportamento.md) F09 |
| **RNF-023** | Dados protegidos em trânsito e em repouso | TLS 1.2+ obrigatório; criptografia em repouso no armazenamento e nos backups | Inspeção de configuração |
| **RNF-024** | Segredos fora do código e fora da imagem | Zero credenciais em repositório; verificação automatizada no pipeline | Varredura de segredos no CI |
| **RNF-025** | Privilégio mínimo no armazenamento | Usuário de aplicação sem `UPDATE` nem `DELETE` na tabela de lançamentos | Teste de integração que tenta alterar e espera recusa |
| **RNF-026** | Dependências sem vulnerabilidade conhecida de severidade alta | Zero alertas altos ou críticos na publicação | Auditoria de pacotes no CI |
| **RNF-027** | Conformidade com LGPD quanto a minimização e finalidade | Nenhum dado pessoal armazenado além do necessário; base legal documentada | Revisão com encarregado |

### 6.1 Imutabilidade garantida por privilégio, não por disciplina

**RNF-025 é o controle mais relevante deste capítulo.** A imutabilidade do ledger ([EF](./EF-especificacao-funcional.md) RN-003) pode ser implementada de duas formas:

1. **Por disciplina:** o código simplesmente não emite `UPDATE` nem `DELETE`. Depende de todo desenvolvedor presente e futuro não cometer o erro, e de nenhum script administrativo ser executado.
2. **Por privilégio:** o usuário de banco da aplicação recebe `GRANT INSERT, SELECT` e nada mais sobre a tabela de lançamentos. Alteração torna-se impossível, independentemente do código.

A segunda é a única que resiste a erro humano e é verificável em auditoria. Controle que depende de ninguém errar não é controle.

### 6.2 Tratamento de dado pessoal

| Dado | Classificação | Tratamento |
|---|---|---|
| Identificador da conta | Sensível financeiro | Mascarado em log; íntegro em armazenamento e resposta autorizada |
| CPF do titular | Pessoal | Não replicado neste sistema; referenciado por `CustomerId` opaco |
| Valor e histórico de movimentação | Sensível financeiro, sigilo bancário | Criptografado em repouso; acesso restrito ao titular e a papéis autorizados |
| Metadados do originador | Variável | Campo livre; proibido por contrato o envio de dado pessoal, verificado por validação de tamanho e formato |

**Decisão de minimização:** o sistema armazena `CustomerId` opaco e nunca replica CPF, nome ou contato. Reduz a superfície de exposição e limita o impacto de um eventual vazamento ao vínculo conta-cliente, sem identificação direta.

---

## 7. Requisitos de observabilidade e manutenibilidade

| ID | Requisito | Métrica | Verificação |
|---|---|---|---|
| **RNF-030** | Rastreamento distribuído ponta a ponta | 100% das requisições com traço correlacionado entre API, domínio e banco | Inspeção em ambiente de teste |
| **RNF-031** | Log estruturado com correlação | 100% das entradas em formato estruturado com identificador de correlação | Teste automatizado sobre a saída de log |
| **RNF-032** | Métricas de negócio, não apenas técnicas | Expostas: lançamentos por tipo, rejeições por motivo, atraso de snapshot, profundidade da fila de eventos, taxa de uso de `computedFrom=ledger` | Inspeção do endpoint de métricas |
| **RNF-033** | Detecção de divergência entre ledger e derivados | Conciliação periódica com alerta em qualquer divergência | Rotina automatizada com teste próprio |
| **RNF-034** | Código compila sem erro e sem aviso | Zero avisos com `TreatWarningsAsErrors` habilitado | Pipeline de CI |
| **RNF-035** | Direção de dependência preservada entre camadas | Domínio sem referência a infraestrutura ou framework de I/O | Teste de arquitetura (NetArchTest) |
| **RNF-036** | Cobertura de teste significativa no núcleo | Cobertura de linha ≥ 85% no projeto de domínio; 100% dos cenários `@critico` aprovados | Relatório de cobertura no CI |
| **RNF-037** | Ambiente local sobe em um comando | `docker compose up` funcional em máquina limpa, sem passo manual | Verificação em container limpo no CI |
| **RNF-038** | Esquema de banco versionado e aplicado automaticamente | Migrações idempotentes, aplicadas na subida, reversíveis | Teste de integração a partir de banco vazio |
| **RNF-039** | Contrato de API publicado e versionado | OpenAPI gerado a partir do código, com teste de regressão de contrato | Comparação por instantâneo no CI |

### 7.1 Por que RNF-037 é requisito de arquitetura, não de conveniência

O enunciado exige README com instruções claras para rodar localmente, e condiciona a avaliação a isso. Mas o motivo real é anterior ao desafio: um sistema que exige mais de um comando para subir produz atrito em cada nova pessoa do time, em cada reconstrução de ambiente e em cada execução de CI. O legado descrito como de "difícil manutenção" quase certamente falha neste ponto. Tratar a subida local como requisito arquitetural, verificado no pipeline, impede a regressão silenciosa.

### 7.2 Métrica de observabilidade com maior valor diagnóstico

`computedFrom=ledger` ([EF](./EF-especificacao-funcional.md) §8.5) indica que a consulta não encontrou snapshot aplicável. Crescimento desta taxa antecede degradação de latência em horas ou dias. É um indicador **antecedente**, não reativo: permite agir antes que o cliente perceba. Sistemas que monitoram apenas latência e erro descobrem o problema junto com o cliente.

---

## 8. Cenários de atributo de qualidade

Formato SEI (fonte, estímulo, ambiente, artefato, resposta, medida). São os cenários que a arquitetura precisa sustentar e que servem de critério de avaliação de qualquer decisão técnica.

### CQ-01: Pico de movimentação (desempenho e escala)

| Elemento | Conteúdo |
|---|---|
| Fonte | Sistemas originadores (folha de pagamento, liquidação Pix) |
| Estímulo | Rajada de 3.000 lançamentos por segundo, distribuídos em contas distintas |
| Ambiente | Operação normal, horário de pico |
| Artefato | Serviço de escrita e armazenamento primário |
| Resposta | Todos os lançamentos processados corretamente, sem perda, sem duplicidade |
| **Medida** | p99 ≤ 400 ms; zero perda; zero duplicidade (RNF-001, RNF-005) |

### CQ-02: Conta quente (concorrência)

| Elemento | Conteúdo |
|---|---|
| Fonte | Múltiplos originadores simultâneos |
| Estímulo | 50 débitos concorrentes na mesma conta, com saldo suficiente para apenas 10 |
| Ambiente | Operação normal |
| Artefato | Agregado Conta e mecanismo de serialização |
| Resposta | Exatamente 10 aceitos, 40 rejeitados por saldo insuficiente, posição nunca negativa |
| **Medida** | Zero posição negativa em qualquer instante; p99 ≤ 800 ms sob esta contenção (RNF-004) |

### CQ-03: Queda da mensageria (resiliência)

| Elemento | Conteúdo |
|---|---|
| Fonte | Infraestrutura |
| Estímulo | Barramento de eventos indisponível por 30 minutos |
| Ambiente | Operação normal |
| Artefato | Publicador de eventos e armazenamento |
| Resposta | Lançamentos continuam sendo aceitos; eventos acumulam pendentes; publicação retoma sem perda ao restabelecer |
| **Medida** | Zero lançamentos recusados por esta causa; zero eventos perdidos; retomada automática (RNF-010, RNF-011) |

### CQ-04: Retry por timeout de rede (consistência)

| Elemento | Conteúdo |
|---|---|
| Fonte | Sistema originador |
| Estímulo | Timeout do cliente após o servidor ter confirmado; cliente reenvia o comando |
| Ambiente | Instabilidade de rede |
| Artefato | Controle de idempotência |
| Resposta | Resposta original devolvida; nenhum lançamento adicional |
| **Medida** | Zero duplicidade sob 100% de reenvio (RNF-011) |

### CQ-05: Acesso indevido (segurança)

| Elemento | Conteúdo |
|---|---|
| Fonte | Chamador autenticado como outro titular |
| Estímulo | Consulta à conta de terceiro |
| Ambiente | Operação normal |
| Artefato | Camada de autorização |
| Resposta | Acesso recusado sem revelar a existência da conta; tentativa registrada em auditoria |
| **Medida** | Zero vazamento de existência; 100% das tentativas auditadas (RNF-020, RNF-022) |

### CQ-06: Incidente de dado divergente (auditabilidade)

| Elemento | Conteúdo |
|---|---|
| Fonte | Cliente reclamando de saldo divergente |
| Estímulo | Necessidade de reconstruir a posição em uma data passada e explicar cada alteração |
| Ambiente | Investigação de incidente, até dois anos após o fato |
| Artefato | Ledger e trilha de auditoria |
| Resposta | Posição reconstruída exatamente, com origem, autor e instante de cada lançamento |
| **Medida** | Reconstrução em ≤ 1 hora por analista, sem acesso a backup (RNF-016, RNF-022) |

### CQ-07: Evolução do time (manutenibilidade)

| Elemento | Conteúdo |
|---|---|
| Fonte | Pessoa desenvolvedora recém-chegada ao time |
| Estímulo | Necessidade de adicionar um novo tipo de lançamento |
| Ambiente | Desenvolvimento |
| Artefato | Código, testes e documentação |
| Resposta | Ambiente local funcional e alteração entregue com testes |
| **Medida** | Ambiente operante em ≤ 15 min; alteração concluída em ≤ 1 dia (RNF-035, RNF-037) |

---

## 9. Riscos de qualidade

| ID | Risco | Probabilidade | Impacto | Mitigação | Indicador de alerta |
|---|---|---|---|---|---|
| R-01 | Contenção em conta quente acima do previsto | Média | Alto | Fila dedicada por conta para as contas identificadas; particionamento de lançamentos com consolidação | p99 de escrita por conta acima de 800 ms |
| R-02 | Premissa de capacidade (§3) divergir da realidade | **Alta** | Alto | Substituir premissas pela telemetria do legado antes do dimensionamento final | Diferença acima de 50% em qualquer dimensão |
| R-03 | Atraso crônico na geração de snapshot | Média | Médio | Gerar snapshot por gatilho de volume além do gatilho temporal; alerta por atraso | `entriesReplayed` p99 acima de 1.000 |
| R-04 | Crescimento do ledger sem política de arquivamento | Alta no longo prazo | Médio | Particionamento por tempo; definir retenção (QA-004) | Tamanho da partição ativa acima do limiar |
| R-05 | Consumidor de evento que presume entrega única | Média | Alto | Contrato explícito de ao menos uma vez; `message_id` estável; validação na homologação do consumidor | Duplicidade reportada por consumidor |
| R-06 | Acoplamento ao PostgreSQL via bloqueio de linha (`FOR NO KEY UPDATE`) e `SKIP LOCKED` | Média | Médio | Isolar a serialização atrás de abstração de domínio; documentar em ADR o custo de troca | Decisão corporativa de migração de SGBD |
| R-07 | Erosão da fronteira de escopo (§3.2 da EF) | **Alta** | Alto | Teste de arquitetura; revisão de ADR obrigatória para novo escopo | Requisito de cálculo de produto chegando a este sistema |

**R-07 merece ênfase.** O enunciado afirma que o legado "foi crescendo sem muito planejamento". Nenhuma arquitetura inicial impede isso; o que impede é a disciplina de recusar o requisito que não pertence ao sistema. É por isso que §3.2 da [EF](./EF-especificacao-funcional.md) lista o que está fora com justificativa, e não apenas o que está dentro.

---

## 10. Método de verificação por requisito

| Método | Requisitos verificados | Momento |
|---|---|---|
| Teste automatizado de integração | RNF-010, RNF-011, RNF-013, RNF-016, RNF-020, RNF-021, RNF-025, RNF-038 | Cada commit |
| Teste de concorrência | RNF-004 | Cada commit |
| Teste de carga | RNF-001, RNF-002, RNF-003, RNF-005, RNF-006, RNF-007, RNF-012 | Antes de cada publicação |
| Teste de caos (falha injetada) | RNF-014, RNF-015 | Ciclo de homologação |
| Análise estática e pipeline | RNF-024, RNF-026, RNF-034, RNF-035, RNF-036, RNF-037, RNF-039 | Cada commit |
| Inspeção e revisão | RNF-023, RNF-027, RNF-017, RNF-018 | Revisão de arquitetura |
| Telemetria em operação | RNF-030, RNF-031, RNF-032, RNF-033 | Contínuo |

**Princípio:** requisito não funcional verificado apenas por inspeção tende a regredir sem que ninguém perceba. A meta é migrar o máximo possível de itens da linha "inspeção" para as linhas automatizadas conforme o sistema amadurece.

---

## 11. Escopo de realização no desafio

O enunciado declara que não é necessário esgotar as possibilidades técnicas. Esta seção separa, com honestidade, o que será demonstrado do que será apenas especificado.

| Categoria | Realizado no código | Apenas especificado |
|---|---|---|
| Desempenho | RNF-006 na posição corrente (snapshot a cada 100 lançamentos, no máximo 99 somados, card 30.1) e na posição histórica (fechamento do dia anterior, somados só os lançamentos do dia consultado, card 32), com teste. RNF-003: os dois mecanismos limitam o custo, mas a latência nunca foi medida | RNF-001, RNF-002, RNF-005, RNF-007 (exigem ambiente de carga; card 30 encerrado como decisão); latência da RNF-003 |
| Resiliência | RNF-010, RNF-011, RNF-013, RNF-016 | RNF-014, RNF-015, RNF-017, RNF-018 |
| Segurança | RNF-020, RNF-021, RNF-022, RNF-024 (varredura de segredos no CI, card 31), RNF-025, RNF-026 (auditoria no restore, transitivas inclusive, card 31.1) | RNF-023, RNF-027 |
| Observabilidade | RNF-030 (traço ponta a ponta, card 33.1), RNF-031 (log JSON com correlação e mascarado, card 33), RNF-032 (seis métricas de negócio, inclusive a taxa de `computedFrom=ledger`, card 33.2); ADR-0012 | RNF-033 |
| Manutenibilidade | RNF-034, RNF-035, RNF-036 (86,4%, medida só pelos testes de domínio, em Release, com o `coverlet.msbuild` (10.1.0 desde o card 34, mesma medida) restrito ao assembly `PacioliBank.Ledger`; o CI reprova abaixo de 85%, card 31.2), RNF-037, RNF-038, RNF-039 | n/a |

Declarar essa separação é parte da entrega. Apresentar requisito especificado como se estivesse implementado seria, em contrato real, informação incorreta prestada ao cliente.

---

## 12. Rastreabilidade consolidada

| RNF | Atributo | RF relacionados | Cenários BDD | Cenário de qualidade |
|---|---|---|---|---|
| RNF-001 a RNF-007 | Desempenho e escala | RF-001 a RF-005, RF-010 | F04, F05, F07 | CQ-01, CQ-02 |
| RNF-010, RNF-011 | Resiliência e consistência | RF-006, RF-008, RF-011 | F03, F08 | CQ-03, CQ-04 |
| RNF-012 | Proteção de sobrecarga | RF-005, RF-009 | F05, F09 | CQ-01 |
| RNF-013 a RNF-016 | Operação e recuperação | RF-008, RF-010 | F08, F10 | CQ-03, CQ-06 |
| RNF-017, RNF-018 | Disponibilidade e recuperação | RF-008 | n/a | CQ-03 |
| RNF-020 a RNF-027 | Segurança e privacidade | RF-009 | F09 | CQ-05 |
| RNF-030 a RNF-033 | Observabilidade | RF-008, RF-010 | F08 | CQ-06 |
| RNF-034 a RNF-039 | Manutenibilidade | todos | todos | CQ-07 |

---

## 13. Decisões que esta especificação impõe aos ADRs

Esta seção é a ponte para `docs/adr/`. Cada item exige decisão formalizada e justificada.

| ADR previsto | Dirigido por |
|---|---|
| Estilo arquitetural e fronteiras de módulo | RNF-035, RNF-037, R-07 |
| Modelo de persistência append-only do ledger | EF RN-003, RNF-003, RNF-016, RNF-025 |
| Estratégia de controle de concorrência por conta | EF RN-001, RNF-004, CQ-02, R-01 |
| Mecanismo de idempotência | EF RN-005, RNF-011, CQ-04 |
| Estratégia de snapshot e projeção | RNF-002, RNF-003, RNF-006, R-03 |
| Publicação transacional de eventos (Outbox) | EF RF-011, RNF-010, RNF-011, CQ-03 |
| Representação de valor monetário | EF §8.2, RN-002 |
| Modelo de segurança e privilégio mínimo | RNF-020 a RNF-025, CQ-05 |
| Estratégia de teste e critério de bloqueio | RNF-036, BDD §7 |
| Escolha de plataforma e armazenamento | RNF-001, RNF-037, R-06 |

---

## 14. Histórico

| Versão | Data | Autor | Alteração |
|---|---|---|---|
| 1.0 | 2026-10-02 | Eduardo J. G. do Carmo | Versão inicial inferida a partir do enunciado do desafio |
| 1.1 | 2026-10-02 | Eduardo J. G. do Carmo | R-05: `eventId` substituído por `message_id`, o identificador efetivo (ADR-0008). R-06: descrevia acoplamento por bloqueio consultivo, opção **rejeitada** no ADR-0005; corrigido para o mecanismo adotado, bloqueio de linha, mais o `SKIP LOCKED` do despachante (card 21.1) |
| 1.2 | 2026-10-04 | Eduardo J. G. do Carmo | §11: RNF-036 movida de "realizado" para "apenas especificado". Nunca tinha sido medida; medida no card 31, a cobertura de linha do projeto de domínio é 56,8% com os testes de domínio e 74,9% somando os de integração, abaixo dos 85% (lacuna L-13). RNF-024 passa a verificada no CI; RNF-026 passa a realizada (card 31.1): alerta alto ou crítico em dependência, direta ou transitiva, reprova o restore |
| 1.3 | 2026-10-04 | Eduardo J. G. do Carmo | §11: RNF-036 volta a "realizado no código", agora medida: 86,4% de cobertura de linha no projeto de domínio, medida só pelos testes de domínio, em Release, com o `coverlet.msbuild` 6.0.2 restrito ao assembly `PacioliBank.Ledger`, com limite de 85% no CI (card 31.2, lacuna L-13 encerrada). Método registrado no ADR-0010 |
| 1.4 | 2026-10-04 | Eduardo J. G. do Carmo | §11: RNF-003 e RNF-006 deixam de constar como realizadas sem ressalva (lacuna L-15). RNF-006 vale só na posição corrente, por construção, ainda sem teste automatizado (card 30.1); na posição histórica não vale (card 32). A latência da RNF-003 nunca foi medida. RNF-001, RNF-002, RNF-005 e RNF-007 seguem especificadas e não verificadas (card 30 encerrado como decisão) |
| 1.5 | 2026-10-04 | Eduardo J. G. do Carmo | §11: RNF-006 na posição corrente passa a ter teste de integração (`SnapshotTests`, card 30.1), com três mutações detectadas |
| 1.6 | 2026-10-04 | Eduardo J. G. do Carmo | §11: RNF-031 e RNF-032 constavam como realizadas sem nada no código (lacuna L-16). RNF-031 passa a realizada de fato no card 33 (Serilog, JSON, correlação, mascaramento, com teste pelo caminho completo); RNF-032 volta a apenas especificada até o card 33.2; RNF-030, até o card 33.1 |
| 1.7 | 2026-10-05 | Eduardo J. G. do Carmo | §11: RNF-030 realizada (card 33.1): traço ponta a ponta com teste automatizado e inspeção no painel local |
| 1.8 | 2026-10-05 | Eduardo J. G. do Carmo | §11: RNF-032 realizada (card 33.2). "Atraso de snapshot" medido como lançamentos somados além dele: o snapshot é síncrono (ADR-0007) e não tem atraso de tempo |
| 1.9 | 2026-10-05 | Eduardo J. G. do Carmo | §11: a posição histórica deixa de somar todo o histórico (card 32, fechamento diário). RNF-006 passa a valer também nela; a latência da RNF-003 continua sem medição |

