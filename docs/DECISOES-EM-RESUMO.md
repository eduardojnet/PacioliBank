# Decisões em Resumo

**PacioliBank Ledger** · as seis perguntas do enunciado, respondidas no registro de decisão arquitetural.

Cada seção segue a mesma estrutura, que é a do [MADR](./adr/) usado em todo o projeto:

**Decisão** · **critérios fixados antes de avaliar as opções** · **opções rejeitadas, com o gatilho que as reabriria** · **trade-off aceito** · **como foi verificado**.

A ordem importa. Critério definido depois da escolha é justificativa, não critério, e é a forma mais comum de preferência pessoal disfarçada de arquitetura. Os catorze ADRs em [`docs/adr/`](./adr/) trazem cada decisão por inteiro; o estado verificado está em [`docs/ESTADO.md`](./ESTADO.md).

---

## 1. Como a solução está estruturada

**Decisão:** uma aplicação, monolito modular em Ports and Adapters, com fronteiras verificadas pelo compilador e por teste.

### Critérios, fixados antes de olhar as opções

1. Preserva consistência forte por conta **sem transação distribuída** (prioridade 1 da ENF §2)
2. Sobe localmente em um comando (RNF-037)
3. **Resiste à erosão de fronteiras**, que é a causa raiz declarada do legado (R-07)
4. Permite escala horizontal da carga prevista (RNF-005)
5. Mantém caminho de extração se a escala organizacional mudar

O critério 3 é o que governa. O enunciado não descreve um sistema lento: descreve um sistema cujas fronteiras se dissolveram. Qualquer estilo que não enderece isso resolve o sintoma.

### Opções rejeitadas

| Opção | Por que caiu | O que a reabriria |
|---|---|---|
| **Monolito em camadas** (Controller, Service, Repository) | Camadas horizontais não criam fronteira de domínio: qualquer serviço chama qualquer repositório. É exatamente assim que um sistema "cresce sem muito planejamento". Repetiria o defeito que se quer corrigir | Nada. Não atende o critério 3 por construção |
| **Microsserviços por capacidade** | Três razões cumulativas: fragmentaria a transação que sustenta a invariante crítica; violaria RNF-037; e resolveria um problema de escala **organizacional** que o enunciado não apresenta | Times independentes com cadências de release conflitantes, ou um módulo com perfil de escala sustentadamente distinto |
| **Serverless por caso de uso** | Partida a frio incompatível com RNF-001, pool de conexões sob alta concorrência, e inviabiliza o ambiente local de um comando | Cargas esporádicas em lote, como reconstrução de snapshot |

### O que torna a fronteira real, e não declarada

Fronteira em diagrama não sobrevive a prazo apertado. Três mecanismos a sustentam sem depender de disciplina:

- **`PacioliBank.Ledger` não declara nenhum `PackageReference`.** Referenciar Npgsql no domínio é erro de compilação, não comentário em revisão de código
- **Seis regras de dependência em NetArchTest**, e duas delas foram medidas reprovando o que violam
- **Única raiz de composição** (`Program.cs`): um só lugar conhece todas as camadas

### Trade-off aceito

Um processo único é um domínio de falha único: um defeito de memória derruba todos os módulos juntos. Aceito porque a alternativa, no volume previsto, troca esse risco por transação distribuída, que é um risco maior num sistema cuja invariante central é financeira.

> [ADR-0001](./adr/ADR-0001-estilo-arquitetural.md) · `tests/PacioliBank.Architecture.Tests` · [C2 e C3](./diagrams/)

---

## 2. Tecnologias, frameworks e padrões

**Decisão:** pilha pequena e madura, cada item amarrado a um requisito nomeado.

### O princípio que governou a escolha

Toda tecnologia é um passivo: precisa ser atualizada, auditada, e alguém precisa saber operá-la. Entra quem paga esse custo resolvendo um requisito que de outro modo ficaria sem resposta.

| Escolha | Requisito que ela resolve |
|---|---|
| **.NET 10 LTS** | Suporte longo; C# é exigência do enunciado |
| **PostgreSQL 17** | `FOR NO KEY UPDATE`, `SKIP LOCKED`, `numeric` exato e particionamento declarativo. As quatro são usadas |
| **Dapper**, não ORM completo | O SQL **é** a arquitetura aqui (ver §3). Abstrair o bloqueio seria esconder a decisão central |
| **DbUp em passo separado** | RNF-038, e a aplicação sem privilégio de alterar esquema (ADR-0002, revisão do card 27) |
| **Testcontainers** | Invariante de persistência contra PostgreSQL real. Repositório em memória **aprova** a implementação ingênua, o que é pior que não testar |
| **Serilog + OpenTelemetry** | RNF-030 a RNF-032, com mascaramento no formatador (ver §5) |

### Padrões, e o problema de cada um

- **Ledger append-only (partidas dobradas):** a posição é dado derivado. Permite responder "qual era o saldo em 20 de janeiro" sem estrutura adicional, e torna toda alteração auditável por construção
- **Idempotência por chave obrigatória:** detectada pela violação da restrição única, nunca por consulta prévia. Consulta prévia abre janela de corrida, e é nela que as requisições simultâneas chegam
- **Outbox transacional:** o evento é gravado na transação do lançamento. Não existe lançamento sem evento, nem evento sem lançamento
- **Snapshot amortizado, síncrono:** teto de custo na leitura sem introduzir processo assíncrono
- **Particionamento por mês de registro** com as chaves de unicidade numa tabela não particionada (ADR-0013)

### Uma escolha que foi desfeita, e por quê

O **exportador Prometheus** para as métricas foi rejeitado: em 04/10/2026 só existia em versão beta. Critério explícito do ADR-0012: só pacotes estáveis no caminho de produção. Volta a ser considerado quando houver versão estável.

Registrar a rejeição por maturidade, com a data e a versão, é diferente de não ter avaliado.

> [ADR-0002](./adr/ADR-0002-plataforma-e-armazenamento.md) · [ADR-0012](./adr/ADR-0012-observabilidade.md) · [`ESTADO.md` §9](./ESTADO.md)

---

## 3. Consistência, concorrência e falha

**Decisão:** a conta é a unidade de serialização. Bloqueio pessimista da linha da conta, dentro da transação de escrita, antes de ler a posição.

Esta é a decisão central do sistema. As demais se acomodam a ela.

### Critérios, fixados antes

1. Garante posição não negativa sob **qualquer** grau de concorrência
2. Garante sequência monotônica sem lacuna nem duplicata (RN-006)
3. **Não introduz contenção entre contas distintas** (RNF-005)
4. Falha de forma previsível, convertendo conflito em nova tentativa invisível (RNF-004)
5. Mantém portabilidade razoável entre SGBDs (R-06)

### A ordem dos passos é a arquitetura

```
BEGIN
SET LOCAL lock_timeout = '3s'
SELECT ... FROM accounts WHERE account_id = @id FOR NO KEY UPDATE   <- bloqueia
  ler a posição corrente                                            <- depois
  o agregado decide (recusa se ficaria negativa)
  gravar lançamento + sequência + idempotência + outbox juntos
COMMIT
```

Ler a posição **depois** do bloqueio é o que torna a decisão confiável. Antes, seria uma fotografia que outra transação já invalidou. Trocar esses dois passos compila, passa em teste com repositório em memória, e quebra a invariante em silêncio sob concorrência.

**O agregado não carrega os lançamentos.** É reidratado sob bloqueio com a posição já calculada. Carregar o histórico para validar um débito reintroduziria exatamente a degradação do legado.

### Opções rejeitadas

| Opção | Por que caiu | O que a reabriria |
|---|---|---|
| **Controle otimista puro** | Critério 4: sob contenção, a taxa de nova tentativa cresce e o trabalho útil cai, porque a validação de saldo é refeita a cada tentativa. Em conta quente, degrada de forma **não linear** | Contenção por conta comprovadamente próxima de zero, com o custo do bloqueio mensurável |
| **Bloqueio consultivo** (`pg_advisory_xact_lock`) | Tecnicamente mais limpo, e mesmo assim rejeitado: exige mapear o identificador para `bigint`, e hash introduz colisão, que faz duas contas distintas disputarem o mesmo bloqueio. Violaria o critério 3 **de forma silenciosa**: a correção se mantém, o desempenho não | O controle de sequência sair de uma linha atualizada, ou o inchaço se mostrar limitante |
| **Isolamento `SERIALIZABLE`** | O PostgreSQL o implementa com detecção otimista: sob contenção aborta e exige refazer tudo. Mesma fragilidade do otimista, com custo em **todas** as transações, inclusive as que não concorrem | |
| **Fila com escritor único por conta** | Custo operacional: roteamento estável, rebalanceamento, e inversão da API para assíncrona, alterando o contrato de todos os originadores | **É a mitigação planejada para R-01**, aplicável só às contas quentes identificadas |

### A medição que contrariou a hipótese

O ADR-0005 afirmava que o teste de concorrência deveria reprovar sem o bloqueio. Isso nunca tinha sido medido. O experimento (card 22) removeu o `FOR NO KEY UPDATE` e mediu, com duas hipóteses declaradas **antes** do resultado.

O que apareceu: a restrição de sequência preservou a invariante financeira sozinha. O que se perdeu foi **disponibilidade**, com 24% a 78% de respostas `503`.

Conclusão que mudou o texto do ADR: **o bloqueio não é o que impede saldo negativo. É o que impede o sistema recusar requisições sob concorrência.** A defesa em profundidade é real, e o critério que eu havia escrito estava errado.

Declarar as hipóteses antes é o que impede racionalizar o resultado depois.

### Transferência entre contas: a mesma decisão, estendida

Duas contas na mesma transação, bloqueadas em **ordem crescente de identificador**. A saga com conta transitória, que a própria ENF §5.2 recomendava, foi rejeitada: haveria estado intermediário visível, e a conta transitória, como toda conta, é serializada pelo seu bloqueio, então **toda transferência do sistema disputaria a mesma linha**. Um ponto único de contenção criado para evitar um que não existe.

Bloquear na ordem do sentido (origem, depois destino) também foi rejeitado, e medido: A para B e B para A simultâneas produzem impasse, o PostgreSQL aborta uma após `deadlock_timeout`, e o teste reprova.

### Trade-off aceito

Contenção dentro da mesma conta é real e não foi eliminada: é o preço da consistência forte. A mitigação está nomeada (fila por conta quente, R-01), não implementada, e o gatilho é a medição, não a intuição.

> [ADR-0003](./adr/ADR-0003-ledger-append-only.md), [ADR-0005](./adr/ADR-0005-controle-de-concorrencia.md), [ADR-0006](./adr/ADR-0006-idempotencia.md), [ADR-0014](./adr/ADR-0014-transferencia-entre-contas.md) · 106 testes de integração

---

## 4. Alta demanda e indisponibilidade parcial

**Decisão:** custo de leitura constante em relação ao histórico, degradação nomeada antes de ocorrer, e recusa rápida em vez de espera indefinida.

### Critérios do snapshot, fixados antes

1. Custo de leitura **constante** em relação ao histórico da conta (RNF-003)
2. Resultado idêntico ao cálculo integral pelo ledger, **sempre** (RN-010)
3. Ausência de snapshot não produz erro, apenas lentidão (RF-010)
4. **Mínimo de componentes móveis:** cada processo assíncrono é um ponto de falha e de atraso (R-03)
5. Não aumenta a duração do bloqueio de escrita

### Opções rejeitadas

| Opção | Por que caiu | O que a reabriria |
|---|---|---|
| **Sem snapshot** | Critério 1: o custo cresceria com o histórico e ampliaria a duração do bloqueio proporcionalmente à **idade da conta** | |
| **Projetor assíncrono** | Critério 4: acrescenta componente, atraso e modo de falha silencioso para resolver um problema que a geração amortizada resolve sem nenhum dos três | O cálculo da posição não caber mais na transação de escrita |
| **Saldo materializado** a cada lançamento | Criaria **segunda fonte da verdade** passível de divergir do ledger (RN-010). É o modelo do legado com uma camada a mais | |
| **Cache externo** como mecanismo primário | Dependência cuja indisponibilidade precisa ser tratada (RNF-010), e cuja invalidação é fonte conhecida de inconsistência | Permanece disponível **acima** do snapshot, com invalidação por `sequence`, se o volume de leitura justificar |

### Como o sistema se comporta quando algo falha

- **Snapshot e cache são descartáveis.** Apagá-los não altera nenhuma resposta do sistema, só o tempo de resposta. É consequência direta de o ledger ser a única fonte da verdade, e é o teste que distingue cache de verdade paralela
- **`lock_timeout` de 3 segundos:** sob contenção extrema o sistema **recusa rápido** em vez de acumular conexões presas. Recusa explícita é tratável pelo cliente; espera indefinida esgota o pool e derruba o serviço inteiro, inclusive para as contas que não disputam nada
- **Vivo e pronto são verificações distintas.** `/health/live` não depende de nada; `/health/ready` consulta o PostgreSQL. Banco fora do ar tira a instância do balanceador sem matar o processo
- **Outbox com `SKIP LOCKED`:** vários despachantes em paralelo sem coordenação externa. Falha na publicação tem recuo exponencial e limite com alerta, e o lançamento **já está gravado**: a entrega atrasa, o dado não se perde
- **Entrega ao menos uma vez, declarada no contrato.** O consumidor precisa ser idempotente, e isso está escrito, não presumido
- **Particionamento por mês de registro:** o ledger cresce de forma monotônica e nada é apagado (R-04). A partição dá caminho para arquivar período sem mover linha de uma tabela que a aplicação não pode alterar

### Limitação assumida, e por que ela é registrada

O snapshot é ancorado em **sequência**, que é ordem de registro; a consulta histórica usa `occurred_at`, que é ordem do fato. Com lançamento retroativo permitido (RN-012), os dois divergem, e o snapshot por sequência não serve à consulta histórica sem verificação adicional. Por isso a consulta histórica tem mecanismo próprio: o fechamento diário (card 32).

Registrar a limitação no ADR, em vez de descobri-la em produção, é a diferença entre decisão e sorte.

> [ADR-0007](./adr/ADR-0007-snapshot-e-projecao.md), [ADR-0008](./adr/ADR-0008-outbox-transacional.md), [ADR-0013](./adr/ADR-0013-particionamento-do-ledger.md) · [ENF §11](./specs/ENF-especificacao-nao-funcional.md)

---

## 5. Proteção dos dados sensíveis

**Decisão:** o serviço não guarda dado que identifique pessoa, e não tem privilégio para alterar o que gravou.

### A fronteira, primeiro

**O Ledger não autentica.** A identidade chega pronta da borda, e a autorização de escopo amplo é do gateway. Este serviço responde pela **segunda camada**: a que vale mesmo que a primeira falhe.

Essa separação é deliberada. Controle que depende de uma única camada funcionar é controle único, não defesa em profundidade.

### Critérios, fixados antes

1. Nenhum identificador íntegro na saída, por **nenhum** caminho: propriedade, mensagem renderizada ou texto de exceção
2. Verificável por teste automatizado que varre a saída real
3. Toda entrada emitida dentro de uma requisição carrega a correlação dela
4. O núcleo continua sem pacote externo (ADR-0001)

### Os quatro controles

**Minimização, que é a proteção mais forte aqui.** O esquema guarda `customer_id`, identificador opaco. Não há CPF, nome, endereço ou contato em lugar algum, porque nenhum deles é necessário para registrar lançamento ou calcular posição. Consequência num vazamento: o atacante obtém valores ligados a identificadores opacos; correlacionar com pessoas exigiria comprometer também o Cadastro.

**Privilégio mínimo, verificado por teste.** `pacioli_runtime` recebe `SELECT, INSERT` no ledger. Existe teste de integração que tenta `UPDATE` e `DELETE` num lançamento gravado e **exige a falha**. Três papéis separados: migrador altera esquema, runtime opera, readonly consulta.

**Resposta opaca.** Conta de terceiro e conta inexistente devolvem a **mesma** resposta ao cliente final: `404 ACCOUNT_NOT_FOUND`. Devolver `403` para uma e `404` para a outra permitiria a qualquer pessoa autenticada **enumerar quais contas existem**. Em sigilo bancário, a existência da relação entre cliente e banco é, ela própria, informação protegida.

**Mascaramento no formatador do log**, não em enriquecedor.

### A opção rejeitada que explica o critério 1

O ADR-0009 sugeria **enriquecedor de propriedades** para mascarar. Foi rejeitado na implementação (ADR-0012) porque o enriquecedor só altera propriedades: a mensagem renderizada e o texto da exceção chegam à saída por outros caminhos. O teste do formatador mostrou o vazamento pela exceção.

Também rejeitado: mascarar só campos com nome de conta ou cliente. A rota `/api/v1/accounts/{id}/credits` carrega a conta num campo de texto qualquer, e o teste de caminho completo reprova essa implementação.

E rejeitado: **deixar o log íntegro e proteger o acesso a ele.** Pelo mesmo princípio do ADR-0009: controle que depende de ninguém copiar o log para onde não devia é disciplina, não controle.

### Uma contradição resolvida, não escondida

A EF §8.6 e o BDD F09 especificavam `403` para conta de terceiro. A análise de sigilo mostrou que isso vaza a existência da conta. A divergência foi **registrada no ADR**, e a especificação corrigida depois, com nota de revisão.

Especificação corrigida em silêncio perde a rastreabilidade de por que mudou, e é indistinguível de erro não percebido.

### Trade-off aceito

Mascarar identificadores no log dificulta investigação: recuperar o valor íntegro exige consulta à base com o papel de leitura. Aceito: a alternativa é log que, copiado para qualquer lugar, carrega o dado.

> [ADR-0009](./adr/ADR-0009-seguranca-e-privilegio-minimo.md), [ADR-0012](./adr/ADR-0012-observabilidade.md) · [ERD](./diagrams/ERD-esquema-ledger.md)

---

## 6. O essencial a documentar para outra pessoa evoluir

**Decisão:** documentar a **decisão**, não a implementação. Quem entra precisa do porquê, do estado verificado, e do que vem depois.

Código já diz o que o sistema faz. O que ele não diz, e se perde quando a pessoa sai, é **o que foi considerado e descartado, e sob qual condição a escolha deixa de valer**.

### A ordem de leitura

| Documento | Responde | Natureza |
|---|---|---|
| [`docs/ESTADO.md`](./ESTADO.md) | O que existe, o que falta, a fila | **Vivo**, atualizado a cada entrega |
| [`docs/adr/`](./adr/) | Por que é assim | **Imutável**: decisão superada ganha revisão datada |
| [`docs/specs/EF`](./specs/EF-especificacao-funcional.md) | Regras (RN), requisitos (RF), contrato | Versionada |
| [`docs/diagrams/`](./diagrams/) | C4 e ERD, cada elemento com seu estado | Versionada |
| [`README.md`](../README.md) | Subir e fazer um lançamento em cinco minutos | Verificada em clone limpo |

### As quatro regras que sustentam isso

Valem mais que a lista de arquivos, porque sem elas a lista apodrece:

1. **Nenhuma decisão arquitetural sem ADR**, com alternativa rejeitada e a condição que a reabriria. **ADR sem alternativa rejeitada é documentação de implementação, não registro de decisão**
2. **Separar sempre implementado de especificado.** Apresentar um como o outro é, em contrato real, informação incorreta prestada ao cliente
3. **Marcar `[NVI]`** o que não foi verificado diretamente
4. **Decisão superada não é editada.** Cria-se revisão datada com a razão da mudança. Mesmo princípio do ledger: corrige-se por contrapartida, nunca por rasura

### A prova de que as regras operam

Quatro lacunas (L-13 a L-16) foram abertas e fechadas por conferência dos documentos **contra o código**, e as quatro tinham a mesma forma: **requisito declarado como realizado sem nunca ter sido medido.** Cobertura (RNF-036), desempenho (RNF-003, RNF-006), observabilidade (RNF-030 a 032), e um ponto de extensão que não existia.

Encontrar defeito no código é o esperado. Encontrar **afirmação não verificada na própria documentação** é o que a regra 3 existe para produzir, e é mais difícil, porque exige duvidar do que você mesmo escreveu.

### O que um recém-chegado quebraria sem saber

- Aviso de compilação é erro (`TreatWarningsAsErrors`, analisadores em `Recommended`). Suprimir para destravar é proibido
- A ordem dos passos na transação de escrita é a arquitetura. Trocá-los compila e quebra a invariante em silêncio
- Teste de invariante de persistência roda contra PostgreSQL real. Em memória aprovaria a implementação errada
- O CI reprova abaixo de 85% de cobertura e de mutantes mortos, e o **poder de detecção do próprio CI foi medido**: um aviso introduzido de propósito o deixa vermelho

> [`docs/REGRAS.md`](./REGRAS.md) (dez regras invioláveis) · [`docs/PROCESSO-KANBAN.md`](./PROCESSO-KANBAN.md) · [ADR-0010](./adr/ADR-0010-estrategia-de-testes.md)

---

## A tese, se houver tempo para uma frase

O enunciado descrevia um sistema que degradava sob carga e cujas fronteiras se dissolveram. A resposta não foi mais tecnologia.

Foi **identificar o ponto de contenção** (a conta), **serializar só ali**, **colocar cada invariante financeira em restrição e privilégio de banco**, onde nenhum código futuro a viola por descuido, e **medir o que foi afirmado**, inclusive quando a medição contrariou o que estava escrito.
