# ADR-0011: Entregar painel de evidência como página estática, em escopo condicional e último na fila

- **Status:** Aceito, com escopo condicional
- **Data:** 2026-10-02
- **Decisor:** Eduardo J. G. do Carmo
- **Requisitos dirigentes:** RNF-037, e demonstração visual de RN-001, RN-005, RN-009, RNF-004, RNF-006

## Contexto e problema

O enunciado do desafio não pede interface. Os requisitos obrigatórios são implementação em C#, testes automatizados, repositório público, README com instruções de execução local, documentação no repositório e build sem erros nem avisos. Interface não aparece em nenhum deles.

Ao mesmo tempo, três das decisões mais relevantes deste projeto são **invisíveis em leitura de código**:

- A invariante de posição não negativa sob concorrência ([ADR-0005](./ADR-0005-controle-de-concorrencia.md)) só se manifesta quando cinquenta requisições disputam a mesma conta
- A posição em instante passado ([ADR-0003](./ADR-0003-ledger-append-only.md)) só fica evidente quando se percorre a linha do tempo
- O replay idempotente ([ADR-0006](./ADR-0006-idempotencia.md)) só é perceptível quando se observa o saldo imóvel diante de um reenvio

Elas estão cobertas por teste automatizado, que é a prova que importa. Mas teste é lido por quem abre a suíte; a demonstração é vista por qualquer pessoa que abra o navegador.

A pergunta: existe uma forma de tornar essas decisões visíveis sem que o esforço comprometa os requisitos obrigatórios?

## Critérios de decisão

1. Não introduz nenhuma dependência de toolchain fora do ecossistema .NET
2. Não altera a promessa de subir o ambiente com um comando (RNF-037)
3. Não cria risco ao requisito de compilar sem erros nem avisos (RNF-034)
4. Custo inferior a 3 horas, cabendo integralmente na folga do cronograma
5. Demonstra decisões arquiteturais, não operações de cadastro

## Opções consideradas

1. Nenhuma interface, apenas a especificação OpenAPI e um arquivo de requisições
2. Página estática servida pela própria API
3. Blazor WebAssembly
4. React ou Angular
5. Flutter Web

## Decisão

**Página estática única servida pelo `wwwroot` da própria API**, em HTML, CSS e JavaScript sem dependências externas, implementando quatro painéis de demonstração.

### Escopo do painel

| Painel | O que demonstra | Decisão provada |
|---|---|---|
| **Disparo concorrente** | Aciona 50 débitos simultâneos em conta com saldo para 10. Exibe aceitos, rejeitados, posição final e o fato de nunca ter sido negativa | [ADR-0005](./ADR-0005-controle-de-concorrencia.md) |
| **Linha do tempo** | Controle deslizante de data recalculando a posição consolidada em qualquer instante, com os lançamentos do período destacados | [ADR-0003](./ADR-0003-ledger-append-only.md) |
| **Replay idempotente** | Reenvia o mesmo comando com a mesma chave. Exibe o cabeçalho `Idempotency-Replayed` e a posição inalterada | [ADR-0006](./ADR-0006-idempotencia.md) |
| **Origem do cálculo** | Exibe `computedFrom` e `entriesReplayed` a cada consulta, tornando visível o funcionamento do snapshot | [ADR-0007](./ADR-0007-snapshot-e-projecao.md) |

**O painel não faz cadastro.** Não há formulário de criação de conta, não há listagem administrativa, não há edição. Toda interação existe para evidenciar uma decisão arquitetural. Uma interface de cadastro seria indistinguível de qualquer outro desafio e consumiria o tempo que as quatro demonstrações usam melhor.

### Complementos de custo marginal

- Especificação OpenAPI gerada a partir do código, servida com interface de exploração
- Arquivo `requests.http` versionado, executável diretamente no VS Code e no Rider

### Posição na fila de execução

**Último item, sem exceção.** A ordem de execução é:

1. Domínio e invariantes
2. Persistência e migrações
3. API e tratamento de erro
4. Testes de domínio, integração e concorrência
5. README e documentação
6. Painel de evidência

### Critério de corte

**Se, ao final da hora 16 de execução, o teste de concorrência não estiver aprovado ou o README não estiver completo, o painel é cortado integralmente** e passa a constar como item em `docs/com-mais-tempo.md`.

O critério é fixado agora, antes de existir pressão de prazo. Critério de corte definido sob pressão é racionalização, não decisão.

## Consequências

**Positivas**

- Decisões arquiteturais tornam-se verificáveis por qualquer avaliador em um navegador, sem abrir a suíte de testes
- Custo marginal real: nenhuma dependência nova, nenhum passo adicional de build, nenhuma alteração no `docker-compose`
- A API passa a servir, na raiz, uma porta de entrada para quem abre o projeto pela primeira vez
- O critério de corte protege os requisitos obrigatórios de forma objetiva

**Negativas**

- **É escopo não solicitado.** Mesmo a custo baixo, consome tempo que poderia reforçar testes ou documentação. É o risco aceito conscientemente, mitigado pela posição na fila e pelo critério de corte
- JavaScript sem framework é mais verboso e menos organizado que uma solução com componentes. Aceitável na escala de quatro painéis; não escalaria além disso
- O painel não tem testes automatizados próprios. Declarado explicitamente no README: é material de demonstração, não componente de produção
- Código front-end no repositório pode atrair atenção de revisão que seria mais bem empregada no domínio. Mitigação: isolamento em `wwwroot` e nota no README delimitando sua natureza

**Neutras**

- O painel consome exatamente a mesma API pública que qualquer integrador consumiria, sem endpoint privilegiado
- A massa de dados da demonstração é criada pelos próprios endpoints públicos, na carga inicial do ambiente

## Análise das opções rejeitadas

**Nenhuma interface, apenas OpenAPI.** Legítima e suficiente para atender todos os requisitos obrigatórios. Rejeitada por não criar diferenciação alguma, deixando as três decisões mais relevantes visíveis apenas para quem abre a suíte de testes. *Permanece como o estado final* caso o critério de corte seja acionado.

**Blazor WebAssembly.** Mantém toda a solução em C# e demonstra competência na stack avaliada. Rejeitado pelo critério 4: adiciona um projeto, aumenta o tempo de build, carrega o runtime .NET no navegador e introduz risco de aviso de compilação vindo do template, o que tangencia o critério 3. *Voltaria a ser a melhor escolha* se houvesse folga superior a 4 horas ao final da execução, ou se o painel precisasse crescer além de demonstração.

**React ou Angular.** Rejeitados pelos critérios 1 e 2. Exigem Node no ambiente de build, o que quebra a promessa de um comando ou obriga a versionar artefato de build. Além disso, demonstram competência que o desafio não avalia.

**Flutter Web.** Rejeitado pelos critérios 1, 2 e 4, de forma cumulativa:

- Exige Dart SDK e Flutter SDK no build. As duas saídas possíveis são ruins: incluir o SDK na imagem, inflando-a em mais de 1GB e elevando o tempo de build do avaliador de segundos para minutos; ou versionar o artefato compilado no `wwwroot`, o que significa megabytes de JavaScript gerado no repositório, antipadrão visível em qualquer revisão
- O renderer carrega a ordem de 1,5MB a 2MB antes de desenhar o primeiro elemento. [NVI] O estado atual dos renderers web do Flutter mudou em versões recentes e deve ser confirmado na documentação oficial antes de qualquer compromisso
- Custo estimado de 6 a 10 horas, contra 2 a 3 da solução escolhida
- Uma pasta Dart em um repositório de desafio C# comunica dispersão tecnológica, não amplitude, em um desafio cujo enunciado declara avaliar explicitamente a capacidade de priorizar

*Voltaria a ser a melhor escolha* para um aplicativo móvel ou multiplataforma consumindo esta API, que é um projeto distinto, em repositório próprio, posterior à entrega. Nessa hipótese, as desvantagens listadas acima desaparecem e as vantagens do Flutter passam a ser as relevantes.

## Validação

- `docker compose up` seguido de acesso à raiz da aplicação entrega o painel sem passo adicional
- Build permanece sem erros e sem avisos com o `wwwroot` presente (RNF-034)
- O painel consome exclusivamente endpoints públicos documentados no OpenAPI
- O painel de disparo concorrente reproduz visualmente o resultado do cenário [BDD](../specs/BDD-comportamento.md) F07

### Estado da implementação (2026-10-02, card 25)

Implementado em `src/PacioliBank.Api/wwwroot/` (`index.html`, `painel.css`, `painel.js`), servido na raiz da API. O critério de corte não se aplicou: as duas condições estavam satisfeitas. Diferenças em relação ao texto acima, sem mudar a decisão:

- **Contas dedicadas no script de massa local.** "A massa de dados é criada pelos próprios endpoints públicos" vale para os lançamentos, não para as contas: não existe endpoint de criação de conta, porque o ciclo de vida da conta é do Cadastro (EF §3.2). Três contas (`3333…`, `4444…`, `5555…`) foram reservadas no `002_seed_local.sql`, uma por demonstração; cada demonstração prepara o próprio estado pela API antes de executar
- **`entriesReplayed` acrescentado à resposta de posição** (EF 1.4, §8.5), porque o painel de origem do cálculo o exibe e a API não o devolvia
- **A disputa do disparo concorrente é menor que a do teste F07.** O navegador limita as conexões simultâneas por servidor; o painel declara isso na própria tela e remete ao teste da suíte, que dispara os 50 em paralelo real
- **OpenAPI com interface de exploração, o "complemento de custo marginal", não foi feito.** É pré-requisito do card 19.3 (testes de contrato). Por isso o item "consome exclusivamente endpoints documentados no OpenAPI" da validação vale como "documentados na EF §8"

**Validação feita:** os quatro painéis executados em Chrome headless contra o ambiente do `docker compose`, repetidamente: 10 aceitos e 40 recusados com posição mínima 0,00; posição recalculada em três instantes passados; reenvio com `200`, `Idempotency-Replayed` e mesmo SHA-256 do corpo, e `409` com outro valor; travessia da âncora com `snapshot` e `entriesReplayed` 0. Sem erro de JavaScript no console; as únicas linhas de console são as respostas `422` e `409` esperadas.

## Gatilho de revisão

1. Acionamento do critério de corte na hora 16, que move esta decisão para `docs/com-mais-tempo.md`
2. Necessidade de o painel crescer além de demonstração, o que reabre a opção Blazor WebAssembly
3. Requisito de aplicativo móvel ou multiplataforma consumindo a API, o que reabre a opção Flutter em repositório próprio
