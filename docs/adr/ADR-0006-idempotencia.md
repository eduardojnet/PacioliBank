# ADR-0006: Exigir chave de idempotência em toda escrita, com unicidade garantida pelo banco e resposta original persistida

- **Status:** Aceito; revisado em 2026-10-02 (ver "Revisão: reconhecimento da repetição sob bloqueio")
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
    response_body     json        NOT NULL,   -- json, não jsonb: ver revisão do card 19.5
    entry_id          uuid        NOT NULL REFERENCES ledger_entries(entry_id),
    created_at        timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (account_id, idempotency_key)
);
```

O registro é gravado **na mesma transação** do lançamento ([ADR-0005](./ADR-0005-controle-de-concorrencia.md), passo 6). Não existe estado em que o lançamento exista e a chave não, ou o inverso.

### Fluxo

> **Revisado em 2026-10-02.** O fluxo original, preservado abaixo, deixava o agregado decidir antes de reconhecer a repetição. A revisão está no fim deste documento.

1. A transação bloqueia a linha da conta ([ADR-0005](./ADR-0005-controle-de-concorrencia.md))
2. **Sob esse bloqueio**, o registro da chave é lido. Se existe e `request_hash` coincide, devolve-se o resultado original com `200` e `Idempotency-Replayed: true`, sem que o agregado avalie nada; se `request_hash` difere, `409 IDEMPOTENCY_KEY_CONFLICT`
3. Sem registro, a transação segue: o agregado decide e a gravação acontece
4. Se ainda assim a inserção em `idempotency_records` violar a chave primária, a transação é desfeita, o registro existente é lido em nova conexão, e valem as mesmas respostas do passo 2

**A colisão de chave primária continua sendo o mecanismo estrutural**: é ela que impede a duplicidade, qualquer que seja o código. A leitura do passo 2 decide apenas *qual resposta* o reenvio recebe, e só é segura porque acontece sob o bloqueio. Quando duas requisições idênticas chegam ao mesmo tempo, o bloqueio as serializa: a primeira grava, a segunda encontra o registro no passo 2.

**Fluxo original (até 2026-10-02):** a transação executava até a inserção; a violação da chave primária desfazia a transação, e o registro era lido em nova conexão. Não havia consulta antes do agregado.

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
- Resposta devolvida é a original, byte a byte, não uma reconstrução. *Até 2026-10-02 esta afirmação era falsa no código; tornada verdadeira pela revisão do card 19.5, ao fim deste documento*
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

**Verificação prévia em aplicação.** Rejeitada pelo critério 1. Entre `SELECT` e `INSERT` existe uma janela, e sob a carga em que a proteção importa é exatamente nessa janela que as requisições chegam. Falha de forma intermitente e difícil de reproduzir, que é a pior forma de falhar. *(Nota de 2026-10-02: a rejeição continua valendo para a verificação feita fora de serialização, como única barreira. A revisão abaixo adota uma leitura prévia sob o bloqueio da conta, sem janela, e mantém a constraint como barreira.)*

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

## Revisão: reconhecimento da repetição sob bloqueio (2026-10-02)

### Problema

Lacuna L-10 do [ESTADO](../ESTADO.md), verificada por execução: crédito de 150, débito de 100 com a chave `k`, reenvio do mesmo débito com a mesma chave. Esperado pela RN-005: o resultado original. Obtido: `422 INSUFFICIENT_FUNDS`.

No fluxo original, o agregado decidia **antes** de a repetição ser reconhecida, e a repetição só era reconhecida **na** gravação. Se o estado da conta mudou entre o envio original e o reenvio (saldo caiu, conta foi bloqueada), o agregado recusava o reenvio, a gravação nunca acontecia, e a colisão de chave nunca era alcançada. O cliente que reenviava após timeout, exatamente o caso que esta ADR existe para proteger, recebia rejeição para um débito efetivado. Efeito colateral da mesma causa: reusar a chave com conteúdo diferente devolvia `422` em vez de `409`.

O critério 2 desta ADR ("devolve exatamente a resposta original") estava violado sempre que o estado da conta mudava, e o fluxo original não tinha teste para isso.

### Opções consideradas

1. **Leitura do registro sob o bloqueio da conta, antes do agregado** (escolhida)
2. Leitura do registro só quando o agregado rejeita
3. Gravar registro de idempotência também para comandos rejeitados
4. Manter o fluxo original e documentar a limitação

### Decisão

**Opção 1.** Depois de adquirir o bloqueio da conta e antes de reidratar a decisão do agregado, o adaptador lê o registro da chave. Existindo, responde a partir dele.

**Por que não reabre a janela que a opção "verificação prévia" tinha:** o registro de uma chave só é gravado por uma transação que detém o bloqueio daquela conta, porque todo caminho de escrita adquire o bloqueio primeiro. Em `READ COMMITTED`, a leitura feita depois de adquirir o bloqueio enxerga tudo o que o detentor anterior confirmou. Entre a leitura e a gravação não pode surgir outro registro para a mesma conta. A janela existia porque a verificação era feita fora de serialização; aqui ela é feita dentro.

**A constraint continua.** A violação da chave primária permanece no caminho, como segunda barreira, para qualquer escrita futura que não passe pelo bloqueio. A leitura decide a resposta; a constraint impede a duplicidade.

### Opções rejeitadas

**Leitura só quando o agregado rejeita.** Correta para os casos medidos, e sem custo no caminho feliz. Rejeitada por acertar por coincidência: quando o agregado *aceita* o reenvio (crédito, ou débito com saldo ainda suficiente), a correção depende de a gravação colidir na chave e desfazer tudo, e qualquer efeito acrescentado antes da gravação no futuro seria executado duas vezes. Além disso, espalha a regra "repetição devolve o original" por dois pontos do código.

**Gravar registro também para rejeições.** Rejeitada pela "decisão fina" desta ADR: rejeição de negócio não consome a chave, para que o cliente possa reenviar depois de um crédito entrar na conta.

**Manter e documentar.** Rejeitada pelo critério 2 e pelo impacto: o originador pode tratar como recusado um pagamento consumado, e estornar ou repetir por outra via, gerando divergência financeira real.

### Consequências

- **Custo:** uma leitura por chave primária a cada escrita, na mesma transação e sob o bloqueio já adquirido. Sem I/O de rede adicional. [NVI] O impacto em latência não foi medido; RNF-001 segue sem ambiente de carga (card 30)
- **Precedência das respostas, agora explícita:** repetição idêntica, depois conflito de chave, depois as regras do agregado
- **O estorno herda a correção:** o reenvio de um estorno que zerou a conta devolve o original, e não "já estornado" nem "saldo insuficiente"

### Validação

- Três testes de integração contra PostgreSQL real em `LedgerStoreTests`: reenvio após o saldo cair, reenvio com a conta bloqueada, e reuso de chave cujo novo conteúdo seria recusado por saldo. **Os três reprovaram no fluxo original** antes da correção
- `ConcurrencyTests`, envios simultâneos com a mesma chave: continua produzindo exatamente um lançamento
- Suíte completa: 97 testes verdes

### Revisão: a resposta devolvida vem do registro (2026-10-02, card 19.5)

### Problema

As consequências desta ADR afirmavam devolver "a resposta original, byte a byte, não uma reconstrução", guardada em `response_body`. O código gravava a coluna e **nunca a lia**: a repetição era reconstruída a partir de `ledger_entries`. O corpo coincidia com o original, e havia teste disso, mas por reconstrução. Além disso, o que se gravava em `response_body` era o resultado interno serializado, fora do formato do contrato. Achado no card 24.2.

Havia ainda um impedimento técnico escondido: a coluna era `jsonb`, que reordena as chaves e normaliza os espaços. Mesmo lendo a coluna, a resposta não sairia byte a byte.

### Opções consideradas

1. **Devolver de fato o `response_body`, gravado no formato do contrato** (escolhida)
2. Corrigir esta ADR: assumir a reconstrução a partir do ledger, que é imutável e portanto determinística, e reduzir `response_body` a registro de auditoria

### Decisão

**Opção 1**, por decisão do usuário. O corpo da resposta de escrita (EF §8.4) é montado uma única vez, por `PostingResponse` na camada de aplicação, gravado em `response_body` na transação do lançamento, e devolvido pela API como texto, sem reserializar, tanto na primeira resposta quanto na repetição. A coluna passa de `jsonb` para `json`, que valida o JSON e guarda o texto exatamente como recebido.

### Opção rejeitada

**Reconstruir a partir do ledger.** Correta enquanto o formato da resposta não mudar, e sem a cópia armazenada. Rejeitada pelo critério 2 desta ADR, tomado ao pé da letra: a resposta devolvida é a original, e não uma equivalente. Se o formato do contrato evoluir, a reconstrução passaria a devolver ao reenvio uma resposta diferente da que o cliente recebeu na primeira vez; a cópia gravada, não.

### Consequências

- Primeira resposta e repetição são o mesmo texto por construção: verificado no Docker, com o mesmo SHA-256 na primeira resposta, no reenvio e na coluna
- O tipo do contrato de escrita sai do adaptador HTTP e vai para a aplicação, porque é gravado onde é produzido. Os contratos de leitura (posição, extrato) continuam no adaptador
- As colunas do lançamento continuam alimentando o resultado interno da repetição; o **corpo HTTP** vem só do registro
- Mudança de esquema (`jsonb` para `json`): no ambiente local, exige `docker compose down -v` até o card 27 (DbUp). Resolvido no card 27: mudança de esquema passou a ser migração nova, sem recriar o volume ([ADR-0002](./ADR-0002-plataforma-e-armazenamento.md), revisão)
- **Validação:** teste que altera o `response_body` por fora e verifica que a repetição devolve o texto alterado, o que uma reconstrução não conseguiria; e teste que compara a primeira resposta com o texto gravado

## Gatilho de revisão desta revisão

Qualquer caminho de escrita que não adquira o bloqueio da conta (operação em lote, transferência entre contas) invalida o argumento de ausência de janela e exige reavaliar a leitura prévia para esse caminho.
