# Especificação Funcional

**Projeto:** Sistema de Movimentações Financeiras e Posição Consolidada
**Documento:** 2 de 3 do pacote de especificação
**Versão:** 1.0
**Data:** 2026-10-02
**Status:** Proposto

**Documentos relacionados:**

- [BDD: Especificação por Comportamento](./BDD-comportamento.md): critérios de aceite executáveis de cada `RF` e `RN` aqui definidos
- [Especificação Não Funcional](./ENF-especificacao-nao-funcional.md): `RNF-xxx`, SLOs e atributos de qualidade
- `docs/adr/` (a produzir): decisões arquiteturais que realizam esta especificação

---

## 1. Propósito e precedência

Este documento define **o que o sistema faz**: domínio, regras, requisitos funcionais e contratos. Não define **como** é construído, o que cabe aos ADRs, nem **quão bem** opera, o que cabe à ENF.

**Regra de precedência do pacote:**

1. Para *definição* de termo, regra ou contrato: prevalece este documento.
2. Para *critério de aceite* de um requisito: prevalece o [BDD](./BDD-comportamento.md).
3. Para *meta de qualidade operacional*: prevalece a [ENF](./ENF-especificacao-nao-funcional.md).
4. Para *escolha técnica*: prevalece o ADR correspondente.

Divergência entre documentos é defeito de especificação e deve ser corrigida no documento de origem, nunca contornada no código.

---

## 2. Origem dos requisitos e disciplina de inferência

O enunciado do desafio fornece três requisitos explícitos:

> Registre as movimentações financeiras dos clientes (créditos e débitos) em suas contas;
> Permita consultar a posição consolidada (saldo) de um cliente em um determinado momento;
> Continue operando de forma confiável mesmo em cenários de instabilidade, alta demanda ou falhas parciais.

Todo o restante deste documento é **inferência deliberada**. Cada item inferido está marcado `[INFERIDO]` com a justificativa da inferência. Nenhum volume, SLA, política de produto ou número de negócio foi tratado como fato: o que falta está em §10 como questão aberta, com conduta provisória declarada.

Esta disciplina é intencional e é parte da entrega. Em contrato de consultoria real, o consultor que preenche lacuna de negócio com suposição silenciosa transfere risco ao cliente sem que o cliente saiba.

---

## 3. Escopo

### 3.1 Dentro do escopo

- Registro de créditos e débitos em contas de clientes
- Cálculo da posição consolidada atual e em instante passado
- Consulta de extrato de movimentações
- Estorno por lançamento compensatório
- Garantia de idempotência de comandos
- Controle de acesso por titularidade da conta
- Publicação de eventos de integração para sistemas consumidores
- Trilha de auditoria imutável

### 3.2 Fora do escopo

| Item | Justificativa |
|---|---|
| Cadastro e ciclo de vida de cliente | Pertence a contexto delimitado distinto (Cadastro) |
| Abertura e encerramento de conta | Pertence ao contexto Contas; aqui a conta é lida como dado de referência |
| Emissão de credenciais e gestão de identidade | Pertence ao contexto de IAM; o sistema valida token, não o emite |
| Liquidação interbancária, Pix, TED, boleto | São originadores que invocam este sistema, não parte dele |
| Cálculo de juros, tarifas e tributos | Produzem lançamentos através da mesma API; a regra de cálculo é externa |
| Interface gráfica | O sistema expõe API; canais são consumidores |
| Antifraude e análise de risco transacional | Decide antes da chamada; este sistema registra o que foi autorizado |

**Decisão de fronteira:** este sistema é um **livro-razão de contas correntes de clientes**, não um motor de produtos bancários. Tudo que calcula *quanto* deve ser lançado fica fora; o que registra *que foi lançado* e responde *qual a posição* fica dentro. Manter essa fronteira é o que impede o sistema de voltar a ser o legado descrito no enunciado, que "foi crescendo sem muito planejamento".

---

## 4. Modelo de domínio

### 4.1 Visão geral

```
Cliente (referência externa)
   │ 1
   │
   │ N
Conta ──────────────┬──────────────────────┐
   │ 1              │ 1                    │ 1
   │                │                      │
   │ N              │ N                    │ N
Lançamento     Snapshot de Posição    Chave de Idempotência
   │ 0..1
   │ (origem do estorno)
   └──> Lançamento
```

### 4.2 Conta (*Account*)

Agregado raiz. Unidade de consistência transacional e **unidade de serialização de concorrência** do sistema inteiro.

| Atributo | Tipo | Regra |
|---|---|---|
| `AccountId` | Identificador | Imutável, gerado na criação |
| `CustomerId` | Identificador | Titular; base do controle de acesso (RF-009) |
| `Currency` | ISO 4217 | Imutável após criação (RN-007) |
| `Status` | `Active` \| `Blocked` \| `Closed` | Determina aceitação de lançamentos (RN-008) |
| `LastSequence` | Inteiro ≥ 0 | Última sequência atribuída; controla concorrência (RN-006) |

**Decisão estrutural:** a Conta é o agregado porque a invariante "posição nunca negativa" só pode ser garantida se todas as movimentações daquela conta forem serializadas entre si. Contas distintas são independentes, e é daí que vem a escalabilidade horizontal do sistema (ver ENF, RNF-005).

### 4.3 Lançamento (*LedgerEntry*)

Entidade imutável. Fato consumado.

| Atributo | Tipo | Regra |
|---|---|---|
| `EntryId` | Identificador | Imutável |
| `AccountId` | Identificador | Conta a que pertence |
| `Sequence` | Inteiro ≥ 1 | Único por conta, monotônico, sem lacunas (RN-006) |
| `Direction` | `Credit` \| `Debit` | Sentido do efeito na posição |
| `Amount` | Decimal positivo | Sempre positivo; o sinal vem de `Direction` (RN-002) |
| `Currency` | ISO 4217 | Igual à da conta (RN-007) |
| `OccurredAt` | Instante UTC | Data do fato financeiro; informada pelo chamador ou padrão `RecordedAt` |
| `RecordedAt` | Instante UTC | Data da gravação; atribuída pelo sistema, nunca pelo chamador |
| `IdempotencyKey` | Texto | Fornecida pelo chamador; única por conta (RN-005) |
| `CorrelationId` | Identificador | Rastreabilidade ponta a ponta |
| `ReversalOf` | Identificador? | Preenchido apenas em estorno (RN-004) |
| `Metadata` | Chave-valor | Dados do originador; não participa de nenhuma regra |

**Nota de modelagem: por que `Amount` é sempre positivo.** Permitir valor negativo cria duas representações para o mesmo fato (débito de 100 ou crédito de -100) e abre espaço para erro de sinal em agregações. Separar magnitude de sentido torna impossível essa classe de defeito.

### 4.4 Snapshot de Posição (*BalanceSnapshot*)

Entidade derivada. **Descartável e reconstruível por definição** (RN-010).

| Atributo | Tipo | Regra |
|---|---|---|
| `AccountId` | Identificador | Conta |
| `UpToSequence` | Inteiro | Posição acumulada até esta sequência, inclusive |
| `Balance` | Decimal | Resultado acumulado |
| `AsOf` | Instante UTC | `OccurredAt` do lançamento na sequência de corte |

**Invariante arquitetural:** apagar todos os snapshots não pode alterar nenhuma resposta do sistema, apenas o tempo de resposta. Validado em [BDD](./BDD-comportamento.md) F10.

### 4.5 Chave de Idempotência (*IdempotencyRecord*)

| Atributo | Tipo | Regra |
|---|---|---|
| `AccountId` + `IdempotencyKey` | Chave composta | Unicidade garantida por constraint no banco |
| `RequestFingerprint` | Hash | Detecta reuso de chave com conteúdo diferente |
| `ResponseSnapshot` | Documento | Resposta original, devolvida na repetição |
| `CreatedAt` | Instante UTC | Base da política de retenção |

**Decisão:** a unicidade é garantida por constraint do banco, não por verificação prévia em código. Verificar antes de inserir é vulnerável a corrida entre a verificação e a inserção. A constraint é a única garantia real.

### 4.6 Posição Consolidada (*Balance*)

Não é entidade. É resultado de consulta:

```
Posição(conta, T) = Snapshot(conta, mais recente com AsOf ≤ T).Balance
                  + Σ lançamentos da conta com AsOf < OccurredAt ≤ T
                    aplicando +Amount para Credit e -Amount para Debit
```

---

## 5. Regras de negócio

| ID | Regra | Origem | Validada em |
|---|---|---|---|
| **RN-001** | A posição consolidada de uma conta nunca pode ser negativa. Débito que a tornaria negativa é rejeitado integralmente, sem gravação parcial. | `[INFERIDO]` do enunciado: "qualquer inconsistência gera impacto direto ao cliente". Sujeito a QA-001. | F02, F06, F07 |
| **RN-002** | `Amount` é estritamente positivo, com no máximo 2 casas decimais para BRL. Zero e negativo são inválidos. | `[INFERIDO]` prática contábil | F01 |
| **RN-003** | Lançamento gravado é imutável. Não existe operação de alteração ou exclusão, nem administrativa. | `[INFERIDO]` do enunciado: "dados financeiros sensíveis"; exigência de auditabilidade | F01, F06 |
| **RN-004** | Correção se dá exclusivamente por lançamento compensatório que referencia o original. Estorno de estorno é proibido. | Decorre de RN-003 | F06 |
| **RN-005** | Todo comando de escrita exige chave de idempotência. Repetição com mesmo conteúdo devolve o resultado original; mesma chave com conteúdo diferente é rejeitada. | `[INFERIDO]` do enunciado: "falhas parciais" implicam retry | F03, F08 |
| **RN-006** | A sequência por conta é monotônica, inicia em 1 e não admite lacunas. Comando rejeitado não consome sequência. | `[INFERIDO]` requisito de auditoria: lacuna impede detectar perda de registro | F01, F02, F07 |
| **RN-007** | Uma conta opera em moeda única, definida na criação e imutável. Lançamento em moeda divergente é rejeitado. | `[INFERIDO]`, sujeito a QA-005 | F01 |
| **RN-008** | Apenas conta com status `Active` aceita lançamentos. Conta `Blocked` ou `Closed` aceita apenas consulta. | `[INFERIDO]` prática bancária | F01 |
| **RN-009** | A posição em instante T considera lançamentos com `OccurredAt` ≤ T, independentemente de `RecordedAt`. | Enunciado: "posição em um determinado momento" | F04 |
| **RN-010** | Snapshot, cache e projeção são derivados. O ledger é a única fonte da verdade. Perda de qualquer derivado não altera nenhuma resposta. | `[INFERIDO]` princípio arquitetural | F10 |
| **RN-011** | O limite temporal da consulta é inclusivo: lançamento com `OccurredAt` exatamente igual a T entra no cálculo. | `[INFERIDO]` desambiguação necessária | F04 |
| **RN-012** | A validação de saldo em débito usa a **posição corrente**, não a posição retroativa. Lançamento retroativo altera o histórico, nunca invalida débito já aceito. | `[INFERIDO]`, sujeito a QA-002 | F04 |

### 5.1 Trade-off explícito de RN-012

**O problema:** se lançamentos retroativos são permitidos e a validação de saldo fosse retroativa, um débito aceito e já informado ao cliente poderia se tornar inválido depois, por causa de um lançamento inserido com data anterior. O cliente veria a posição mudar sem ter feito nada.

**A escolha:** separar os dois eixos temporais. `OccurredAt` governa a visão histórica; a posição corrente governa a autorização. O sistema aceita que a posição histórica seja revisável e que a posição corrente seja definitiva.

**O custo:** a soma dos lançamentos até hoje pode divergir momentaneamente do saldo que autorizou um débito passado. Esta divergência é explicável e auditável, e é preferível a tornar um débito já liquidado retroativamente inválido.

**Alternativa rejeitada:** proibir lançamento retroativo. Simplifica o modelo, mas é irreal em ambiente bancário, onde conciliação e estorno de parceiro chegam com atraso.

---

## 6. Requisitos funcionais

Notação: **Deve** = obrigatório; **Deveria** = recomendado, negociável por prazo; **Pode** = opcional.

### RF-001: Registrar crédito `[Origem: enunciado]`

O sistema **deve** registrar entrada de valor em conta de cliente, atribuindo sequência, data de registro e chave de idempotência, tornando o lançamento imediatamente visível na posição consolidada.

- Pré-condições: conta existe, está `Active`, moeda compatível, chave de idempotência presente
- Pós-condições: lançamento imutável gravado, posição atualizada, evento de integração enfileirado na mesma transação
- Regras: RN-002, RN-005, RN-006, RN-007, RN-008
- Aceite: [BDD](./BDD-comportamento.md) F01, F07

### RF-002: Registrar débito `[Origem: enunciado]`

O sistema **deve** registrar saída de valor em conta de cliente, **rejeitando integralmente** o comando quando a posição resultante seria negativa.

- Pré-condições: as de RF-001, mais posição corrente ≥ valor do débito
- Pós-condições em sucesso: idênticas a RF-001
- Pós-condições em rejeição: nenhum lançamento gravado, sequência não consumida, posição inalterada
- Regras: RN-001, RN-002, RN-005, RN-006, RN-007, RN-008
- Aceite: [BDD](./BDD-comportamento.md) F02, F07

### RF-003: Consultar posição consolidada atual `[Origem: enunciado]`

O sistema **deve** retornar a posição atual da conta, informando a sequência de referência usada no cálculo para permitir ao consumidor detectar leitura desatualizada.

- Regras: RN-009, RN-010
- Aceite: [BDD](./BDD-comportamento.md) F04, F08

### RF-004: Consultar posição consolidada em instante `[Origem: enunciado]`

O sistema **deve** retornar a posição da conta em qualquer instante passado, com limite inclusivo. Instante futuro é rejeitado.

- Regras: RN-009, RN-011, RN-012
- Aceite: [BDD](./BDD-comportamento.md) F04

**Este é o requisito que define a arquitetura.** Atendê-lo com saldo materializado mutável é impossível sem histórico de alterações, o que equivale a reinventar o ledger de forma pior. É por causa deste requisito que o modelo é append-only.

### RF-005: Consultar extrato `[INFERIDO]`

O sistema **deve** listar os lançamentos de uma conta, com filtro por período, ordenação estável por sequência e paginação por cursor.

- Justificativa da inferência: posição sem extrato é inauditável; o cliente precisa saber a origem de cada alteração
- Aceite: [BDD](./BDD-comportamento.md) F05

### RF-006: Garantir idempotência `[INFERIDO]`

O sistema **deve** exigir chave de idempotência em todo comando de escrita, devolver a resposta original em repetição idêntica e rejeitar reuso de chave com conteúdo diferente. A garantia **deve** valer também sob envio simultâneo.

- Justificativa da inferência: "falhas parciais" no enunciado implicam retry; sem idempotência, retry gera duplicidade financeira
- Aceite: [BDD](./BDD-comportamento.md) F03

### RF-007: Estornar lançamento `[INFERIDO]`

O sistema **deve** permitir reverter o efeito de um lançamento por meio de lançamento compensatório que referencia o original. **Não deve** permitir estorno em duplicidade nem estorno de estorno.

- Justificativa da inferência: decorre logicamente de RN-003; sem estorno, erro operacional seria incorrigível
- Questão aberta: QA-003
- Aceite: [BDD](./BDD-comportamento.md) F06

### RF-008: Operar sob falha parcial `[Origem: enunciado]`

O sistema **deve** continuar atendendo consultas e escritas quando componentes acessórios estiverem indisponíveis, degradando desempenho em vez de correção. Indisponibilidade do armazenamento primário **deve** produzir rejeição explícita e seguramente repetível, nunca gravação parcial.

- Regras: RN-010
- Aceite: [BDD](./BDD-comportamento.md) F08; metas em [ENF](./ENF-especificacao-nao-funcional.md) RNF-010, RNF-011

### RF-009: Controlar acesso e proteger dados `[Origem: enunciado]`

O sistema **deve** autenticar todo chamador, autorizar por titularidade da conta, mascarar dados pessoais em log e manter trilha de auditoria imutável de toda escrita. Resposta de acesso negado **não deve** revelar a existência da conta.

- Aceite: [BDD](./BDD-comportamento.md) F09; controles em [ENF](./ENF-especificacao-nao-funcional.md) §5

### RF-010: Manter snapshot consistente `[INFERIDO]`

O sistema **deve** manter posições pré-calculadas para limitar o custo de leitura, garantindo que o resultado com snapshot seja idêntico ao cálculo integral pelo ledger e que a ausência de snapshot não produza erro.

- Justificativa da inferência: sem snapshot, a leitura degrada linearmente com o histórico, reproduzindo a lentidão do legado
- Regras: RN-010
- Aceite: [BDD](./BDD-comportamento.md) F10

### RF-011: Publicar eventos de integração `[INFERIDO]`

O sistema **deveria** publicar evento por lançamento efetivado, com entrega ao menos uma vez e identificador que permita consumo idempotente. A publicação **não deve** ser condição para o sucesso do lançamento.

- Justificativa da inferência: em banco digital, extrato, notificação e antifraude consomem movimentação; sem evento, esses sistemas fariam polling, reproduzindo a contenção do legado
- Aceite: [BDD](./BDD-comportamento.md) F08

---

## 7. Casos de uso

### CU-01: Registrar movimentação

**Ator primário:** sistema originador (Pix, cartão, tarifa, conciliação)
**Pré-condição:** chamador autenticado e autorizado na conta

**Fluxo principal:**

1. Originador envia comando com conta, sentido, valor, moeda, data do fato e chave de idempotência
2. Sistema valida formato e autorização
3. Sistema verifica chave de idempotência; se já existente com mesmo conteúdo, devolve a resposta original e encerra
4. Sistema serializa o acesso à conta
5. Sistema carrega a posição corrente
6. Se débito, valida RN-001; se insuficiente, rejeita e encerra
7. Sistema atribui a próxima sequência
8. Em transação única: grava o lançamento, atualiza o controle da conta, registra a chave de idempotência e enfileira o evento de integração
9. Sistema confirma a transação e responde com o lançamento criado

**Fluxos alternativos:**

| Condição | Comportamento |
|---|---|
| Conta inexistente | Rejeita `ACCOUNT_NOT_FOUND` |
| Conta não ativa | Rejeita `ACCOUNT_INACTIVE` |
| Moeda divergente | Rejeita `CURRENCY_MISMATCH` |
| Chave reutilizada com conteúdo diferente | Rejeita `IDEMPOTENCY_KEY_CONFLICT` |
| Conflito de concorrência | Nova tentativa automática com recuo exponencial; esgotadas as tentativas, rejeita `SERVICE_UNAVAILABLE` como repetível |
| Armazenamento indisponível | Rejeita `SERVICE_UNAVAILABLE`; nenhuma gravação parcial |

**Invariante crítica do passo 8:** lançamento, controle da conta, chave de idempotência e evento pertencem à **mesma transação local**. Dividir isso entre transações ou entre serviços introduz janela de inconsistência em dado financeiro. É a razão central para manter estes elementos no mesmo contexto delimitado.

### CU-02: Consultar posição consolidada

**Ator primário:** canal digital, atendimento ou sistema parceiro

1. Chamador solicita a posição da conta, opcionalmente com instante de referência
2. Sistema valida autorização por titularidade
3. Sistema localiza o snapshot aplicável
4. Sistema soma os lançamentos posteriores ao snapshot até o instante de referência
5. Sistema responde com posição, moeda, instante e sequência de referência

**Fluxo alternativo:** snapshot ausente ou inconsistente leva ao cálculo integral pelo ledger, com degradação de tempo e registro na telemetria.

### CU-03: Estornar lançamento

1. Operador autorizado solicita estorno informando o lançamento de origem
2. Sistema valida que o lançamento existe, não é estorno e ainda não foi estornado
3. Sistema valida RN-001 para o efeito do estorno
4. Sistema registra lançamento de sentido oposto referenciando o original, seguindo CU-01 a partir do passo 4

---

## 8. Contrato de API

### 8.1 Princípios

| Princípio | Decisão |
|---|---|
| Versionamento | Prefixo de caminho `/api/v1`, com política de compatibilidade retroativa dentro da versão |
| Formato de erro | `application/problem+json` conforme RFC 9457, com campo `code` estável para consumo programático |
| Idempotência | Cabeçalho `Idempotency-Key`, obrigatório em todo verbo de escrita |
| Rastreabilidade | Cabeçalho `X-Correlation-Id`, aceito do chamador ou gerado; sempre devolvido |
| Valores monetários | String decimal, nunca número JSON |
| Instantes | ISO 8601 em UTC com sufixo `Z` |
| Separação de verbos | Crédito e débito em recursos distintos, permitindo autorização e limites diferenciados |

### 8.2 Por que valor monetário trafega como string

JSON não define precisão numérica. Parsers em JavaScript convertem número para IEEE-754 de dupla precisão, onde `0.1 + 0.2` não é `0.3`. Em livro-razão, arredondamento silencioso em qualquer ponto da cadeia é defeito financeiro. Transportar como string elimina a classe inteira de defeito, ao custo de uma conversão explícita no cliente. A conversão explícita é exatamente o que se deseja.

### 8.3 Endpoints

| Verbo | Recurso | Requisito |
|---|---|---|
| `POST` | `/api/v1/accounts/{accountId}/credits` | RF-001 |
| `POST` | `/api/v1/accounts/{accountId}/debits` | RF-002 |
| `POST` | `/api/v1/accounts/{accountId}/entries/{entryId}/reversals` | RF-007 |
| `GET` | `/api/v1/accounts/{accountId}/balance` | RF-003, RF-004 |
| `GET` | `/api/v1/accounts/{accountId}/entries` | RF-005 |
| `GET` | `/health/live` | ENF RNF-013 |
| `GET` | `/health/ready` | ENF RNF-013 |

### 8.4 Registrar débito

**Requisição**

```http
POST /api/v1/accounts/7f3a.../debits HTTP/1.1
Authorization: Bearer <token>
Idempotency-Key: 8c1f9e2a-5b7d-4e31-9a0c-2d6f8b4e1c73
X-Correlation-Id: 3e9d1a77-...
Content-Type: application/json

{
  "amount": "250.00",
  "currency": "BRL",
  "occurredAt": "2026-10-02T14:21:00Z",
  "description": "Pagamento de boleto",
  "metadata": { "originator": "payments-service", "externalId": "PAY-99812" }
}
```

**Resposta 201**

```json
{
  "entryId": "a1b2...",
  "accountId": "7f3a...",
  "sequence": 1042,
  "direction": "Debit",
  "amount": "250.00",
  "currency": "BRL",
  "occurredAt": "2026-10-02T14:21:00Z",
  "recordedAt": "2026-10-02T14:21:00.482Z",
  "balanceAfter": "1750.00"
}
```

**Resposta 200 em repetição idempotente:** corpo idêntico ao da primeira execução, acrescido do cabeçalho `Idempotency-Replayed: true`.

**Resposta 422 por saldo insuficiente**

```json
{
  "type": "https://api.banco.example/problems/insufficient-funds",
  "title": "Saldo insuficiente",
  "status": 422,
  "code": "INSUFFICIENT_FUNDS",
  "detail": "O débito solicitado excede a posição disponível.",
  "availableBalance": "100.00",
  "requestedAmount": "250.00",
  "correlationId": "3e9d1a77-..."
}
```

### 8.5 Consultar posição consolidada

```http
GET /api/v1/accounts/7f3a.../balance?asOf=2026-01-20T00:00:00Z HTTP/1.1
```

```json
{
  "accountId": "7f3a...",
  "balance": "750.00",
  "currency": "BRL",
  "asOf": "2026-01-20T00:00:00Z",
  "computedAtSequence": 87,
  "computedFrom": "snapshot"
}
```

O campo `computedFrom` assume `snapshot` ou `ledger` e é instrumento de observabilidade: frequência elevada de `ledger` indica atraso na projeção, antes que vire problema de desempenho percebido.

### 8.6 Catálogo de códigos de erro

| Código | HTTP | Repetível | Regra |
|---|---|---|---|
| `INVALID_AMOUNT` | 400 | Não | RN-002 |
| `CURRENCY_MISMATCH` | 400 | Não | RN-007 |
| `INVALID_POINT_IN_TIME` | 400 | Não | RN-009 |
| `PAGE_SIZE_EXCEEDED` | 400 | Não | RF-005 |
| `IDEMPOTENCY_KEY_REQUIRED` | 400 | Não | RN-005 |
| `UNAUTHENTICATED` | 401 | Não | RF-009 |
| `FORBIDDEN` | 403 | Não | RF-009 |
| `ACCOUNT_NOT_FOUND` | 404 | Não | RF-003 |
| `IDEMPOTENCY_KEY_CONFLICT` | 409 | Não | RN-005 |
| `ENTRY_ALREADY_REVERSED` | 409 | Não | RN-004 |
| `CANNOT_REVERSE_REVERSAL` | 409 | Não | RN-004 |
| `ACCOUNT_INACTIVE` | 422 | Não | RN-008 |
| `INSUFFICIENT_FUNDS` | 422 | Não | RN-001 |
| `RATE_LIMIT_EXCEEDED` | 429 | Sim | ENF RNF-012 |
| `SERVICE_UNAVAILABLE` | 503 | Sim | RF-008 |

A coluna **Repetível** é contratual: define quais erros o chamador pode reenviar com a mesma chave de idempotência. Sem essa definição explícita, cada integrador adota política própria, e a política errada gera duplicidade ou perda de transação.

---

## 9. Eventos de integração

| Evento | Quando | Consumidores previstos |
|---|---|---|
| `LedgerEntryRecorded` | Lançamento efetivado | Extrato, notificação, antifraude, contabilidade |
| `EntryReversed` | Estorno efetivado | Os mesmos de `LedgerEntryRecorded` |

**Garantia:** entrega ao menos uma vez, com `eventId` estável que permite deduplicação pelo consumidor. Ordenação é garantida apenas **dentro da mesma conta**, pela sequência. Ordenação global não é oferecida porque não é necessária e custaria serialização total do sistema.

**Contrato com o consumidor:** o consumidor é responsável por idempotência. O produtor não garante entrega única, e qualquer consumidor que dependa disso será incorreto sob falha de rede.

---

## 10. Questões abertas

| ID | Questão | Por que altera a decisão | Como obter | Conduta provisória |
|---|---|---|---|---|
| QA-001 | Existe limite, cheque especial ou saldo bloqueado? | Altera RN-001 e o cálculo de disponível | Produto e risco de crédito | Posição não negativa, com a validação isolada em política substituível |
| QA-002 | Lançamento retroativo é permitido? Com que limite? | Altera RN-012, RF-004 e conciliação contábil | Operações e contabilidade | Permitido, com validação de saldo pela posição corrente |
| QA-003 | Estorno pode gerar posição negativa? | Altera RF-007 | Operações | Rejeitar, com política configurável |
| QA-004 | Qual a retenção e a política de arquivamento do ledger? | Altera dimensionamento, custo e LGPD | Compliance e jurídico | Retenção integral no escopo do desafio |
| QA-005 | Há exigência multimoeda? | Altera RN-007 e `Money` | Produto | BRL, com `Money` portando moeda desde o início |
| QA-006 | Volume esperado: lançamentos por segundo, contas, taxa de leitura? | Dimensiona toda a ENF | Dados do legado | Premissa declarada na [ENF](./ENF-especificacao-nao-funcional.md) §3, explicitamente marcada como premissa |
| QA-007 | Quem são os originadores e qual o modelo de autenticação vigente? | Altera RF-009 | Arquitetura corporativa | JWT validado contra emissor externo |

**Compromisso de método:** nenhuma destas lacunas foi preenchida com número apresentado como fato. Cada conduta provisória é reversível e está isolada em ponto de extensão, de modo que a resposta do negócio altere configuração ou uma política, não o núcleo do domínio.

---

## 11. Rastreabilidade consolidada

| RF | Regras | BDD | RNF relacionados |
|---|---|---|---|
| RF-001 | RN-002, RN-005, RN-006, RN-007, RN-008 | F01, F07 | RNF-001, RNF-004 |
| RF-002 | RN-001, RN-002, RN-005, RN-006, RN-007, RN-008 | F02, F07 | RNF-001, RNF-004 |
| RF-003 | RN-009, RN-010 | F04, F08 | RNF-002, RNF-010 |
| RF-004 | RN-009, RN-011, RN-012 | F04 | RNF-002 |
| RF-005 | RN-006 | F05 | RNF-002, RNF-012 |
| RF-006 | RN-005 | F03 | RNF-004, RNF-011 |
| RF-007 | RN-001, RN-003, RN-004 | F06 | RNF-022 |
| RF-008 | RN-010 | F08 | RNF-010, RNF-011, RNF-013 |
| RF-009 | n/a | F09 | RNF-020, RNF-021, RNF-022, RNF-012 |
| RF-010 | RN-010 | F10 | RNF-002, RNF-005 |
| RF-011 | n/a | F08 | RNF-011 |

---

## 12. Histórico

| Versão | Data | Autor | Alteração |
|---|---|---|---|
| 1.0 | 2026-10-02 | Eduardo J. G. do Carmo | Versão inicial inferida a partir do enunciado do desafio |
