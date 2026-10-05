# ADR-0010: Testar invariantes contra banco de dados real, com critério de bloqueio objetivo

- **Status:** Aceito
- **Data:** 2026-10-02
- **Decisor:** Eduardo J. G. do Carmo
- **Requisitos dirigentes:** RNF-034, RNF-035, RNF-036, RNF-037, RNF-038, RNF-039, BDD §7

## Contexto e problema

O desafio exige testes automatizados. O requisito, isolado, é fácil de atender mal: uma suíte de testes de unidade com repositórios simulados produz cobertura alta, executa em segundos e **não detecta nenhum dos defeitos que este sistema precisa evitar**.

A razão é específica. As invariantes críticas aqui não residem no código de domínio isolado. Residem na interação entre o código e o banco de dados:

- A posição não negativa depende do bloqueio de linha ([ADR-0005](./ADR-0005-controle-de-concorrencia.md))
- A sequência sem lacunas depende de uma constraint única
- A idempotência sob envio simultâneo depende da violação de chave primária ([ADR-0006](./ADR-0006-idempotencia.md))
- A imutabilidade depende de privilégio ([ADR-0009](./ADR-0009-seguranca-e-privilegio-minimo.md))

Um repositório em memória não tem isolamento transacional, não tem bloqueio, não tem constraint e não tem privilégio. Um teste de concorrência executado contra ele **passa na implementação ingênua**, aquela que lê o saldo, valida e grava sem nenhuma proteção. Esse teste é pior do que ausência de teste, porque produz confiança injustificada.

A decisão, portanto, não é "quais ferramentas usar". É **onde cada invariante pode ser verificada com significado**.

## Critérios de decisão

1. Cada invariante é verificada na camada onde ela realmente existe
2. Teste de concorrência falha contra a implementação ingênua
3. Suíte executável em máquina limpa, sem configuração manual
4. Critério de bloqueio objetivo, não sujeito a negociação por prazo
5. Tempo de execução compatível com feedback a cada commit

## Opções consideradas

1. Unidade com simulação de dependências, exclusivamente
2. Unidade mais integração com banco em memória
3. Unidade mais integração com banco real efêmero
4. Testes ponta a ponta contra ambiente compartilhado

## Decisão

**Pirâmide em cinco camadas, com banco de dados real para tudo que envolve invariante de persistência.**

| Camada | Verifica | Ferramenta | Execução |
|---|---|---|---|
| Domínio | Invariantes puras: `Money`, escala, moeda, sinal, regras de estorno | xUnit | Segundos, sem I/O |
| Integração | Transação, constraint, privilégio, cenários BDD | Reqnroll + Testcontainers (PostgreSQL) | Banco efêmero por execução |
| Concorrência | Serialização por conta, sequência, idempotência simultânea | xUnit + Testcontainers | Paralelismo real com barreira |
| Arquitetura | Direção de dependência entre módulos | NetArchTest | Sobre os assemblies compilados |
| Contrato | Conformidade do payload e estabilidade do OpenAPI | Verify (comparação por instantâneo) | Sobre a especificação gerada |

### A regra central

**Toda invariante que depende do banco é testada contra o banco.** Testcontainers sobe um PostgreSQL real por execução, aplica as migrações reais ([ADR-0002](./ADR-0002-plataforma-e-armazenamento.md)) e cria os papéis reais com os privilégios reais ([ADR-0009](./ADR-0009-seguranca-e-privilegio-minimo.md)).

Consequência direta: o teste que verifica que `UPDATE` em `ledger_entries` é recusado só tem significado se executado com o papel `pacioli_runtime` contra um PostgreSQL de verdade. Contra um simulador, ele testaria o simulador.

### Teste de concorrência com significado

O cenário decisivo é [BDD](../specs/BDD-comportamento.md) F07: cinquenta débitos simultâneos sobre saldo que comporta dez.

Exigências de execução, que fazem parte da decisão:

- Paralelismo real, com todas as tarefas liberadas por uma barreira de sincronização comum, não disparadas em laço
- Banco de dados real, com o mesmo esquema e os mesmos índices de produção
- Asserção sobre o resultado agregado: exatamente dez aceitos, posição final zero, nunca negativa em nenhum instante

**Critério de qualidade do próprio teste:** ele deve falhar quando executado contra uma implementação sem bloqueio. Um teste de concorrência que passa nos dois casos não está testando concorrência, e isso deve ser verificado uma vez, deliberadamente, durante o desenvolvimento.

### Critério de bloqueio

Modelo proposto, a ser confirmado como política:

| Condição | Efeito |
|---|---|
| Qualquer cenário `@critico` reprovado | Bloqueia a publicação, sem exceção |
| Qualquer aviso de compilação | Bloqueia o build (RNF-034) |
| Cobertura do projeto de domínio inferior a 85% | Bloqueia a publicação |
| Teste de arquitetura reprovado | Bloqueia o build |
| Alteração não intencional no contrato OpenAPI | Bloqueia a publicação |
| Vulnerabilidade de severidade alta em dependência | Bloqueia a publicação |

A expressão "sem exceção" é a parte relevante. Critério de bloqueio que admite dispensa sob pressão de prazo é documentação, não controle. Em sistema financeiro, a dispensa concedida uma vez vira precedente.

**Sobre a métrica de cobertura:** 85% no domínio é indicador de atenção, não de qualidade. Cobertura alta com asserções fracas é autoengano. O critério que importa é a aprovação dos cenários `@critico`, que verificam comportamento observável. A cobertura serve apenas para detectar área de domínio esquecida.

> **Estado do critério de bloqueio em 2026-10-04 (card 31).** O CI no GitHub Actions aplica a cada push: aviso de compilação, teste de arquitetura, contrato OpenAPI e todos os testes, inclusive os de integração e a coleção do Insomnia (não há marcação `@critico` nos testes xUnit; toda reprovação bloqueia). **Ainda não aplica dois itens da tabela:** cobertura mínima do domínio, que medida está em 56,8% só com os testes de domínio e 74,9% somando os de integração (lacuna L-13, card 31.2, decisão pendente), e vulnerabilidade em dependência (card 31.1).

### Organização da suíte

```
tests/
├── PacioliBank.Domain.Tests/          # rápido, sem I/O
├── PacioliBank.Integration.Tests/     # Reqnroll + Testcontainers
│   └── Features/                      # arquivos .feature do BDD
├── PacioliBank.Concurrency.Tests/     # paralelismo real
├── PacioliBank.Architecture.Tests/    # NetArchTest
└── PacioliBank.Contract.Tests/        # instantâneo do OpenAPI
```

> **Revisão de 2026-10-02 (lacuna L-05).** O prefixo era `Ledger.`. Substituído por `PacioliBank.`, porque `Ledger` passou a designar um módulo, não o sistema ([convenções](../convencoes-de-nomenclatura.md) §3).

> **Estado da implementação em 2026-10-02.** A organização acima é a alvo. Existem hoje quatro projetos: `PacioliBank.Domain.Tests` (66 testes), `PacioliBank.Integration.Tests` (57 testes, 7 deles de migração, card 27), `PacioliBank.Contract.Tests` (2 testes, card 19.3) e `PacioliBank.Architecture.Tests` (6 regras, NetArchTest 1.3.2, cards 26 e 27). Contagens atualizadas em 2026-10-03. O de integração usa xUnit e Testcontainers, **sem Reqnroll**: os cenários do BDD foram traduzidos para testes xUnit nomeados em português, e não executados a partir dos `.feature`. A concorrência está dentro de `Integration.Tests` (`ConcurrencyTests`), não em projeto próprio. **O teste de contrato compara por instantâneo sem a biblioteca Verify** citada na tabela acima: a comparação de um único arquivo é feita no próprio teste, que grava o documento recebido ao lado do aprovado quando divergem. A decisão, comparar por instantâneo, não muda; muda a ferramenta, para evitar uma dependência e sua árvore de pacotes. Verificado: acrescentar um campo à resposta de posição reprova o teste. Consequência a declarar: a frase abaixo, "a especificação é o teste", vale como alvo; hoje existe tradução intermediária, e ela pode divergir do BDD.

Os arquivos `.feature` são os do [BDD](../specs/BDD-comportamento.md), copiados sem adaptação. A especificação é o teste; não existe tradução intermediária que possa divergir.

### Tempo de execução

As camadas de domínio, arquitetura e contrato executam em segundos e rodam a cada compilação local. Integração e concorrência levam de um a dois minutos pela subida do container, e rodam a cada commit no pipeline. Reutilização do container entre classes de teste mantém o tempo dentro do critério 5.

## Consequências

**Positivas**

- Invariantes críticas verificadas onde de fato existem, atendendo ao critério 1
- Teste de concorrência com valor real de detecção, atendendo ao critério 2
- Nenhuma configuração manual: o container é o ambiente, atendendo ao critério 3
- Cenários BDD executados sem tradução, eliminando divergência entre especificação e teste
- Erosão de fronteira entre módulos detectada automaticamente, sustentando o [ADR-0001](./ADR-0001-estilo-arquitetural.md) e mitigando R-07
- Alteração acidental de contrato de API detectada antes da publicação

**Negativas**

- **Suíte de integração é ordens de grandeza mais lenta que unidade.** Mitigado por separação de projetos e reutilização de container, mas o ciclo de feedback da camada de integração é de minutos, não de segundos
- Docker torna-se dependência obrigatória de desenvolvimento e de CI
- Testes contra banco real são mais sensíveis a condições de ambiente e podem apresentar intermitência, que precisa ser investigada como defeito, nunca contornada com nova tentativa automática. Teste intermitente tolerado é teste descartado
- O critério de cobertura pode induzir a escrever teste para subir número, o que é desperdício. Mitigação: cobertura exigida apenas no domínio, onde o teste é barato e significativo
- Manter os `.feature` sincronizados com a especificação exige disciplina de processo

**Neutras**

- Teste de mutação (Stryker.NET) não entra no escopo atual, por custo de execução. Fica registrado como evolução natural para medir a força das asserções, que é a lacuna que a métrica de cobertura não cobre
- Teste de carga não integra o pipeline de commit, executando em ciclo próprio ([ENF](../specs/ENF-especificacao-nao-funcional.md) §10)

## Análise das opções rejeitadas

**Unidade com simulação, exclusivamente.** Rejeitada pelo critério 1. Não detectaria nenhuma das quatro invariantes críticas listadas no contexto. Produziria cobertura alta e confiança indevida.

**Banco em memória (SQLite ou provedor em memória do EF Core).** Rejeitada pelo critério 2, de forma decisiva. Não reproduz isolamento transacional, bloqueio de linha, `SKIP LOCKED`, privilégio por objeto nem o comportamento de constraint sob concorrência. É mais rápido e é inútil para este sistema.

**Ponta a ponta contra ambiente compartilhado.** Rejeitada pelo critério 3: estado compartilhado entre execuções produz intermitência, exige limpeza coordenada e impede execução paralela. *Permanece adequado* para verificação de fumaça após publicação, não para o pipeline de commit.

## Validação

- Pipeline executando todas as camadas a cada commit
- Verificação deliberada, uma vez durante o desenvolvimento, de que o teste de concorrência reprova a implementação sem bloqueio. **Feita em 2026-10-02:** reprova, mas por perda de disponibilidade, não por violação de saldo; a constraint de sequência preserva a correção mesmo sem bloqueio. Resultado completo no [ADR-0005](./ADR-0005-controle-de-concorrencia.md), "Validação empírica do bloqueio"
- `docker compose up` em container limpo, validando RNF-037
- Relatório de cobertura publicado no pipeline

## Gatilho de revisão

1. Tempo da suíte de integração comprometendo o ciclo de feedback, que levaria a dividir a execução por etapa
2. Intermitência recorrente em testes de concorrência, indicando defeito real no controle de concorrência ou no próprio teste
3. Maturidade suficiente para introduzir teste de mutação como critério adicional
