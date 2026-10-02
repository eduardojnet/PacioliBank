# ADR-0001: Adotar monolito modular com fronteiras verificadas automaticamente

- **Status:** Aceito
- **Data:** 2026-10-02
- **Decisor:** Eduardo J. G. do Carmo (Arquiteto de Soluções e Software)
- **Requisitos dirigentes:** RNF-035, RNF-037, RNF-005, R-07, CQ-07

## Contexto e problema

O sistema legado é descrito no enunciado como tendo "crescido sem muito planejamento", apresentando lentidão, instabilidade em pico e dificuldade de manutenção. O banco pede que a solução seja repensada do zero.

A tentação imediata, em contexto bancário, é responder com microsserviços. Essa resposta precisa ser examinada em vez de assumida, porque o problema declarado não é de escala organizacional nem de independência de deploy entre times: é de qualidade interna e de contenção em um sistema único.

Há ainda uma restrição explícita do desafio: a aplicação deve subir localmente com instruções claras (RNF-037). Uma topologia distribuída transforma a avaliação da solução em exercício de infraestrutura.

A pergunta é: qual estilo arquitetural resolve a dor declarada com o menor custo de operação e o maior custo de erosão futura?

## Critérios de decisão

1. **Preserva consistência forte por conta** sem transação distribuída (prioridade 1 da ENF §2)
2. **Sobe localmente em um comando** (RNF-037)
3. **Resiste à erosão de fronteiras**, que é a causa raiz declarada do legado (R-07)
4. **Permite escala horizontal** da carga prevista (RNF-005)
5. **Mantém caminho de extração** caso a escala organizacional mude

## Opções consideradas

1. Monolito em camadas tradicional (Controller, Service, Repository, transversal)
2. Monolito modular com módulos de fronteira explícita
3. Microsserviços por capacidade de negócio
4. Funções serverless por caso de uso

## Decisão

**Monolito modular**: um único artefato implantável, dividido em módulos com fronteiras explícitas e dependências verificadas por teste automatizado.

Módulos:

| Módulo | Responsabilidade | Depende de |
|---|---|---|
| `Ledger` | Lançamentos, invariantes, sequência, estorno | nenhum |
| `Balances` | Posição consolidada, snapshot, consulta temporal | `Ledger` (leitura) |
| `Accounts` | Dados de referência da conta: status, moeda, titular | nenhum |
| `Events` | Outbox, publicação de eventos | `Ledger` (leitura) |
| `Api` | Exposição HTTP, autenticação, mapeamento de erro | todos |

Regras de dependência, verificadas por NetArchTest ([ADR-0010](./ADR-0010-estrategia-de-testes.md)):

- O domínio de cada módulo não referencia infraestrutura, ASP.NET Core nem acesso a dados
- Módulos se comunicam por contrato explícito, nunca por acesso direto à persistência alheia
- `Ledger` não conhece `Api`, `Balances` nem `Events`

> **Revisão de 2026-10-02 (lacuna L-05).** O módulo de publicação chamava-se `Integration`. Foi renomeado para `Events` porque `PacioliBank.Integration` colidiria com `PacioliBank.Integration.Tests`, que são testes de integração, não testes do módulo ([convenções](../convencoes-de-nomenclatura.md) §3). A decisão não muda; muda o nome.

> **Estado da implementação em 2026-10-02.** A tabela acima é a arquitetura-alvo. Existem hoje dois módulos: `Ledger` (domínio e aplicação, em `PacioliBank.Ledger`, mais o adaptador `PacioliBank.Ledger.Persistence`) e `Api`. As responsabilidades de `Balances` (snapshot e consulta temporal) vivem dentro do adaptador de persistência do `Ledger`; `Accounts` é a tabela `ledger.accounts`, preenchida por massa local; de `Events`, só a gravação na outbox existe, também no adaptador do `Ledger`. A separação em módulos fica para quando houver pressão real de fronteira; o teste que a protegeria (NetArchTest) é o card 26. Ver [C3](../diagrams/c3-componentes.md).

**Justificativa pelos critérios:** o critério 1 é decisivo. A invariante de posição não negativa exige que lançamento, controle de sequência, registro de idempotência e evento de integração sejam gravados na **mesma transação local** ([EF](../specs/EF-especificacao-funcional.md) CU-01, passo 8). Distribuir esses elementos entre serviços substitui uma transação ACID por uma saga com estado intermediário visível, trocando um problema resolvido por um problema difícil, sem ganho correspondente. Os critérios 2 e 5 são atendidos; o critério 4 é atendido porque a unidade de serialização é a conta, não o processo ([ENF](../specs/ENF-especificacao-nao-funcional.md) §3).

## Padrão interno dos módulos

A decisão acima fixa a **forma de implantação**. Esta seção fixa o **padrão interno**, porque um monolito modular pode ser organizado de várias maneiras e a escolha muda tudo.

**Cada módulo segue Ports and Adapters** (Hexagonal, Alistair Cockburn, 2005), com DDD tático no domínio.

| Camada | Conteúdo | Dependências permitidas |
|---|---|---|
| Domínio | Agregados, entidades, Value Objects, invariantes | Nenhuma |
| Aplicação | Casos de uso e **portas** (interfaces) | Domínio |
| Adaptadores | Implementações das portas: persistência, HTTP, mensageria | Aplicação e domínio |

**A inversão é física, não convencional.** `PacioliBank.Ledger.Persistence` referencia `PacioliBank.Ledger`; o inverso não existe e não pode existir. O projeto de domínio não declara nenhum `PackageReference`, de modo que referenciar Npgsql a partir dele é erro de compilação, não achado de revisão. Mesmo princípio do [ADR-0009](./ADR-0009-seguranca-e-privilegio-minimo.md): garantia estrutural em vez de disciplina.

### Desvio consciente: orquestração transacional no adaptador

Em Ports and Adapters ortodoxo, o adaptador de persistência seria um repositório sem lógica, e um caso de uso na camada de aplicação coordenaria a sequência: carregar a conta, validar, persistir, publicar o evento.

**Aqui a orquestração vive no adaptador** (`PostgresLedgerStore`), e a porta `ILedgerStore` expõe a operação inteira em vez dos passos.

Razão: **a ordem dos passos é o mecanismo de concorrência**. Bloquear a linha da conta antes de ler a posição não é detalhe de persistência, é a garantia de RN-001 ([ADR-0005](./ADR-0005-controle-de-concorrencia.md)). Expor os passos separadamente permitiria compô-los na ordem errada, e a ordem errada aqui significa posição negativa ou duplicidade financeira. A porta é estreita e orientada a caso de uso por essa razão, não por descuido.

**O custo é real:** o caso de uso não é testável sem banco de dados. Essa troca é a razão de o [ADR-0010](./ADR-0010-estrategia-de-testes.md) exigir banco real via Testcontainers. Um repositório em memória passaria na implementação ingênua, o que é pior que não testar.

### Alternativas de padrão interno rejeitadas

**Clean Architecture canônica.** Quatro camadas concêntricas com input e output boundary por caso de uso, presenters e DTOs de travessia em cada fronteira. Rejeitada por desproporção: com dois casos de uso de escrita e três de leitura, as camadas adicionais produziriam objetos que apenas repetem dados, sem proteger invariante alguma. A inversão de dependência, que é o valor real das duas abordagens, já está garantida pela estrutura de projetos. *Voltaria a ser considerada* se o sistema ganhasse múltiplas bordas de entrada com formatos de apresentação distintos.

**Arquitetura em camadas tradicional.** Rejeitada pela mesma razão do monolito tradicional acima: camadas horizontais não criam fronteira de domínio.

**Transaction script.** Procedimentos que leem, calculam e gravam sem modelo de domínio. Mais rápido de escrever e perfeitamente adequado a CRUD. Rejeitada porque as invariantes ficariam espalhadas pelos procedimentos, sem um lugar único onde "a posição nunca é negativa" seja verificável. Em sistema financeiro, invariante sem dono é invariante que se perde na terceira alteração.

## Consequências

**Positivas**

- Consistência forte por conta obtida com transação local, sem coordenação distribuída
- Ambiente local completo em um comando, atendendo RNF-037 e CQ-07
- Depuração e rastreamento ponta a ponta em um único processo
- Custo de infraestrutura e de operação significativamente menor
- Refatoração entre módulos é compilada e verificada, não contratada e versionada

**Negativas**

- Todos os módulos escalam juntos: aumentar capacidade de leitura implica subir instâncias que também carregam a escrita. Mitigação prevista: separar a execução por perfil (instâncias dedicadas a leitura e a escrita a partir do mesmo artefato), sem dividir o código
- Falha em um módulo pode comprometer o processo inteiro. Mitigação: disjuntor e isolamento de recursos (RNF-014)
- Uma única stack tecnológica para todos os módulos
- Fronteiras internas dependem de verificação ativa; sem o teste de arquitetura, a erosão é questão de tempo

**Neutras**

- Um repositório, um pipeline, uma versão
- O caminho de extração para serviço independente permanece aberto, ao custo de substituir chamada em processo por chamada remota e de reavaliar a fronteira transacional

## Análise das opções rejeitadas

**Monolito em camadas tradicional.** Rejeitado porque camadas horizontais não criam fronteira de domínio: qualquer serviço pode chamar qualquer repositório, e é exatamente assim que um sistema "cresce sem muito planejamento". Repetiria o defeito que se pretende corrigir.

**Microsserviços por capacidade de negócio.** Rejeitado por três razões cumulativas: fragmentaria a transação que sustenta a invariante crítica; violaria RNF-037; e resolveria um problema de escala organizacional que o enunciado não apresenta. *Voltaria a ser a melhor escolha* quando houver times independentes com cadências de release conflitantes, ou quando um módulo exigir perfil de escala radicalmente distinto dos demais de forma sustentada.

**Serverless por caso de uso.** Rejeitado por partida a frio incompatível com RNF-001, por dificuldade de manter pool de conexões sob alta concorrência e por inviabilizar o ambiente local de um comando. *Voltaria a ser considerado* para cargas esporádicas de processamento em lote, como reconstrução de snapshot.

## Validação

- Teste de arquitetura (NetArchTest) reprovando qualquer dependência fora do grafo declarado, executado a cada commit
- `docker compose up` em container limpo no CI, validando RNF-037
- Revisão obrigatória de ADR para qualquer requisito que atravesse a fronteira de escopo da [EF](../specs/EF-especificacao-funcional.md) §3.2

## Gatilho de revisão

Qualquer um dos eventos abaixo obriga a reabrir esta decisão:

1. Mais de um time com cadência de release independente sobre o mesmo código
2. Necessidade sustentada de escalar um módulo em perfil incompatível com os demais
3. Entrada de transferência entre contas em escopo, que altera a fronteira transacional ([ENF](../specs/ENF-especificacao-nao-funcional.md) §5.2)
