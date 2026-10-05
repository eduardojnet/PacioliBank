# BDD: Especificação por Comportamento

**Projeto:** Sistema de Movimentações Financeiras e Posição Consolidada
**Documento:** 1 de 3 do pacote de especificação
**Versão:** 1.2
**Data:** 2026-10-04
**Status:** Proposto

**Documentos relacionados:**

- [Especificação Funcional](./EF-especificacao-funcional.md) (EF): define `RF-xxx`, `RN-xxx`, modelo de domínio e contratos de API
- [Especificação Não Funcional](./ENF-especificacao-nao-funcional.md) (ENF): define `RNF-xxx`, SLOs e cenários de atributo de qualidade
- `docs/adr/` (a produzir): decisões arquiteturais que implementam estes comportamentos

---

## 1. Propósito deste documento

Este documento é a **fonte da verdade sobre comportamento observável**. Ele existe por três motivos:

1. **Alinhamento:** cenários em linguagem natural são legíveis por negócio, produto, QA e engenharia sem tradução.
2. **Executabilidade:** cada cenário vira teste automatizado via Reqnroll, atendendo ao requisito obrigatório de testes do desafio.
3. **Rastreabilidade:** cada cenário referencia o requisito funcional (`RF`) e a regra de negócio (`RN`) que valida. Requisito sem cenário é requisito não verificado.

**Precedência:** quando este documento e a EF divergirem, prevalece a EF para *definição* e este documento para *critério de aceite*. Divergência deve ser tratada como defeito de especificação, não resolvida informalmente.

**Escopo de inferência:** o enunciado do desafio descreve o problema em três parágrafos. Tudo além disso é inferência deliberada deste autor, marcada como `[INFERIDO]` na EF e sujeita a validação com o negócio. Nenhum número de volume, SLA ou política de produto foi inventado como fato.

---

## 2. Convenções

### 2.1 Idioma

Gherkin escrito em português (`# language: pt`), suportado nativamente por Reqnroll e SpecFlow. Justificativa: a especificação precisa ser lida por áreas de negócio e compliance do banco, não apenas por engenharia. O código-fonte permanece em inglês.

### 2.2 Tags

| Tag | Significado |
|---|---|
| `@RF-xxx` | Requisito funcional validado pelo cenário (ver EF §6) |
| `@RN-xxx` | Regra de negócio validada pelo cenário (ver EF §5) |
| `@RNF-xxx` | Requisito não funcional validado (ver ENF) |
| `@critico` | Falha gera impacto financeiro direto ao cliente; bloqueia release |
| `@concorrencia` | Exige execução paralela real, não sequencial |
| `@integracao` | Exige banco de dados real (Testcontainers), não in-memory |
| `@unidade` | Executável apenas com o domínio, sem infraestrutura |

### 2.3 Formato de valores monetários

Valores aparecem nos cenários como string decimal com duas casas (`"150.00"`). Nunca como número de ponto flutuante. Esta convenção é vinculante também no contrato de API (ver EF §8.2) e existe para eliminar perda de precisão em parsers JSON que usam IEEE-754.

### 2.4 Identificadores nos cenários

Contas são referenciadas por apelido legível (`CONTA-A`), resolvido pelo passo de setup para um `AccountId` real. Isto mantém o cenário legível sem acoplar a especificação a formato de identificador.

---

## 3. Linguagem ubíqua

Os termos abaixo têm significado fixo em todos os três documentos e no código. Sinônimos não são permitidos.

| Termo | Definição |
|---|---|
| **Lançamento** (*LedgerEntry*) | Registro imutável de um fato financeiro em uma conta. Nunca é alterado nem excluído após gravado. |
| **Crédito** | Lançamento que aumenta a posição da conta. |
| **Débito** | Lançamento que reduz a posição da conta. |
| **Posição consolidada** (*Balance*) | Valor derivado da soma dos lançamentos de uma conta até um instante. Não é um dado armazenado como verdade primária. |
| **Sequência** (*Sequence*) | Inteiro monotônico crescente, sem lacunas, único por conta, que ordena os lançamentos daquela conta. |
| **Snapshot** | Posição pré-calculada de uma conta até uma sequência específica. Otimização de leitura, descartável e reconstruível. |
| **Chave de idempotência** | Identificador fornecido pelo chamador que torna a repetição de um comando inofensiva. |
| **Estorno** | Lançamento de sinal oposto que anula o efeito de um lançamento anterior. Nunca exclusão. |
| **Data do fato** (*occurredAt*) | Instante em que o evento financeiro ocorreu no mundo real. |
| **Data do registro** (*recordedAt*) | Instante em que o lançamento foi gravado no ledger. Atribuído pelo sistema, nunca pelo chamador. |
| **Conta quente** | Conta com alta taxa de lançamentos concorrentes. Única fonte real de contenção no sistema. |

---

## 4. Mapa de funcionalidades

| ID | Funcionalidade | Requisitos cobertos | Criticidade |
|---|---|---|---|
| F01 | Registro de crédito | RF-001, RF-006 | Crítica |
| F02 | Registro de débito | RF-002, RF-006 | Crítica |
| F03 | Idempotência de comandos | RF-006 | Crítica |
| F04 | Consulta de posição consolidada | RF-003, RF-004 | Crítica |
| F05 | Consulta de extrato | RF-005 | Alta |
| F06 | Estorno de lançamento | RF-007 | Alta |
| F07 | Integridade sob concorrência | RF-002, RN-001, RN-006 | Crítica |
| F08 | Degradação graciosa e falha parcial | RF-008, RNF-010 | Alta |
| F09 | Controle de acesso e proteção de dados | RF-009, RNF-020 | Crítica |
| F10 | Consistência do snapshot | RF-010 | Alta |

---

## 5. Funcionalidades

### F01: Registro de crédito

```gherkin
# language: pt
@F01 @critico
Funcionalidade: Registro de crédito em conta
  Como sistema originador de transações do banco
  Quero registrar entradas de valor na conta de um cliente
  Para que a posição consolidada do cliente reflita os recursos disponíveis

  Contexto:
    Dado que existe a conta "CONTA-A" ativa com saldo "0.00"

  @RF-001 @RN-002 @unidade
  Cenário: Crédito válido aumenta a posição da conta
    Quando for registrado um crédito de "150.00" na conta "CONTA-A"
    Então o lançamento deve ser aceito
    E a posição consolidada de "CONTA-A" deve ser "150.00"
    E o lançamento deve receber a sequência 1

  @RF-001 @RN-006 @integracao
  Cenário: Créditos sucessivos recebem sequência monotônica sem lacunas
    Quando forem registrados os créditos na conta "CONTA-A":
      | valor  |
      | 100.00 |
      | 250.50 |
      | 0.01   |
    Então as sequências atribuídas devem ser 1, 2 e 3
    E a posição consolidada de "CONTA-A" deve ser "350.51"

  @RF-001 @RN-002 @unidade
  Esquema do Cenário: Crédito com valor inválido é rejeitado
    Quando for registrado um crédito de "<valor>" na conta "CONTA-A"
    Então o lançamento deve ser rejeitado com o código "INVALID_AMOUNT"
    E a posição consolidada de "CONTA-A" deve permanecer "0.00"

    Exemplos:
      | valor   |
      | 0.00    |
      | -10.00  |
      | 10.001  |

  @RF-001 @RN-007 @unidade
  Cenário: Crédito em moeda divergente da conta é rejeitado
    Dado que a conta "CONTA-A" opera em "BRL"
    Quando for registrado um crédito de "100.00" em "USD" na conta "CONTA-A"
    Então o lançamento deve ser rejeitado com o código "CURRENCY_MISMATCH"

  @RF-001 @RN-008 @integracao
  Cenário: Crédito em conta inativa é rejeitado
    Dado que a conta "CONTA-A" está encerrada
    Quando for registrado um crédito de "100.00" na conta "CONTA-A"
    Então o lançamento deve ser rejeitado com o código "ACCOUNT_INACTIVE"

  @RF-001 @RN-003 @integracao
  Cenário: Lançamento gravado não pode ser alterado
    Dado que foi registrado um crédito de "100.00" na conta "CONTA-A"
    Quando for tentada a alteração direta do valor do lançamento no armazenamento
    Então a operação deve ser recusada pelo mecanismo de persistência
```

**Nota de implementação:** o último cenário valida `RN-003` no nível de infraestrutura. A recusa deve vir de privilégio negado no banco de dados (ausência de `GRANT UPDATE`/`DELETE` para o usuário de aplicação), não de validação em código. Imutabilidade garantida por disciplina de desenvolvedor não é garantia.

---

### F02: Registro de débito

```gherkin
# language: pt
@F02 @critico
Funcionalidade: Registro de débito em conta
  Como sistema originador de transações do banco
  Quero registrar saídas de valor na conta de um cliente
  Para que recursos sejam movimentados sem jamais exceder o disponível

  Contexto:
    Dado que existe a conta "CONTA-A" ativa com saldo "1000.00"

  @RF-002 @unidade
  Cenário: Débito dentro do saldo disponível é aceito
    Quando for registrado um débito de "300.00" na conta "CONTA-A"
    Então o lançamento deve ser aceito
    E a posição consolidada de "CONTA-A" deve ser "700.00"

  @RF-002 @RN-001 @critico @unidade
  Cenário: Débito superior ao saldo disponível é rejeitado
    Quando for registrado um débito de "1000.01" na conta "CONTA-A"
    Então o lançamento deve ser rejeitado com o código "INSUFFICIENT_FUNDS"
    E a resposta deve informar o saldo disponível "1000.00"
    E a posição consolidada de "CONTA-A" deve permanecer "1000.00"
    E nenhum lançamento deve ter sido gravado

  @RF-002 @RN-001 @unidade
  Cenário: Débito exatamente igual ao saldo zera a conta e é aceito
    Quando for registrado um débito de "1000.00" na conta "CONTA-A"
    Então o lançamento deve ser aceito
    E a posição consolidada de "CONTA-A" deve ser "0.00"

  @RF-002 @RN-001 @integracao
  Cenário: Rejeição por saldo insuficiente não consome sequência
    Quando for registrado um débito de "5000.00" na conta "CONTA-A"
    E for registrado um débito de "100.00" na conta "CONTA-A"
    Então o segundo lançamento deve receber a sequência 2
```

**Nota de especificação:** o último cenário fixa que a sequência é consumida apenas por lançamento efetivamente gravado. Lacunas na sequência inviabilizariam a detecção de perda de registro em auditoria, que é a razão de existir da sequência (ver EF §5, RN-006).

---

### F03: Idempotência de comandos

```gherkin
# language: pt
@F03 @critico
Funcionalidade: Idempotência no registro de movimentações
  Como sistema cliente sujeito a timeout, retry e reentrega de mensagem
  Quero repetir um comando com segurança
  Para que falha de rede jamais produza débito ou crédito em duplicidade

  Contexto:
    Dado que existe a conta "CONTA-A" ativa com saldo "500.00"

  @RF-006 @RN-005 @critico @integracao
  Cenário: Repetição do mesmo comando não duplica o lançamento
    Dado que foi registrado um débito de "100.00" na conta "CONTA-A" com a chave de idempotência "chave-001"
    Quando o mesmo comando for reenviado com a chave de idempotência "chave-001"
    Então a resposta deve ser idêntica à da primeira execução
    E a resposta deve indicar que foi uma repetição
    E deve existir exatamente 1 lançamento na conta "CONTA-A"
    E a posição consolidada de "CONTA-A" deve ser "400.00"

  @RF-006 @RN-005 @critico @integracao
  Cenário: Chave de idempotência reutilizada com conteúdo diferente é rejeitada
    Dado que foi registrado um débito de "100.00" na conta "CONTA-A" com a chave de idempotência "chave-001"
    Quando for registrado um débito de "999.00" na conta "CONTA-A" com a chave de idempotência "chave-001"
    Então o lançamento deve ser rejeitado com o código "IDEMPOTENCY_KEY_CONFLICT"
    E a posição consolidada de "CONTA-A" deve ser "400.00"

  @RF-006 @RN-005 @integracao
  Cenário: Comando sem chave de idempotência é rejeitado
    Quando for registrado um débito de "100.00" na conta "CONTA-A" sem chave de idempotência
    Então o lançamento deve ser rejeitado com o código "IDEMPOTENCY_KEY_REQUIRED"

  @RF-006 @RN-005 @concorrencia @integracao
  Cenário: Envios simultâneos com a mesma chave produzem um único lançamento
    Quando forem enviados 20 débitos simultâneos de "10.00" na conta "CONTA-A" com a chave de idempotência "chave-002"
    Então deve existir exatamente 1 lançamento na conta "CONTA-A"
    E a posição consolidada de "CONTA-A" deve ser "490.00"
    E nenhuma resposta deve conter erro não tratado

  @RF-006 @RN-005 @integracao
  Cenário: Chave de idempotência é escopada por conta
    Dado que existe a conta "CONTA-B" ativa com saldo "500.00"
    E que foi registrado um débito de "100.00" na conta "CONTA-A" com a chave de idempotência "chave-003"
    Quando for registrado um débito de "100.00" na conta "CONTA-B" com a chave de idempotência "chave-003"
    Então o lançamento deve ser aceito
    E a posição consolidada de "CONTA-B" deve ser "400.00"
```

**Decisão registrada:** a chave de idempotência é obrigatória, não opcional. Em sistema financeiro, tornar opcional significa que o caminho inseguro é o caminho padrão. O custo é um requisito adicional ao integrador; o benefício é a eliminação estrutural de duplicidade. Esta decisão será formalizada em ADR.

---

### F04: Consulta de posição consolidada

```gherkin
# language: pt
@F04 @critico
Funcionalidade: Consulta da posição consolidada do cliente
  Como área de atendimento, canal digital ou sistema parceiro
  Quero consultar o saldo de uma conta em um instante determinado
  Para informar ao cliente sua posição com exatidão auditável

  Contexto:
    Dado que existe a conta "CONTA-A" ativa
    E que foram registrados os lançamentos na conta "CONTA-A":
      | data do fato         | tipo    | valor   |
      | 2026-01-10T10:00:00Z | credito | 1000.00 |
      | 2026-01-15T14:30:00Z | debito  | 250.00  |
      | 2026-02-01T09:00:00Z | credito | 500.00  |
      | 2026-02-20T16:45:00Z | debito  | 100.00  |

  @RF-003 @unidade
  Cenário: Consulta da posição atual
    Quando for consultada a posição consolidada de "CONTA-A"
    Então a posição deve ser "1150.00"
    E a resposta deve informar a sequência de referência do cálculo

  @RF-004 @RN-009 @critico @integracao
  Esquema do Cenário: Consulta da posição em instante passado
    Quando for consultada a posição consolidada de "CONTA-A" em "<instante>"
    Então a posição deve ser "<posicao>"

    Exemplos:
      | instante             | posicao |
      | 2026-01-09T23:59:59Z | 0.00    |
      | 2026-01-10T10:00:00Z | 1000.00 |
      | 2026-01-20T00:00:00Z | 750.00  |
      | 2026-02-01T09:00:00Z | 1250.00 |
      | 2026-02-28T00:00:00Z | 1150.00 |

  @RF-004 @unidade
  Cenário: Consulta em instante futuro é rejeitada
    Quando for consultada a posição consolidada de "CONTA-A" em um instante futuro
    Então a consulta deve ser rejeitada com o código "INVALID_POINT_IN_TIME"

  @RF-004 @RN-011 @integracao
  Cenário: Consulta no limite do instante inclui o lançamento daquele instante
    Quando for consultada a posição consolidada de "CONTA-A" em "2026-01-15T14:30:00Z"
    Então a posição deve ser "750.00"

  @RF-003 @integracao
  Cenário: Consulta de conta sem lançamentos retorna posição zero
    Dado que existe a conta "CONTA-C" ativa sem lançamentos
    Quando for consultada a posição consolidada de "CONTA-C"
    Então a posição deve ser "0.00"

  @RF-003 @integracao
  Cenário: Consulta de conta inexistente retorna não encontrado
    Quando for consultada a posição consolidada de uma conta inexistente
    Então a consulta deve ser rejeitada com o código "ACCOUNT_NOT_FOUND"

  @RF-004 @RN-012 @integracao
  Cenário: Lançamento retroativo altera a posição histórica mas não o saldo já validado
    Dado que a posição consolidada de "CONTA-A" é "1150.00"
    Quando for registrado um crédito de "300.00" com data do fato "2026-01-12T08:00:00Z"
    Então a posição consolidada de "CONTA-A" em "2026-01-20T00:00:00Z" deve ser "1050.00"
    E a posição consolidada atual de "CONTA-A" deve ser "1450.00"
```

**Nota de modelagem:** o último cenário expõe a natureza bitemporal do ledger. A consulta por instante usa a **data do fato**; a validação de saldo em débito usa a **posição corrente**. Separar os dois eixos evita que um lançamento retroativo torne negativo um saldo que já foi validado e comunicado ao cliente. Trade-off detalhado na EF §5, RN-012.

---

### F05: Consulta de extrato

```gherkin
# language: pt
@F05
Funcionalidade: Consulta do extrato de movimentações
  Como cliente ou operador de atendimento
  Quero listar as movimentações de uma conta em um período
  Para conferir a origem de cada alteração na posição

  Contexto:
    Dado que existe a conta "CONTA-A" ativa com 150 lançamentos registrados

  @RF-005 @integracao
  Cenário: Extrato é retornado em ordem cronológica estável
    Quando for consultado o extrato de "CONTA-A"
    Então os lançamentos devem vir ordenados por sequência crescente

  @RF-005 @RNF-002 @integracao
  Cenário: Extrato é paginado por cursor
    Quando for consultado o extrato de "CONTA-A" com limite de 50
    Então devem ser retornados 50 lançamentos
    E a resposta deve conter um cursor para a próxima página
    Quando for consultada a próxima página com o cursor recebido
    Então devem ser retornados os 50 lançamentos seguintes sem repetição

  @RF-005 @integracao
  Cenário: Extrato filtrado por período
    Quando for consultado o extrato de "CONTA-A" entre "2026-01-01T00:00:00Z" e "2026-01-31T23:59:59Z"
    Então todos os lançamentos retornados devem ter data do fato dentro do período

  @RF-005 @RNF-012 @integracao
  Cenário: Limite de página excessivo é recusado
    Quando for consultado o extrato de "CONTA-A" com limite de 10000
    Então a consulta deve ser rejeitada com o código "PAGE_SIZE_EXCEEDED"
```

---

### F06: Estorno de lançamento

```gherkin
# language: pt
@F06
Funcionalidade: Estorno de lançamento por compensação
  Como área de operações do banco
  Quero reverter o efeito de um lançamento equivocado
  Para corrigir a posição do cliente sem destruir a trilha de auditoria

  Contexto:
    Dado que existe a conta "CONTA-A" ativa com saldo "0.00"
    E que foi registrado um crédito de "500.00" na conta "CONTA-A" identificado como "LCTO-1"

  @RF-007 @RN-004 @integracao
  Cenário: Estorno gera lançamento compensatório e preserva o original
    Quando for solicitado o estorno de "LCTO-1"
    Então deve ser criado um novo lançamento de débito de "500.00"
    E o lançamento "LCTO-1" deve permanecer inalterado no ledger
    E o novo lançamento deve referenciar "LCTO-1" como origem
    E a posição consolidada de "CONTA-A" deve ser "0.00"
    E o extrato de "CONTA-A" deve conter 2 lançamentos

  @RF-007 @RN-004 @integracao
  Cenário: Estorno em duplicidade é rejeitado
    Dado que "LCTO-1" já foi estornado
    Quando for solicitado o estorno de "LCTO-1"
    Então a operação deve ser rejeitada com o código "ENTRY_ALREADY_REVERSED"

  @RF-007 @RN-004 @integracao
  Cenário: Estorno de estorno é rejeitado
    Dado que "LCTO-1" foi estornado gerando "LCTO-2"
    Quando for solicitado o estorno de "LCTO-2"
    Então a operação deve ser rejeitada com o código "CANNOT_REVERSE_REVERSAL"

  @RF-007 @RN-001 @integracao
  Cenário: Estorno de crédito que tornaria a posição negativa é rejeitado
    Dado que foi registrado um débito de "400.00" na conta "CONTA-A"
    Quando for solicitado o estorno de "LCTO-1"
    Então a operação deve ser rejeitada com o código "INSUFFICIENT_FUNDS"
```

**Questão aberta para o negócio:** o último cenário assume que o estorno respeita a invariante de saldo não negativo. Em operação bancária real, estorno de crédito indevido frequentemente **deve** poder gerar saldo negativo, tratado como pendência a cobrar. A decisão entre as duas políticas é do negócio, não da engenharia. Registrado na EF §10 como questão aberta QA-003.

---

### F07: Integridade sob concorrência

```gherkin
# language: pt
@F07 @critico @concorrencia
Funcionalidade: Integridade da posição sob acesso concorrente
  Como banco responsável por dados financeiros
  Quero que movimentações simultâneas na mesma conta nunca corrompam a posição
  Para que nenhuma inconsistência chegue ao cliente

  @RF-002 @RN-001 @critico @concorrencia @integracao
  Cenário: Débitos simultâneos jamais tornam a posição negativa
    Dado que existe a conta "CONTA-A" ativa com saldo "100.00"
    Quando forem disparados 50 débitos simultâneos de "10.00" na conta "CONTA-A"
    Então exatamente 10 débitos devem ser aceitos
    E exatamente 40 débitos devem ser rejeitados com o código "INSUFFICIENT_FUNDS"
    E a posição consolidada de "CONTA-A" deve ser "0.00"
    E em nenhum momento a posição deve ter sido negativa

  @RF-001 @RN-006 @critico @concorrencia @integracao
  Cenário: Créditos simultâneos não perdem lançamento nem duplicam sequência
    Dado que existe a conta "CONTA-A" ativa com saldo "0.00"
    Quando forem disparados 200 créditos simultâneos de "1.00" na conta "CONTA-A"
    Então devem existir exatamente 200 lançamentos na conta "CONTA-A"
    E as sequências devem formar a série contínua de 1 a 200
    E a posição consolidada de "CONTA-A" deve ser "200.00"

  @RN-006 @concorrencia @integracao
  Cenário: Operações concorrentes em contas distintas não se bloqueiam
    Dado que existem 100 contas ativas com saldo "1000.00"
    Quando for disparado 1 débito de "10.00" simultâneo em cada uma das 100 contas
    Então todos os 100 débitos devem ser aceitos
    E nenhuma operação deve ter aguardado bloqueio de outra conta

  @RF-002 @concorrencia @integracao
  Cenário: Conflito de concorrência é resolvido por nova tentativa, não por erro ao chamador
    Dado que existe a conta "CONTA-A" ativa com saldo "10000.00"
    Quando forem disparados 100 créditos simultâneos de "1.00" na conta "CONTA-A"
    Então nenhuma resposta deve conter o código "CONCURRENCY_CONFLICT"
    E a posição consolidada de "CONTA-A" deve ser "10100.00"
```

**Nota de execução:** estes cenários são inúteis se executados sequencialmente. O passo "disparados simultâneos" deve usar paralelismo real com barreira de sincronização, contra banco de dados real via Testcontainers. Um teste de concorrência que não falha na implementação ingênua (ler saldo, validar, gravar) não está testando concorrência.

---

### F08: Degradação graciosa e falha parcial

```gherkin
# language: pt
@F08
Funcionalidade: Comportamento sob falha parcial de componentes
  Como banco que opera em ambiente real
  Quero que a indisponibilidade de um componente acessório não derrube a operação
  Para preservar a capacidade de movimentar e consultar recursos

  @RF-008 @RNF-010 @integracao
  Cenário: Indisponibilidade do cache não impede a consulta de posição
    Dado que existe a conta "CONTA-A" ativa com saldo "750.00"
    E que o cache de posição está indisponível
    Quando for consultada a posição consolidada de "CONTA-A"
    Então a posição deve ser "750.00"
    E o tempo de resposta pode ser superior ao alvo
    E o evento de degradação deve ser registrado na telemetria

  @RF-008 @RF-010 @RNF-010 @integracao
  Cenário: Atraso na projeção de snapshot não produz posição incorreta
    Dado que existe a conta "CONTA-A" ativa com saldo "750.00"
    E que o processo de geração de snapshot está parado
    Quando forem registrados 500 créditos de "1.00" na conta "CONTA-A"
    E for consultada a posição consolidada de "CONTA-A"
    Então a posição deve ser "1250.00"

  @RF-008 @RNF-011 @integracao
  Cenário: Falha na publicação de evento de integração não desfaz o lançamento
    Dado que existe a conta "CONTA-A" ativa com saldo "100.00"
    E que o barramento de mensageria está indisponível
    Quando for registrado um crédito de "50.00" na conta "CONTA-A"
    Então o lançamento deve ser aceito
    E a posição consolidada de "CONTA-A" deve ser "150.00"
    E o evento deve permanecer pendente de publicação
    Quando o barramento de mensageria voltar a operar
    Então o evento deve ser publicado exatamente uma vez do ponto de vista do consumidor

  @RF-008 @RNF-011 @critico @integracao
  Cenário: Indisponibilidade do armazenamento rejeita a escrita sem registro parcial
    Dado que existe a conta "CONTA-A" ativa com saldo "100.00"
    E que o armazenamento primário está indisponível
    Quando for registrado um crédito de "50.00" na conta "CONTA-A"
    Então a operação deve ser rejeitada com o código "SERVICE_UNAVAILABLE"
    E a resposta deve indicar que a nova tentativa é segura
    Quando o armazenamento voltar a operar
    E o mesmo comando for reenviado com a mesma chave de idempotência
    Então deve existir exatamente 1 lançamento na conta "CONTA-A"

  @RF-008 @RNF-010 @integracao
  Cenário: Falhas sucessivas abrem o circuito e a recuperação é automática
    Dado que o armazenamento primário apresenta falhas sucessivas
    Quando o limiar de falhas do disjuntor for atingido
    Então as requisições seguintes devem falhar imediatamente sem aguardar tempo limite
    E a verificação de prontidão do serviço deve reportar estado degradado
    Quando o armazenamento voltar a operar
    Então o disjuntor deve fechar automaticamente sem intervenção manual
```

---

### F09: Controle de acesso e proteção de dados

```gherkin
# language: pt
@F09 @critico
Funcionalidade: Controle de acesso e proteção de dados sensíveis
  Como encarregado de proteção de dados e área de segurança
  Quero que dados financeiros sejam acessíveis apenas a quem tem direito
  Para atender à LGPD e à regulação bancária

  @RF-009 @RNF-020 @integracao
  Cenário: Requisição sem autenticação é recusada
    Quando for consultada a posição consolidada de "CONTA-A" sem credencial
    Então a requisição deve ser recusada com o código "UNAUTHENTICATED"
    E nenhum dado da conta deve constar na resposta

  @RF-009 @RNF-020 @critico @integracao
  Cenário: Cliente não acessa conta de terceiro
    Dado que o cliente "CLIENTE-1" é titular da conta "CONTA-A"
    E que o cliente "CLIENTE-2" é titular da conta "CONTA-B"
    Quando o cliente "CLIENTE-2" consultar a posição consolidada de "CONTA-A"
    Então a requisição deve ser recusada com o código "ACCOUNT_NOT_FOUND"
    E a resposta deve ser idêntica à de uma conta inexistente

  @RF-009 @RNF-021 @critico @integracao
  Cenário: Dados sensíveis não aparecem em log
    Quando for registrado um crédito de "100.00" na conta "CONTA-A" do titular de CPF "12345678909"
    Então nenhuma entrada de log deve conter o CPF em texto claro
    E nenhuma entrada de log deve conter o número completo da conta
    E as entradas de log devem conter o identificador de correlação da requisição

  @RF-009 @RNF-022 @integracao
  Cenário: Toda operação de escrita é auditável
    Quando for registrado um débito de "100.00" na conta "CONTA-A"
    Então deve ser possível identificar o autor, o instante e a origem da requisição
    E o registro de auditoria não deve ser alterável

  @RF-009 @RNF-012 @integracao
  Cenário: Excesso de requisições é limitado
    Quando forem enviadas requisições acima do limite configurado para o mesmo chamador
    Então as requisições excedentes devem ser recusadas com o código "RATE_LIMIT_EXCEEDED"
    E a resposta deve informar o tempo de espera recomendado
```

---

### F10: Consistência do snapshot

```gherkin
# language: pt
@F10
Funcionalidade: Consistência da posição pré-calculada
  Como engenheiro responsável pela operação
  Quero que o snapshot seja sempre uma otimização e nunca uma fonte de erro
  Para que a posição informada ao cliente seja sempre reconstruível a partir do ledger

  @RF-010 @RN-010 @critico @integracao
  Cenário: Posição calculada com snapshot é idêntica à calculada pelo ledger completo
    Dado que existe a conta "CONTA-A" com 5000 lançamentos e snapshot gerado na sequência 4000
    Quando a posição for calculada usando o snapshot
    E a posição for recalculada percorrendo todo o ledger
    Então os dois valores devem ser idênticos

  @RF-010 @RN-010 @integracao
  Cenário: Snapshot corrompido ou ausente não impede a consulta
    Dado que existe a conta "CONTA-A" com 5000 lançamentos
    E que todos os snapshots da conta foram removidos
    Quando for consultada a posição consolidada de "CONTA-A"
    Então a posição deve ser calculada corretamente a partir do ledger
    E a consulta deve ser concluída com sucesso

  @RF-010 @integracao
  Cenário: Snapshot é reconstruível a qualquer momento
    Dado que existe a conta "CONTA-A" com 5000 lançamentos
    Quando o processo de reconstrução de snapshot for executado
    Então o snapshot reconstruído deve ser idêntico ao anterior
```

**Princípio arquitetural que estes cenários protegem:** o ledger é a única fonte da verdade. Snapshot, cache e projeção são derivados descartáveis. Se apagar qualquer um deles tornar o sistema incorreto, o desenho está errado. Este é o teste decisivo da solução.

---

## 6. Matriz de rastreabilidade

| Requisito | Origem | Funcionalidades BDD | Status |
|---|---|---|---|
| RF-001 Registrar crédito | Enunciado | F01, F07 | Especificado |
| RF-002 Registrar débito | Enunciado | F02, F07 | Especificado |
| RF-003 Consultar posição atual | Enunciado | F04, F08 | Especificado |
| RF-004 Consultar posição em instante | Enunciado | F04 | Especificado |
| RF-005 Consultar extrato | Inferido | F05 | Especificado |
| RF-006 Idempotência | Inferido (criticidade financeira) | F03 | Especificado |
| RF-007 Estorno | Inferido (imutabilidade) | F06 | Especificado, com questão aberta |
| RF-008 Degradação graciosa | Enunciado ("falhas parciais") | F08 | Especificado |
| RF-009 Controle de acesso | Enunciado ("dados sensíveis") | F09 | Especificado |
| RF-010 Snapshot consistente | Inferido (desempenho) | F10 | Especificado |

| Regra | Funcionalidades que a validam |
|---|---|
| RN-001 Posição não negativa | F02, F06, F07 |
| RN-002 Valor positivo com duas casas | F01 |
| RN-003 Lançamento imutável | F01, F06 |
| RN-004 Estorno por compensação | F06 |
| RN-005 Idempotência obrigatória | F03, F08 |
| RN-006 Sequência monotônica sem lacunas | F01, F02, F07 |
| RN-007 Moeda única por conta | F01 |
| RN-008 Conta ativa | F01 |
| RN-009 Posição em instante | F04 |
| RN-010 Snapshot é derivado | F10 |
| RN-011 Inclusão no limite do instante | F04 |
| RN-012 Separação fato e registro | F04 |

**Requisitos não funcionais com validação comportamental:** RNF-002, RNF-010, RNF-011, RNF-012, RNF-020, RNF-021, RNF-022. Os demais RNF são verificados por teste de carga, análise estática ou inspeção, conforme a [ENF](./ENF-especificacao-nao-funcional.md) §8.

---

## 7. Estratégia de automação

| Camada | Escopo | Ferramenta | Tags |
|---|---|---|---|
| Domínio | Invariantes puras, sem I/O | xUnit | `@unidade` |
| Integração | Banco real, transação real | Reqnroll + Testcontainers (PostgreSQL) | `@integracao` |
| Concorrência | Paralelismo real com barreira | xUnit + Testcontainers | `@concorrencia` |
| Arquitetura | Direção de dependência entre módulos | NetArchTest | n/a |
| Contrato | Conformidade do payload com OpenAPI | Verify + snapshot | n/a |

**Critério de bloqueio de release (modelo proposto, não política vigente):** 100% dos cenários marcados `@critico` devem passar. Falha em qualquer cenário `@critico` impede publicação, sem exceção por prazo.

**Antipadrão a evitar:** substituir o banco real por repositório em memória nos cenários `@integracao` e `@concorrencia`. Repositório em memória não reproduz isolamento transacional, bloqueio nem violação de constraint. Um teste de concorrência contra memória passa na implementação errada, o que é pior do que não existir.

---

## 8. Questões abertas

| ID | Questão | Impacto se não respondida | Conduta provisória |
|---|---|---|---|
| QA-001 | Existe limite ou cheque especial por conta? | Altera RN-001 | Assumir saldo não negativo; validação num único ponto do agregado, sem política substituível (EF §10) |
| QA-002 | Lançamento retroativo é permitido pelo negócio? | Altera RN-012 e F04 | Permitir, validando saldo contra posição corrente |
| QA-003 | Estorno pode gerar posição negativa? | Altera F06 | **Decidida:** rejeitar (ESTADO §3). Sem política configurável |
| QA-004 | Qual a política de retenção do ledger? | Altera dimensionamento e LGPD | Assumir retenção integral no escopo do desafio |
| QA-005 | Há exigência de operação multimoeda? | Altera RN-007 e modelo de `Money` | Assumir BRL, com `Money` preparado para moeda |

Nenhuma destas lacunas foi preenchida com número ou política inventada. Todas estão registradas na [EF](./EF-especificacao-funcional.md) §10 com a conduta provisória adotada e o critério para revisão.

---

## 9. Histórico

| Versão | Data | Autor | Alteração |
|---|---|---|---|
| 1.0 | 2026-10-02 | Eduardo J. G. do Carmo | Versão inicial inferida a partir do enunciado do desafio |
| 1.1 | 2026-10-02 | Eduardo J. G. do Carmo | F09: conta de terceiro responde `ACCOUNT_NOT_FOUND`, e não `FORBIDDEN`, para não revelar existência ([ADR-0009](../adr/ADR-0009-seguranca-e-privilegio-minimo.md) §3; lacuna L-05) |
| 1.2 | 2026-10-04 | Eduardo J. G. do Carmo | §8: QA-001 e QA-003 corrigidas contra o código; QA-003 decidida (lacuna L-14, card 29) |
