# ADR-0005: Serializar escritas por conta com bloqueio pessimista de linha, com constraint única como garantia estrutural

- **Status:** Aceito
- **Data:** 2026-10-02
- **Decisor:** Eduardo J. G. do Carmo
- **Requisitos dirigentes:** RN-001, RN-006, RNF-004, RNF-005, CQ-02, R-01, R-06

## Contexto e problema

Esta é a decisão mais consequente do projeto. É ela que determina se a invariante "a posição nunca é negativa" é uma garantia ou uma esperança.

O problema concreto: registrar um débito exige ler a posição, validar que há saldo e gravar o lançamento. São três passos. Se dois débitos executarem esses passos de forma intercalada, ambos leem a mesma posição, ambos passam na validação, e ambos gravam. A conta fica negativa. É a condição de corrida clássica de leitura e escrita, e em sistema bancário ela produz dano financeiro direto.

Agrava o problema o fato de que a sequência por conta (RN-006) também é um ciclo de leitura e escrita: obter a próxima sequência exige conhecer a última.

Há uma observação que orienta toda a análise: **a contenção existe apenas dentro de uma mesma conta**. Pelas premissas da [ENF](../specs/ENF-especificacao-nao-funcional.md) §3, a concorrência típica por conta é de uma a duas operações simultâneas, e apenas um conjunto reduzido de contas institucionais chega a dezenas. Serializar escritas por conta custa quase nada na grande maioria dos casos e preserva escala horizontal, porque contas distintas não se bloqueiam.

## Critérios de decisão

1. Garante a invariante de posição não negativa sob qualquer grau de concorrência
2. Garante sequência monotônica sem lacuna nem duplicata (RN-006)
3. Não introduz contenção entre contas distintas (RNF-005)
4. Falha de forma previsível, convertendo conflito em nova tentativa invisível ao chamador (RNF-004)
5. Mantém portabilidade razoável entre SGBDs (R-06)

## Opções consideradas

1. Controle otimista com constraint única e nova tentativa
2. Bloqueio pessimista de linha na conta (`SELECT ... FOR UPDATE`)
3. Bloqueio consultivo por conta (`pg_advisory_xact_lock`)
4. Isolamento `SERIALIZABLE` para toda a transação
5. Fila com escritor único por conta

## Decisão

**Bloqueio pessimista de linha sobre o registro da conta, dentro da transação de escrita**, complementado pela constraint `UNIQUE (account_id, sequence)` como garantia estrutural independente.

Sequência de execução:

```sql
BEGIN;
SET LOCAL lock_timeout = '3s';

-- 1. Serializa escritas desta conta e carrega o estado de controle
SELECT status, currency, last_sequence
  FROM accounts
 WHERE account_id = @accountId
   FOR NO KEY UPDATE;

-- 2. Posição corrente (snapshot + delta), ver ADR-0007
-- 3. Valida RN-001 para débito; em falha, ROLLBACK sem consumir sequência

-- 4. Grava o lançamento
INSERT INTO ledger_entries (...) VALUES (..., @lastSequence + 1, ...);

-- 5. Avança o controle de sequência
UPDATE accounts
   SET last_sequence = last_sequence + 1
 WHERE account_id = @accountId;

-- 6. Registro de idempotência (ADR-0006) e outbox (ADR-0008), mesma transação
COMMIT;
```

### Por que o bloqueio de linha, e não as alternativas

**A linha da conta já seria bloqueada de qualquer forma.** O passo 5 atualiza `accounts.last_sequence`, o que adquire bloqueio exclusivo sobre aquela linha. Antecipar esse bloqueio para o início da transação, com `FOR NO KEY UPDATE`, não acrescenta custo algum: apenas move para o começo um bloqueio que ocorreria ao final, fechando a janela da condição de corrida. É a solução que resolve o problema sem introduzir nenhum mecanismo novo.

`FOR NO KEY UPDATE` em vez de `FOR UPDATE`: a variante mais fraca não bloqueia leituras de chave estrangeira sobre a conta, reduzindo interferência com outras operações do sistema sem perder a exclusividade entre escritores.

### Revisão de posição registrada

A análise preliminar deste projeto recomendou **diferenciar o tratamento**: bloqueio pessimista para débitos, que validam saldo, e controle otimista para créditos, que não validam.

**Essa recomendação foi revista e rejeitada.** Dois motivos:

1. Crédito também executa leitura e escrita sobre `last_sequence`. Não é uma operação livre de contenção; apenas não valida saldo.
2. A duplicação de caminhos de concorrência dobraria a superfície de teste da parte mais crítica do sistema, em troca de um ganho que o bloqueio de linha, por ser curto e sem I/O, não justifica.

O registro desta revisão é intencional: em arquitetura, a micro-otimização que complica o caminho crítico quase sempre é o erro mais caro, e mudar de posição com justificativa é preferível a sustentar uma recomendação por consistência retórica.

### Função da constraint única

`UNIQUE (account_id, sequence)` não é redundância decorativa. É a garantia que sobrevive a qualquer defeito do código de aplicação, a qualquer processo paralelo não previsto e a qualquer manutenção futura que altere a lógica de bloqueio. Se o bloqueio falhar, a gravação é recusada pelo banco; o sistema erra parando, não corrompendo.

Defesa em profundidade aqui significa que a correção não depende de nenhuma camada única estar certa.

### Tratamento de falhas

| Situação | Comportamento |
|---|---|
| Conflito de sequência (constraint violada) | Nova tentativa automática com recuo exponencial e aleatorização, até 3 tentativas |
| Esgotamento das tentativas | `503 SERVICE_UNAVAILABLE`, marcado como repetível; seguro porque a chave de idempotência protege o reenvio |
| `lock_timeout` atingido | Tratado como conflito; mesma política de nova tentativa |
| Deadlock | Impossível no escopo atual: cada transação bloqueia exatamente uma linha de conta. Quando transferência entrar em escopo, as contas devem ser bloqueadas em ordem determinística de identificador |

### Mitigação do inchaço de linha

Cada `UPDATE` em `accounts` gera nova versão de linha no modelo MVCC do PostgreSQL. Em conta de alto movimento, isso produz inchaço e pressão de vacuum.

Duas mitigações aplicadas:

- **`last_sequence` não participa de nenhum índice.** Isso habilita atualização HOT, em que a nova versão da linha permanece na mesma página e não exige atualizar índices.
- **`fillfactor = 70` na tabela `accounts`**, reservando espaço na página para as versões sucessivas.

## Consequências

**Positivas**

- Invariante de posição não negativa garantida sob qualquer concorrência, verificável por teste
- Sequência íntegra, garantida de forma independente pelo banco
- Contas distintas não se bloqueiam, preservando escala horizontal (RNF-005)
- Caminho de escrita único, com superfície de teste reduzida
- `FOR UPDATE` tem equivalente direto em todos os SGBDs relacionais relevantes, atendendo ao critério 5

**Negativas**

- **Escritas na mesma conta são serializadas.** É a consequência pretendida, mas significa que uma conta institucional de altíssimo movimento tem vazão limitada pela duração da transação. É o risco R-01
- Transação longa amplia a janela de bloqueio. Exige disciplina: nenhuma chamada externa, nenhum I/O de rede e nenhum processamento pesado entre `BEGIN` e `COMMIT`
- Inchaço de linha na tabela `accounts`, mitigado mas não eliminado
- `lock_timeout` precisa ser calibrado: muito curto gera rejeição desnecessária, muito longo esgota o pool de conexões sob contenção

**Neutras**

- A duração do bloqueio é dominada pela leitura da posição, o que torna o [ADR-0007](./ADR-0007-snapshot-e-projecao.md) parte do desenho de concorrência, não apenas de desempenho
- A nova tentativa é invisível ao chamador, mas visível na telemetria como métrica de conflito

## Análise das opções rejeitadas

**Controle otimista puro.** Ler `last_sequence`, calcular e inserir confiando na constraint para detectar conflito, repetindo em caso de falha. Rejeitado pelo critério 4: sob contenção, a taxa de nova tentativa cresce e o trabalho útil cai, porque toda a validação de saldo é refeita a cada tentativa. Em conta quente, degrada de forma não linear. *Voltaria a ser a melhor escolha* se a contenção por conta fosse comprovadamente próxima de zero e o custo do bloqueio se tornasse mensurável.

**Bloqueio consultivo (`pg_advisory_xact_lock`).** Tecnicamente adequado e, em alguns aspectos, mais limpo: bloqueia o conceito "escrita nesta conta" sem tocar a linha, eliminando o inchaço. Rejeitado por dois motivos: exige mapear o identificador da conta para `bigint`, e um mapeamento por hash introduz colisão, que faz duas contas distintas disputarem o mesmo bloqueio, violando o critério 3 de forma silenciosa (a correção se mantém, o desempenho não). Resolver a colisão exigiria uma coluna dedicada de chave de bloqueio, acrescentando um conceito ao modelo. Somado ao acoplamento específico ao PostgreSQL (R-06), o custo supera o ganho, já que a linha da conta seria bloqueada de qualquer modo. *Voltaria a ser a melhor escolha* se o controle de sequência deixasse de residir em uma linha atualizada, ou se o inchaço se mostrasse limitante.

**Isolamento `SERIALIZABLE`.** Rejeitado porque o PostgreSQL o implementa com detecção otimista de conflito: sob contenção, a transação é abortada com erro de serialização e precisa ser inteiramente refeita. Tem a mesma fragilidade do controle otimista, com custo adicional em todas as transações, inclusive as que não concorrem.

**Fila com escritor único por conta.** Garante serialização com vazão previsível e elimina o bloqueio. Rejeitado pelo custo operacional: exige roteamento estável de conta para partição, tratamento de rebalanceamento e inversão do modelo de API para assíncrono, o que altera o contrato com todos os originadores. *É a mitigação planejada para R-01*, aplicável seletivamente apenas às contas quentes identificadas, sem alterar o caminho das demais.

## Validação

- [BDD](../specs/BDD-comportamento.md) F07, obrigatoriamente executado com paralelismo real e barreira de sincronização, contra PostgreSQL em Testcontainers
- Cenário decisivo: 50 débitos simultâneos sobre saldo que comporta 10. Exatamente 10 aceitos, posição final zero, nunca negativa
- Cenário de sequência: 200 créditos simultâneos produzindo a série contínua de 1 a 200
- Cenário de independência: operações simultâneas em 100 contas distintas sem espera por bloqueio
- Métrica de conflito e de nova tentativa exposta para monitorar RNF-004

**Advertência de método:** um teste de concorrência executado contra repositório em memória passa na implementação ingênua e, portanto, não testa nada. A exigência de banco real neste cenário é parte da decisão, não detalhe de infraestrutura.

## Gatilho de revisão

1. p99 de escrita em conta específica ultrapassando 800 ms (R-01), indicando necessidade de fila dedicada para contas quentes
2. Entrada de transferência entre contas em escopo, exigindo ordenação determinística de bloqueios
3. Inchaço da tabela `accounts` exigindo intervenção de vacuum fora do automático
4. Migração de SGBD, que exige revalidar a semântica equivalente de bloqueio
