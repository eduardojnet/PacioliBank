# Processo do Quadro Kanban

**Projeto:** PacioliBank Ledger
**Quadro:** TickTick, projeto `PacioliBank`
**Espelho versionado:** [`docs/KANBAN.md`](./KANBAN.md)
**Versão:** 1.1 (2026-10-02)

Este documento é a política do quadro, não um ADR. Nomenclatura e processo são convenção: não rastreiam a um requisito, e a regra 4 do [índice de ADRs](./adr/README.md) exige rastreabilidade.

O que justifica o documento é outra coisa: o quadro é a fila de execução do projeto. Fila sem critério de entrada e saída não é fila, é lista de desejos, e já produziu duas lacunas neste projeto (L-07 e L-09).

---

## 1. Função do quadro

O quadro responde a três perguntas, e só a elas:

1. **O que está sendo feito agora?** Uma resposta, nunca três.
2. **O que entra em seguida, e por quê?** A ordem é a da [fila do ESTADO.md §7](./ESTADO.md).
3. **O que está impedido, e por quem ou pelo quê?**

O quadro **não** é repositório de conhecimento, histórico de decisão nem documentação. Esses vivem em `docs/`. Cartão que explica arquitetura é ADR no lugar errado.

---

## 2. Fonte da verdade, e a regra que impede o quadro de mentir

Esta seção existe porque os dois erros mais caros do projeto nasceram aqui.

| Pergunta | Resposta |
|---|---|
| Qual a fonte da verdade do **código**? | O disco, verificado por leitura. Não o cartão, não o `ESTADO.md` |
| Qual a fonte da verdade do **estado declarado**? | [`docs/ESTADO.md`](./ESTADO.md), seções 4, 5 e 6 |
| Qual a fonte da verdade da **ordem de execução**? | `ESTADO.md §7`. O quadro reflete essa ordem; não a define |
| O que o quadro é, então? | A superfície de gestão. Derivado, nunca primário |

### A regra

> **Nenhum cartão vai para Concluído sem verificação no disco.**
> Nenhuma entrega fecha sem que `ESTADO.md` e `README.md` sejam atualizados na mesma passada (regra 9 do `CLAUDE.md`).

### Por que, em fatos e não em teoria

| Lacuna | O que aconteceu | Causa |
|---|---|---|
| **L-07** | Dois arquivos foram dados como escritos e **não existiam no disco**. O cartão dizia concluído | Confiança no relato em vez de leitura |
| **L-09** | O `README.md` público declarava como pendente domínio, persistência, idempotência e testes, todos verdes | A regra de sincronização cobria o `ESTADO.md` e esquecia o documento que o avaliador lê primeiro |

Nos dois casos o quadro estava coerente consigo mesmo e errado sobre a realidade. Quadro internamente coerente é a forma mais confortável de estar errado.

### Procedimento de sincronização

Ordem fixa, sem ramificação:

1. Ler o disco: existe o arquivo, compila, os testes passam
2. Atualizar `ESTADO.md` seções 4, 5, 6, 7 e 11
3. Atualizar a tabela de estado do `README.md`, coerente com a §4
4. Commitar
5. **Só então** mover o cartão e escrever o que foi entregue

Inverter a ordem produz quadro verde com repositório vermelho.

**Limitação conhecida e aceita:** o quadro vive no ambiente de gestão, separado do terminal de desenvolvimento. Enquanto o trabalho corre no terminal, o quadro congela. A sincronização é manual, partindo deste documento e do `ESTADO.md`. Automatizar é item de backlog, não pré-requisito.

---

## 3. As sete colunas

Cada coluna tem critério de entrada e critério de saída. Cartão que não satisfaz o critério de entrada não entra, ainda que o trabalho seja útil.

### Não Classificado

**Permanece vazia.** É a única coluna com essa exigência.

Cartão aqui significa trabalho registrado sem critério. A ação é **triá-lo**, nunca executá-lo: decidir se vai para Backlog, A Fazer ou se é eliminado. Coluna com cartão parado é sinal de captura sem decisão.

### Backlog/Ideias

**Entra:** item real, com valor declarado, que **não** será feito neste ciclo.
**Sai:** quando o gatilho declarado no cartão ocorre.

Todo cartão aqui carrega o motivo de não estar sendo feito e o que o reabriria. "Implementar `daily_balances`" tem gatilho explícito: p99 da consulta histórica acima do alvo de RNF-002. Sem gatilho, o cartão é desejo e deve ser eliminado.

Backlog não é depósito. Cartão sem gatilho e sem valor sai do quadro.

### A Fazer

**Entra:** item do ciclo atual, com critério de conclusão escrito e sem dependência aberta.
**Sai:** quando é o próximo da fila e Em Andamento está livre.

A ordem dentro da coluna é a do `ESTADO.md §7`. Reordenar aqui sem reordenar lá quebra a sincronização, e a fila perde função.

**Antecipação:** mover um item à frente dos anteriores exige motivo registrado no cartão. Duas antecipações já ocorreram (repositório público e correção do README), ambas com motivo declarado. Uma terceira sem critério e a fila deixou de ser fila.

### Em Andamento

**Limite: um cartão.** Não é preferência, é o controle contra dispersão.

**Entra:** o próximo da fila, quando a coluna está vazia.
**Sai:** quando o critério de conclusão do cartão é satisfeito **e verificado**.

Dois cartões aqui significa que nenhum está sendo feito. O limite é o único mecanismo do quadro que protege atenção, e é o primeiro a ser violado sob pressão.

### Em Revisão

**Entra:** trabalho terminado cuja **correção ainda não foi medida**.
**Sai:** quando a medição existe e está registrada.

Esta coluna não é fila de espera por aprovação. É fila de espera por **evidência**. Dois cartões a ocupam hoje, e os dois ilustram a diferença:

- *Verificar o poder de detecção do teste de concorrência* (L-04): o teste passa, mas nunca se mediu se ele **falha** contra implementação sem bloqueio. Teste que não reprova o erro não é teste, é decoração. O cartão declara duas hipóteses antes do experimento, o que impede racionalizar o resultado depois
- *Aplicar as correções documentais pendentes* (L-05): quatro divergências identificadas, ainda não aplicadas nos documentos de origem

Cartão em revisão por mais de um ciclo volta para A Fazer ou é eliminado. Revisão eterna é conclusão disfarçada.

### Bloqueado

**Entra:** o bloqueio é **externo e nomeado**.
**Sai:** quando o bloqueio cai, ou quando é reclassificado como decisão.

Obrigatório no cartão: **o que bloqueia**, não "aguardando". Os três cartões atuais nomeiam: ausência de interlocutor de negócio, dependência de entrega anterior, ausência de ambiente de carga.

**Falta de tempo não é bloqueio.** É priorização, e vai para Backlog. "Bloqueado por mim mesmo" é hesitação com nome técnico, e o protocolo de alerta trata disso.

### Concluído

**Entra:** critério de conclusão satisfeito **e verificado no disco**.
**Sai:** nunca. Concluído é append-only, pelo mesmo princípio do ledger ([ADR-0003](./adr/ADR-0003-ledger-append-only.md)).

Erro em cartão concluído é corrigido **acrescentando** o que se descobriu, nunca apagando o que se dizia antes. A L-07 está registrada assim: o cartão conserva o que foi afirmado e o que a verificação encontrou. Rasura destrói a única evidência de que o controle funcionou.

---

## 4. Anatomia do cartão

**Modelo, não fato.** O que segue é a forma que os cartões deste quadro seguem, não uma regra universal de Kanban.

```
TÍTULO: verbo no infinitivo + objeto concreto
        "Implementar a porta de entrada e os endpoints HTTP"
        nunca "Endpoints" nem "Ver API"

CONTEÚDO, nesta ordem:

1. ÂNCORA        lacuna (L-xx), ADR, requisito (RF/RN/RNF) ou questão (QA-xxx)
                 que este cartão atende. Cartão sem âncora é preferência pessoal

2. SITUAÇÃO      o que existe hoje, em fatos verificados. Afirmação não
                 verificada leva [NVI]

3. ESCOPO        o que será feito, numerado. Se passa de sete itens, são dois cartões

4. CRITÉRIO      como se sabe que terminou, de forma verificável por terceiro.
                 "funcionando" não é critério; "build sem avisos, 57 testes
                 verdes, crédito seguido de consulta via curl" é

5. RISCO          o que acontece se não for feito. Em cartão concluído, troca por
   ou ENTREGA     o que foi entregue e como foi verificado
```

### Critério de conclusão: a regra que sustenta tudo

Um cartão sem critério verificável não pode ser concluído, porque não há como saber se terminou. Na prática ele fica em Em Andamento indefinidamente ou vai para Concluído por cansaço.

Formas aceitas de critério:

| Tipo | Exemplo deste quadro |
|---|---|
| Comando com saída esperada | "`dotnet test`: 57 passando, 0 falhando, sem avisos" |
| Artefato existindo e verificado | "Repositório público acessível, README renderizando" |
| Hash ou igualdade de referência | "`origin/main` e `HEAD` local iguais" |
| Resultado de experimento, com hipóteses declaradas antes | L-04: "saber qual das duas hipóteses é verdadeira e registrar no ADR-0005" |

### Numeração

Todo cartão carrega um número no início do título, com dois dígitos e ponto final: `19. Implementar a porta de entrada e os endpoints HTTP`.

| Faixa | Significado |
|---|---|
| 01 a 18 | Executado, na ordem em que de fato ocorreu |
| 19 a 25 | Fila ativa, na ordem do [`ESTADO.md §7`](./ESTADO.md) |
| 26 a 28 | Previsto por ADR, fora da fila atual. Entra quando a fila esvazia |
| 29 a 30 | Bloqueado sem desbloqueio previsto no desafio |
| 31 a 38 | Backlog. **A ordem aqui é indicativa**, não compromisso: o que governa é o gatilho declarado no cartão |

**Subníveis** marcam dependência real, não agrupamento temático: `19.1` só pode começar depois ou junto de `19`, e não faz sentido sozinho. O número do pai existe como cartão; não se criam cartões-pai apenas para abrigar filhos.

**Regras de atribuição:**

1. **O número não é reaproveitado.** Cartão eliminado deixa o número vago. Renumerar invalidaria toda referência externa, inclusive as deste documento
2. **Cartão novo recebe o próximo número livre da faixa a que pertence**, ou um subnível do cartão de que depende. Não se renumera a faixa para abrir espaço
3. **O número não muda quando o cartão muda de coluna.** Ele identifica a atividade, não o estado dela
4. **Concluído conserva o número de execução.** É o que permite ler a ordem real em que o projeto aconteceu, e não a ordem que se planejou

A numeração existe para que uma conversa, um commit ou este documento citem "o 19.1" sem ambiguidade. Não substitui a fila do `ESTADO.md §7`, que continua sendo a autoridade sobre a ordem.

### Prioridade

| Valor | Uso neste quadro |
|---|---|
| Alta | Bloqueante do desafio, ou risco de invariante financeira |
| Média | Entrega de escopo declarado |
| Baixa | Melhoria com valor real e sem urgência |
| Nenhuma | Fora de escopo, mantido por rastreabilidade |

Se tudo é alta, nada é. A proporção saudável neste quadro é no máximo um terço em alta.

### Etiquetas

Duas dimensões, no máximo três etiquetas por cartão.

**Natureza:** `codigo`, `doc`, `teste`, `infra`, `spec`, `analise`, `processo`
**Qualificador:** `requisito-obrigatorio`, `risco`, `condicional`, `arquitetura`

`requisito-obrigatorio` marca o que o enunciado exige explicitamente. É a etiqueta que responde "o que, se faltar, invalida a entrega". Hoje marca quatro cartões.

---

## 5. Ritual mínimo

Dois momentos, ambos curtos. Não há reunião, cerimônia nem relatório.

### Ao abrir um bloco de trabalho

1. Ler `ESTADO.md §7` e §10 (bloco de retomada). **O estado registrado é o ponto de partida, não a memória**
2. Confirmar que Em Andamento está vazia ou tem o cartão esperado
3. Mover um cartão de A Fazer para Em Andamento. Um
4. Declarar o teto de tempo antes de começar

### Ao fechar um bloco de trabalho

Executar o procedimento de sincronização da §2 deste documento, na ordem. Depois:

1. Se Não Classificado tem cartão, triá-lo
2. Se Em Revisão tem cartão de ciclo anterior, decidir: medir, devolver ou eliminar
3. Se Em Andamento ficou com cartão incompleto, registrar no cartão **onde parou**, não deixar implícito

---

## 6. Modos de falha a vigiar

Observados neste projeto ou previsíveis a partir dele.

| Sintoma | O que significa | Correção |
|---|---|---|
| Cartão em Concluído com código ausente do disco | Confiança em relato, não em leitura (L-07) | §2: ler antes de mover |
| Documento público divergente do estado real | Controle de sincronização incompleto (L-09) | Regra 9 do `CLAUDE.md` |
| Dois ou mais cartões em Em Andamento | Dispersão. Nenhum está sendo feito | Escolher um, devolver os outros |
| Cartão em Bloqueado sem nomear o bloqueio | Hesitação apresentada como impedimento | Nomear ou mover para Backlog |
| Cartão em Em Revisão por mais de um ciclo | Conclusão disfarçada | Medir, devolver ou eliminar |
| Backlog crescendo sem gatilho de reabertura | Depósito, não backlog | Eliminar o que não tem gatilho |
| Antecipação de item da fila sem motivo registrado | A fila deixou de governar | Registrar o motivo ou respeitar a ordem |
| Cartão que explica arquitetura | ADR no lugar errado | Mover para `docs/adr/` e referenciar |

---

## 7. Gatilhos de revisão deste documento

- Limite de Em Andamento violado duas vezes no mesmo ciclo
- Terceira antecipação de item da fila sem motivo registrado
- Quadro passando de 50 cartões, o que indica granularidade errada
- Entrada de segunda pessoa no projeto, que muda o significado de Em Revisão
