# ADR-0008: Publicar eventos de integração por Outbox transacional, com entrega ao menos uma vez

- **Status:** Aceito
- **Data:** 2026-10-02
- **Decisor:** Eduardo J. G. do Carmo
- **Requisitos dirigentes:** RF-008, RF-011, RNF-010, RNF-011, CQ-03

## Contexto e problema

Movimentação financeira interessa a outros sistemas: extrato, notificação ao cliente, antifraude, contabilidade. Sem publicação de eventos, esses sistemas fariam consulta periódica ao banco de dados, reproduzindo exatamente o padrão de carga que degrada o legado.

O problema não é publicar. É publicar **de forma consistente com a gravação**. Há duas falhas simétricas, ambas graves:

- **Evento fantasma:** publicar antes de confirmar a transação, e a transação falhar. Consumidores reagem a um lançamento que não existe. O cliente recebe notificação de um débito que não ocorreu
- **Evento perdido:** confirmar a transação e falhar ao publicar. O lançamento existe e ninguém sabe. O extrato diverge do saldo

As duas decorrem da mesma causa: gravar no banco e publicar no barramento são dois sistemas distintos, e não há como torná-los atômicos sem commit em duas fases, que traz problemas próprios maiores que os que resolve.

## Critérios de decisão

1. Impossibilidade de evento fantasma e de evento perdido (RNF-011)
2. Indisponibilidade do barramento não impede o registro de lançamentos (RNF-010, CQ-03)
3. Retomada automática após restabelecimento, sem intervenção
4. Sem commit distribuído
5. Mínimo de infraestrutura adicional para o escopo atual

## Opções consideradas

1. Publicação direta após a confirmação da transação
2. Commit em duas fases entre banco e barramento
3. Outbox transacional com entrega por processo de leitura
4. Captura de dados de alteração a partir do log de transações (Debezium)
5. Sem eventos, com consulta periódica pelos consumidores

## Decisão

**Outbox transacional.** O evento é gravado em uma tabela do mesmo banco, na mesma transação do lançamento. Um processo separado lê a tabela e publica, marcando o que foi entregue.

```sql
CREATE TABLE outbox_messages (
    message_id     uuid         PRIMARY KEY,
    account_id     uuid         NOT NULL,
    sequence       bigint       NOT NULL,
    event_type     text         NOT NULL,
    payload        jsonb        NOT NULL,
    occurred_at    timestamptz  NOT NULL,
    published_at   timestamptz  NULL,
    attempts       smallint     NOT NULL DEFAULT 0,
    next_attempt_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_outbox_pending
    ON outbox_messages (next_attempt_at)
    WHERE published_at IS NULL;
```

O índice parcial sobre mensagens pendentes mantém a varredura proporcional à fila, não ao histórico: mensagens já publicadas saem do índice.

### Consumo

```sql
BEGIN;
SELECT * FROM outbox_messages
 WHERE published_at IS NULL AND next_attempt_at <= now()
 ORDER BY occurred_at
 LIMIT 100
   FOR UPDATE SKIP LOCKED;
-- publica no barramento
UPDATE outbox_messages SET published_at = now() WHERE message_id = ANY(@ids);
COMMIT;
```

`FOR UPDATE SKIP LOCKED` permite múltiplos processos consumindo em paralelo sem disputa: cada um ignora as linhas já reservadas por outro. É o que torna o despachante escalável sem coordenação externa.

Em falha de publicação, `attempts` é incrementado e `next_attempt_at` recebe recuo exponencial. Após o limite de tentativas, a mensagem é marcada para inspeção e alerta é emitido, sem bloquear a fila.

### Garantia oferecida: ao menos uma vez

A confirmação de publicação e a marcação em banco não são atômicas. Falha entre uma e outra produz republicação. **Entrega exatamente uma vez não é oferecida, porque não é obtível sem coordenação que custa mais do que vale.**

O contrato com o consumidor é explícito ([EF](../specs/EF-especificacao-funcional.md) §9):

- `message_id` é estável entre republicações, permitindo deduplicação
- O consumidor é responsável por processar de forma idempotente
- Ordenação é garantida **apenas dentro da mesma conta**, pelo campo `sequence`

Ordenação global não é oferecida porque exigiria um único despachante serializado, eliminando a escala do consumo para resolver um problema que nenhum consumidor identificado tem.

### Escolha do barramento

No escopo do desafio, o publicador é uma abstração com duas implementações: uma que registra em log, usada no ambiente local, e uma de barramento real. O barramento concreto não é decidido aqui porque depende da plataforma de mensageria do banco, que não é informação disponível. A decisão arquitetural é o **padrão**, não o produto.

Essa separação é deliberada: trocar o barramento não toca o domínio nem o mecanismo de consistência.

## Consequências

**Positivas**

- Evento fantasma e evento perdido eliminados por construção: o evento existe se e somente se o lançamento existe
- Barramento indisponível não afeta a escrita; as mensagens acumulam e são publicadas na retomada (CQ-03)
- Retomada automática, sem intervenção, atendendo ao critério 3
- Sem commit distribuído
- Nenhuma infraestrutura adicional além do banco já existente para o escopo atual

**Negativas**

- **Latência adicional entre o lançamento e a publicação**, equivalente ao intervalo de leitura do despachante. Aceitável para os consumidores previstos; inadequado para caso de uso que exija reação em tempo real, que precisaria de outra abordagem
- A tabela de outbox cresce e exige expurgo das mensagens publicadas, sob pena de inchaço
- Um processo adicional a implantar e monitorar. É a exceção consciente ao princípio de minimizar componentes assíncronos adotado no [ADR-0007](./ADR-0007-snapshot-e-projecao.md): aqui o assincronismo é inerente ao problema, porque o barramento é externo e pode estar fora do ar. No snapshot, não era
- Consumidor que presumir entrega única estará incorreto. É o risco R-05, e a mitigação é contratual: validação de idempotência na homologação de cada consumidor
- Carga adicional de leitura sobre o banco de dados primário

**Neutras**

- O despachante pode ser executado no mesmo artefato da API, como serviço hospedado, ou separadamente a partir do mesmo binário. A decisão de topologia é operacional, não arquitetural
- Profundidade da fila de outbox é métrica de saúde relevante (RNF-032)

## Análise das opções rejeitadas

**Publicação direta após a confirmação.** Rejeitada pelo critério 1: a janela entre confirmar e publicar produz evento perdido. É silenciosa, rara e, por isso mesmo, descoberta tarde, normalmente pelo cliente.

**Commit em duas fases.** Rejeitado pelo critério 4. Introduz coordenador como ponto único de falha, bloqueio prolongado de recursos e estado em dúvida que exige resolução manual. O remédio é pior que a doença.

**Captura a partir do log de transações (Debezium).** Tecnicamente elegante: nenhuma tabela adicional, nenhum código de publicação, latência menor. Rejeitada pelo critério 5, por exigir Kafka Connect, configuração de replicação lógica e operação de um conector, o que é infraestrutura desproporcional ao escopo e inviabiliza RNF-037. *Voltaria a ser a melhor escolha* se o banco já operasse essa plataforma, ou se o volume tornasse a leitura do outbox custosa.

**Sem eventos, com consulta periódica.** Rejeitada por recriar no novo sistema o padrão de carga que degrada o legado, e por impor latência proporcional ao intervalo de consulta de cada consumidor.

## Validação

- [BDD](../specs/BDD-comportamento.md) F08: barramento indisponível não impede o lançamento; evento permanece pendente e é publicado na retomada
- Teste de injeção de falha entre a confirmação e a publicação, verificando republicação com `message_id` estável
- Teste de consumo paralelo com múltiplos despachantes, verificando ausência de processamento duplicado por `SKIP LOCKED`
- Conciliação periódica entre `ledger_entries` e `outbox_messages`, alertando qualquer lançamento sem mensagem correspondente (RNF-033)

## Gatilho de revisão

1. Requisito de consumidor que exija latência incompatível com a leitura periódica
2. Carga do despachante tornando-se relevante sobre o banco primário, que levaria a reconsiderar a captura por log
3. Definição da plataforma de mensageria do banco, que fixa a implementação do publicador
4. Duplicidade reportada por consumidor (R-05), indicando falha na validação de idempotência na homologação
