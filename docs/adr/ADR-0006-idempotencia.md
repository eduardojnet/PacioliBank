# ADR-0006: Exigir chave de idempotência em toda escrita, com unicidade garantida pelo banco e resposta original persistida

- **Status:** Aceito
- **Data:** 2026-10-02
- **Decisor:** Eduardo J. G. do Carmo
- **Requisitos dirigentes:** RF-006, RN-005, RNF-011, CQ-04

## Contexto e problema

O enunciado exige que o sistema continue operando de forma confiável sob "falhas parciais". Em sistema distribuído, falha parcial significa, na prática, uma situação específica e frequente: **o servidor processou e confirmou a operação, mas a resposta não chegou ao cliente.**

Do ponto de vista do cliente, timeout de rede é indistinguível de falha de processamento. A conduta correta do cliente é tentar de novo. Sem proteção, essa nova tentativa produz um segundo débito na conta de uma pessoa real.

Nenhum mecanismo de transação resolve isso, porque não há defeito transacional: as duas requisições são, para o servidor, duas requisições válidas. A proteção precisa ser explícita.

Há uma sutileza que decide o desenho: a proteção precisa funcionar também quando as duas requisições chegam **simultaneamente**, situação comum quando um gateway tem política agressiva de nova tentativa. Qualquer solução baseada em "consultar se já existe, e se não existir, inserir" tem uma janela entre a consulta e a inserção, e é nessa janela que a duplicidade ocorre.

## Critérios de decisão

1. Elimina duplicidade também sob envio simultâneo, não apenas sequencial
2. Devolve ao cliente exatamente a resposta original, não uma resposta equivalente
3. Detecta reuso indevido de chave com conteúdo diferente
4. Não impede nova tentativa legítima após rejeição de negócio
5. Custo adicional compatível com RNF-001

## Opções consideradas

1. Chave de idempotência opcional, aplicada quando fornecida
2. Chave obrigatória com verificação prévia em aplicação
3. Chave obrigatória com unicidade garantida por constraint do banco
4. Deduplicação por janela de tempo sobre conteúdo da requisição
5. Nenhuma proteção, delegando ao cliente

## Decisão

**Chave de idempotência obrigatória**, transportada no cabeçalho `Idempotency-Key`, com unicidade garantida por constraint do banco e resposta original persistida.

### Modelo

```sql
CREATE TABLE idempotency_records (
    account_id        uuid        NOT NULL,
    idempotency_key   text        NOT NULL,
    request_hash      bytea       NOT NULL,   -- SHA-256 do payload canônico
    response_status   smallint    NOT NULL,
    response_body     jsonb       NOT NULL,
    entry_id          uuid        NOT NULL REFERENCES ledger_entries(entry_id),
    created_at        timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (account_id, idempotency_key)
);
```

O registro é gravado **na mesma transação** do lançamento ([ADR-0005](./ADR-0005-controle-de-concorrencia.md), passo 6). Não existe estado em que o lançamento exista e a chave não, ou o inverso.

### Fluxo

1. A transação executa normalmente até a inserção
2. Se a inserção em `idempotency_records` violar a chave primária, a transação é desfeita
3. Em nova conexão, o registro existente é lido
4. Se `request_hash` coincide, devolve-se `response_body` com status `200` e cabeçalho `Idempotency-Replayed: true`
5. Se `request_hash` difere, devolve-se `409 IDEMPOTENCY_KEY_CONFLICT`

**A colisão de chave primária é o mecanismo de detecção, não um caso excepcional.** Não há consulta prévia, porque consulta prévia introduz a janela de corrida que se quer eliminar. Quando duas requisições idênticas chegam ao mesmo tempo, uma vence a inserção e a outra recebe a violação, e ambas respondem corretamente.

### Impressão da requisição

`request_hash` é o SHA-256 de uma forma canônica do payload: campos ordenados, valores monetários normalizados, metadados incluídos, cabeçalhos de rastreamento excluídos. A canonicalização é necessária porque dois JSONs semanticamente idênticos podem ter serializações distintas, e uma diferença de ordenação não deve produzir conflito.

### Decisão fina: rejeição de negócio não consome a chave

Esta é a parte do desenho mais fácil de errar.

Quando um débito é rejeitado por saldo insuficiente, **nenhum registro de idempotência é gravado**. Razão: o cliente pode legitimamente reenviar o mesmo comando após um crédito ter entrado na conta, e esperar que agora seja aceito. Se a rejeição consumisse a chave, o sistema devolveria indefinidamente a rejeição original, e o cliente precisaria gerar nova chave para uma operação que, do ponto de vista dele, é a mesma.

**Somente resultados de sucesso são idempotentes.** Erros de validação e de negócio são determinísticos e podem ser reproduzidos reexecutando a operação, sem risco de duplicidade: a rejeição não altera estado.

### Retenção

Os registros expiram após **90 dias** (premissa de projeto, configurável), com expurgo por processo em lote sobre partição temporal.

**Risco registrado:** nova tentativa após a expiração da chave produz duplicidade. A mitigação é contratual, não técnica: a janela de retenção deve ser maior que a janela máxima de nova tentativa de qualquer originador, o que precisa constar do contrato de integração. [NVI] Nenhuma janela real de nova tentativa de originador foi verificada; 90 dias é premissa a validar com as equipes integradoras.

## Consequências

**Positivas**

- Duplicidade eliminada estruturalmente, inclusive sob envio simultâneo
- Resposta devolvida é a original, byte a byte, não uma reconstrução
- Reuso indevido de chave é detectado em vez de produzir efeito silencioso
- Nenhuma consulta adicional no caminho feliz: o custo é uma inserção na mesma transação
- Nova tentativa legítima após rejeição de negócio permanece possível

**Negativas**

- **Chave obrigatória impõe requisito a todo integrador**, inclusive aos que não a implementam hoje. É atrito real de adoção, e é a consequência aceita conscientemente: tornar a chave opcional faria do caminho inseguro o padrão, e em sistema financeiro o padrão precisa ser o caminho seguro
- Armazenamento adicional proporcional ao volume de escrita, com a resposta completa persistida
- Canonicalização de payload é código sutil: mudança no formato da requisição pode alterar a impressão e romper a idempotência entre versões. Mitigação: teste de regressão sobre a função de canonicalização
- A expiração cria uma janela teórica de duplicidade, mitigada apenas por contrato

**Neutras**

- A violação de constraint é caminho esperado, o que exige tratamento específico do código de erro do PostgreSQL e não pode ser confundido com falha genérica
- `ledger_entries` também carrega `UNIQUE (account_id, idempotency_key)` ([ADR-0003](./ADR-0003-ledger-append-only.md)), como segunda barreira independente

## Análise das opções rejeitadas

**Chave opcional.** Rejeitada pelo critério 1 na prática: o integrador que mais precisa da proteção é justamente o que não a implementaria. Padrão inseguro em sistema financeiro é defeito de desenho.

**Verificação prévia em aplicação.** Rejeitada pelo critério 1. Entre `SELECT` e `INSERT` existe uma janela, e sob a carga em que a proteção importa é exatamente nessa janela que as requisições chegam. Falha de forma intermitente e difícil de reproduzir, que é a pior forma de falhar.

**Deduplicação por janela de tempo sobre o conteúdo.** Rejeitada por confundir repetição com operação legítima idêntica: duas transferências de R$ 50,00 para o mesmo destinatário no mesmo minuto podem ser intencionais. Bloquear a segunda é defeito funcional.

**Delegar ao cliente.** Rejeitada: transfere a responsabilidade para a parte que não tem como garanti-la, e torna a correção do sistema dependente da qualidade de cada integrador.

## Validação

- [BDD](../specs/BDD-comportamento.md) F03, incluindo o cenário de 20 envios simultâneos com a mesma chave produzindo exatamente um lançamento
- [BDD](../specs/BDD-comportamento.md) F08, reenvio após `503` com a mesma chave produzindo exatamente um lançamento
- Teste de regressão sobre a canonicalização: payloads semanticamente idênticos com ordenação distinta produzem a mesma impressão
- Teste verificando que rejeição por saldo insuficiente não cria registro de idempotência

## Gatilho de revisão

1. Confirmação das janelas reais de nova tentativa dos originadores, que pode exigir retenção superior a 90 dias
2. Crescimento do armazenamento de respostas tornando-se relevante, que levaria a persistir apenas o identificador do lançamento e reconstruir a resposta
3. Entrada de operação em lote em escopo, que exige definir idempotência no nível do lote e no nível do item
