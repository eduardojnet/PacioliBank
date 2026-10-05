# ADR-0014: Transferência entre contas numa transação local, com bloqueio em ordem de identificador e pernas amarradas pelo banco

- **Status:** Aceito e implementado (card 38)
- **Data:** 2026-10-05
- **Decisor:** Eduardo J. G. do Carmo
- **Requisitos dirigentes:** RF-012, RN-013, RN-001, RN-005, RN-007, regra 6 do projeto (invariante financeira garantida pelo banco), ENF §5.1 e §5.2

## Contexto e problema

A transferência estava fora do escopo por decisão ([ENF](../specs/ENF-especificacao-nao-funcional.md) §5.2): débito numa conta e crédito em outra exigem uma operação sobre dois agregados, e a conta é a unidade de consistência e de serialização ([ADR-0005](./ADR-0005-controle-de-concorrencia.md)). O usuário a trouxe para o escopo.

A posição registrada na ENF §5.2 era **saga com conta transitória de liquidação**, justificada como "o único modelo que permanece correto sob particionamento". O particionamento que existe hoje ([ADR-0013](./ADR-0013-particionamento-do-ledger.md)) é **por tempo, num banco único**. Não é por conta, e as duas contas de qualquer transferência estão sempre no mesmo banco.

Três perguntas:

1. **Consistência:** as duas pernas são atômicas, ou há estado intermediário visível?
2. **Concorrência:** como bloquear duas contas sem impasse entre transferências cruzadas (A para B e B para A ao mesmo tempo)?
3. **Integridade:** o que garante, no banco, que débito e crédito pertencem à mesma transferência, pelo mesmo valor, cada um na conta certa?

## Critérios de decisão

1. Nenhum instante em que o dinheiro saiu da origem e não chegou ao destino, visível a qualquer leitura
2. Sem impasse entre transferências concorrentes, por construção, e não por nova tentativa
3. A coerência entre as pernas é garantida pelo banco (regra 6), não pelo código
4. As regras de um lançamento (RN-001, RN-002, RN-005, RN-006, RN-007, RN-008) valem para cada perna sem ser reescritas
5. Contrato e eventos existentes não mudam; quem não usa transferência não percebe nada

## Opções consideradas

1. Transação local: as duas pernas na mesma transação, contas bloqueadas em ordem crescente de identificador
2. Saga com conta transitória de liquidação (posição da ENF §5.2)
3. Transação local com as contas bloqueadas na ordem do sentido (origem, depois destino)

## Decisão

**Débito na origem e crédito no destino numa única transação, com as duas contas bloqueadas em ordem crescente de identificador, e uma tabela `transfers` cujas chaves estrangeiras amarram as duas pernas.** Decisão do usuário, ao iniciar o card 38.

- **Ordem dos bloqueios.** O adaptador bloqueia primeiro a conta de menor identificador, depois a de maior, qualquer que seja o sentido. Duas transferências cruzadas pedem os bloqueios na mesma ordem: a segunda espera na primeira conta sem segurar nada, e não há ciclo. O lançamento comum bloqueia uma conta só e nunca fecha ciclo com uma transferência. Revisão registrada no [ADR-0005](./ADR-0005-controle-de-concorrencia.md)
- **Decisão no domínio.** O serviço de domínio `Transfer.Between` decide as duas pernas chamando `Account.Post` em cada agregado: saldo, situação, moeda, valor e chave são verificados pelas mesmas regras de um lançamento. O que ele acrescenta é o que só existe com duas contas: origem diferente do destino (RN-013), o mesmo valor, fato e correlação nas duas pernas, e a chave da perna de crédito. Toda rejeição acontece antes de qualquer gravação
- **Pernas são lançamentos comuns.** Cada perna entra no extrato, na posição, no fechamento diário, no snapshot e na fila de eventos da sua conta, pelo mesmo caminho de escrita. Os dois lançamentos têm o mesmo instante de registro
- **Amarração pelo banco** (migração 0004). `transfers` aponta para as duas pernas com chaves estrangeiras compostas que conferem **conta, sentido, valor, moeda e instante de registro**: `fk_transfers_debit` exige um débito da origem, e `fk_transfers_credit` um crédito do destino, os dois pelo valor e na moeda da transferência. `uq_transfers_debit` e `uq_transfers_credit` impedem que uma perna sirva a duas transferências; `ck_transfers_distinct_accounts` recusa origem igual ao destino. O alvo das chaves é `uq_entries_leg`, uma restrição única no ledger que inclui `recorded_at`, a coluna da partição, como o PostgreSQL exige ([ADR-0013](./ADR-0013-particionamento-do-ledger.md))
- **Idempotência.** A chave do chamador é da conta de origem: o registro de idempotência fica na origem e aponta para a perna de débito; a repetição é reconhecida sob os dois bloqueios e recebe o corpo gravado, byte a byte ([ADR-0006](./ADR-0006-idempotencia.md)). A perna de crédito recebe uma chave derivada, `transfer:<transferId>`: repetir a chave do chamador no destino colidiria com as chaves do titular do destino. A impressão do comando tem prefixo próprio (`transfer`) e inclui o sentido: a mesma chave usada num débito comum é reuso indevido
- **Contrato.** `POST /api/v1/transfers`, com as contas no corpo; nenhuma delas é o recurso. A resposta mostra ao pagador o próprio saldo e, do destino, só o identificador do lançamento: saldo e sequência do destino são do titular do destino. Origem igual ao destino é `400 SAME_ACCOUNT_TRANSFER`: o pedido é inválido em si, independente do estado das contas
- **Privilégios.** Aplicação com `SELECT, INSERT` em `transfers`; nada de `UPDATE` nem `DELETE` ([ADR-0009](./ADR-0009-seguranca-e-privilegio-minimo.md))

## Consequências

**Positivas**

- Atomicidade: a transferência aparece inteira ou não aparece. Nenhuma leitura vê dinheiro em trânsito
- Sem impasse por construção: com a ordem única, o teste de transferências cruzadas termina com todas concluídas; com a ordem do sentido, reprova
- Coerência das pernas no banco: uma transferência com valores diferentes, sentido trocado, conta errada ou perna reaproveitada é recusada pela restrição, e não pela esperança de que o código acerte
- Nenhuma regra de lançamento foi duplicada; o lançamento comum e o estorno não mudaram

**Negativas**

- A transferência segura dois bloqueios: a janela de contenção de uma conta passa a depender de outra. Uma conta muito disputada atrasa as transferências que a envolvem. [NVI] Não medido sob carga
- `uq_entries_leg` é um índice a mais no ledger inteiro, em cada partição, só para servir de alvo às chaves estrangeiras de `transfers`. [NVI] Custo de espaço e de escrita não medido
- Os eventos das pernas são os de um lançamento comum e **não levam o identificador da transferência**: um consumidor não junta as duas pernas pelo evento, só pela correlação, que é a mesma nas duas. Mudar o payload é mudar o contrato de eventos (EF §9), fora deste card
- Estorno de transferência não existe: estornar uma perna isolada é possível pelo caminho comum e desfaz metade da transferência. Regra pendente (QA-009)
- Sem autenticação (RF-009), qualquer chamador transfere de qualquer conta, como já debita de qualquer conta hoje

**Neutras**

- A fronteira transacional passa a admitir duas contas numa transação, sempre no mesmo banco. A garantia "forte, linearizável dentro de uma conta" da ENF §5.1 vale para cada conta; entre as duas contas de uma transferência, a garantia é atômica

## Análise das opções rejeitadas

**Saga com conta transitória de liquidação (opção 2, posição da ENF §5.2).** Débito da origem contra a conta transitória, depois crédito do destino a partir dela, com compensação se a segunda etapa falhar. Rejeitada pelos critérios 1 e 2: há estado intermediário visível (o dinheiro sai da origem e fica na transitória), exige um executor de etapas e compensação, e a conta transitória, como toda conta, é serializada pelo seu bloqueio: **toda transferência do sistema passaria a disputar a mesma linha**, um ponto único de contenção. O motivo que a recomendava, contas em partições distintas, não existe: o particionamento é por tempo, num banco só. *Voltaria a ser considerada* se as contas passarem a viver em bancos ou partições distintas por conta (fragmentação horizontal), ou se a transferência envolver um sistema externo.

**Bloqueio na ordem do sentido (opção 3).** Origem primeiro, destino depois. Rejeitada pelo critério 2: A para B e B para A simultâneas bloqueiam cada uma a sua origem e esperam pela outra. O PostgreSQL desfaz o impasse abortando uma delas depois de `deadlock_timeout`, e a nova tentativa a conclui: funciona, mas com segundo perdido a cada impasse e esgotamento sob contenção. Medido: com essa ordem, o teste de transferências cruzadas reprova.

**Bloquear as duas contas num só comando (`WHERE account_id IN (...) ORDER BY account_id FOR NO KEY UPDATE`).** A ordem de bloqueio passaria a depender do plano de execução, que não é contratual, e o carregamento da posição de cada conta teria de ser separado do bloqueio. Rejeitada por pouco ganho: um comando a menos por transferência. *Voltaria a ser considerada* se a latência da transferência aparecesse no p99.

**Coluna `transfer_id` nas linhas do ledger, no lugar da tabela.** Exigiria alterar a tabela particionada e, para garantir no banco que as duas pernas de um mesmo `transfer_id` têm o mesmo valor e sentidos opostos, gatilho ou restrição de exclusão entre linhas, que o PostgreSQL não oferece de forma declarativa. Rejeitada pelo critério 3: a tabela com chaves estrangeiras compostas garante a coerência de forma declarativa.

**Gatilho no lugar das chaves estrangeiras compostas.** Evitaria o índice `uq_entries_leg`. Rejeitada: gatilho é código no banco, com ordem de execução e privilégio a cuidar, e a chave estrangeira diz o mesmo de forma declarativa e verificável no catálogo. *Voltaria a ser considerada* se o custo de `uq_entries_leg` aparecer na escrita ou no espaço.

**A chave do chamador também na perna de crédito.** Colidiria com as chaves que o titular do destino usa nos próprios lançamentos (`uq_entries_idempotency` é por conta), e a transferência seria recusada por uma chave que o pagador nem conhece. Rejeitada.

**Rota sob a conta (`POST /accounts/{origem}/transfers`).** Coerente com as rotas de escrita existentes, mas trata a origem como o recurso e o destino como atributo. Rejeitada: a transferência é um recurso próprio, de duas contas. *Voltaria a ser considerada* quando a autorização por titularidade (RF-009) existir, se a política de acesso precisar da origem na rota.

**Saldo e sequência do destino na resposta.** Rejeitados: o pagador veria a posição de uma conta que não é dele, contra o princípio do [ADR-0009](./ADR-0009-seguranca-e-privilegio-minimo.md).

## Validação

- `TransferTests` de domínio (12): as duas pernas, mesmo valor, fato e correlação; chave do chamador no débito e derivada no crédito; mesma conta, saldo insuficiente, origem ou destino inativos, valor não positivo e chave ausente recusados
- `TransferTests` de integração (18), PostgreSQL real: valor movido e soma preservada; cada perna no extrato e na fila de eventos da sua conta; saldo insuficiente, destino inexistente e mesma conta sem gravar nada; **falha provocada no banco ao gravar a perna de crédito desfaz a de débito**; reenvio com a mesma chave devolve o corpo original mesmo com a origem já sem saldo; chave reutilizada com outro conteúdo, inclusive num débito comum, é conflito; envios simultâneos com a mesma chave geram uma transferência; **contas bloqueadas em ordem crescente qualquer que seja o sentido**, verificado de forma determinística (com a conta maior presa por outra transação, a menor já está bloqueada pela transferência); transferências cruzadas simultâneas terminam todas com a soma preservada; o banco recusa pernas de valores diferentes, perna de débito que é crédito, perna de outra conta, perna reaproveitada e origem igual ao destino, com controle positivo; a aplicação não altera nem apaga transferência
- `TransferEndpointTests` (2): 201, reenvio 200 com `Idempotency-Replayed` e o mesmo corpo, e os códigos `SAME_ACCOUNT_TRANSFER`, `INSUFFICIENT_FUNDS`, `ACCOUNT_NOT_FOUND` e `INVALID_REQUEST`
- Poder de detecção, oito mutações, cada uma reprovada pelo seu teste: bloqueio na ordem do sentido; débito confirmado numa transação e crédito em outra; repetição não reconhecida sob o bloqueio; chave estrangeira do crédito só pelo identificador; a do débito só pelo identificador; perna reaproveitável; origem igual ao destino aceita; aplicação com `UPDATE` e `DELETE`
- Coleção do Insomnia com três requisições novas (transferência, reenvio, mesma conta): 18 requisições e 52 testes, verdes duas vezes seguidas contra o `docker compose`
- No ambiente local, a 0004 foi aplicada sobre o volume em uso, sem recriar o banco

## Gatilho de revisão

1. Contas em bancos ou partições distintas por conta: volta a saga (opção 2)
2. QA-009 respondida: estorno de transferência, tarifa, limites ou moedas diferentes
3. Contenção de transferências sobre uma mesma conta aparecendo no p99 de escrita
4. Custo de `uq_entries_leg` aparecendo na escrita ou no espaço do ledger
5. Consumidor de eventos que precise juntar as pernas pela transferência: o identificador entra no payload, com versão do contrato
