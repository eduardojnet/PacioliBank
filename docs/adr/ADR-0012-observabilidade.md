# ADR-0012: Log estruturado mascarado no formatador, correlação fora do adaptador HTTP e telemetria exportada por OTLP

- **Status:** Aceito; log (card 33) e rastreamento (card 33.1) implementados, métricas especificadas (card 33.2). Revisado em 2026-10-05
- **Data:** 2026-10-04
- **Decisor:** Eduardo J. G. do Carmo
- **Requisitos dirigentes:** RNF-021, RNF-030, RNF-031, RNF-032, [ADR-0002](./ADR-0002-plataforma-e-armazenamento.md), [ADR-0009](./ADR-0009-seguranca-e-privilegio-minimo.md) §5

## Contexto e problema

O [ADR-0002](./ADR-0002-plataforma-e-armazenamento.md) escolheu as ferramentas, **Serilog** e **OpenTelemetry**, mas não como usá-las. O [ADR-0009](./ADR-0009-seguranca-e-privilegio-minimo.md) §5 exige que identificadores de conta e de cliente saiam truncados no log, e CPF e token, substituídos por marcador.

Até o card 33, nada disso existia: a API usava o logger padrão do ASP.NET Core, em texto livre, sem correlação e sem mascaramento, e a ENF §11 dava RNF-031 e RNF-032 como realizadas (lacuna L-16).

Três perguntas precisam de resposta: onde mascarar, como a correlação chega ao log sem acoplar o adaptador HTTP à biblioteca de log, e por onde a telemetria sai do processo.

## Critérios de decisão

1. Nenhum identificador íntegro na saída, por **nenhum** caminho: propriedade, mensagem renderizada ou texto de exceção
2. Verificável por teste automatizado que varre a saída real
3. Toda entrada emitida dentro de uma requisição carrega a correlação dela
4. O núcleo (domínio e aplicação) continua sem pacote externo ([ADR-0001](./ADR-0001-estilo-arquitetural.md))
5. Só pacotes estáveis no caminho de produção

## Decisão

### 1. Saída de log

Serilog grava **uma linha de JSON por entrada** na saída padrão, no formato compacto com mensagem renderizada. Os níveis vêm da seção `Serilog` da configuração: com o Serilog no lugar da fábrica de log, a seção `Logging` deixa de valer, e isso foi descoberto pelo teste, não suposto. O hosting do ASP.NET Core fica em `Warning`: suas linhas de início e fim de requisição saem antes de a correlação existir. No lugar delas, uma linha de resumo por requisição (método, caminho, status, duração), já correlacionada.

### 2. Correlação

O middleware de correlação, no adaptador HTTP, resolve e devolve o identificador (EF §8.1) sem conhecer a biblioteca de log. Um segundo middleware, em `Observability`, põe o identificador no contexto de log da requisição. O adaptador HTTP não referencia o Serilog.

### 3. Mascaramento no formatador, não num enriquecedor

O formatador produz a linha, e só então mascara **todo campo de texto**, inclusive os aninhados:

- **Todo GUID** vira `****` seguido dos quatro últimos caracteres. Dentro de um caminho de requisição não há como saber qual GUID é conta e qual é lançamento; trunca-se todos
- **CPF** vira `[CPF]`; **token** (`Bearer …` e JWT) vira `[TOKEN]`
- **Ficam íntegros** só os campos que não identificam pessoa e que ligam o log ao resto: `CorrelationId`, `MessageId` (mensagem da outbox), `TraceId`, `SpanId`, `ParentId`, e os campos de controle `@t`, `@l`, `@i`. O mesmo `MessageId` sai truncado dentro da mensagem renderizada (`@m`), que é mascarada como texto livre; a propriedade preserva o valor

### 4. Telemetria (cards 33.1 e 33.2)

- **Instrumentação com as APIs do próprio .NET** (`ActivitySource`, `Meter`), que não são pacote: o núcleo emite spans e medições sem referenciar OpenTelemetry
- **SDK do OpenTelemetry só na raiz de composição** da API, com exportação **OTLP** configurada pelas variáveis padrão (`OTEL_EXPORTER_OTLP_ENDPOINT`). Sem destino configurado, nada é exportado e nada quebra
- **Inspeção local** por um coletor ou painel OTLP no `docker compose`, escolhido e verificado no card 33.1 (ver a revisão no fim)

### 5. Ajuste no ambiente local

As connection strings locais desligam a negociação GSS do Npgsql (`GSS Encryption Mode=Disable`). Sem isso, o driver tenta carregar `libgssapi_krb5`, ausente na imagem, e o sistema escreve um aviso em texto livre no meio do log JSON. O aviso já existia antes deste ADR; só ficou visível quando o resto passou a ser estruturado.

## Consequências

**Positivas**

- Identificador de conta não sai íntegro nem pelo caminho da requisição, nem pela mensagem, nem pela exceção: um único ponto de controle, coberto por teste que varre a saída real
- O log de uma requisição é recuperável inteiro pela correlação que o chamador recebeu no cabeçalho
- O núcleo continua sem pacote externo; trocar de backend de telemetria é configuração

**Negativas**

- Mascarar todo GUID também trunca identificadores que não são pessoais, como o de lançamento. Diagnóstico de um lançamento específico passa pela correlação, não pelo identificador no log
- Cada linha é produzida, analisada e reescrita: custo de CPU por entrada, aceitável no volume de log desta API [NVI] sem medição de carga
- Identificador de conta em formato que não seja GUID (um número de agência e conta, por exemplo) não seria reconhecido. Hoje não existe
- O migrador escreve o texto próprio do DbUp, não JSON. É um processo que roda uma vez e termina; fica fora da RNF-031, declarado

**Neutras**

- O ADR-0009 §5 previa um "enriquecedor de destruição"; a intenção (nada íntegro) se mantém, o mecanismo muda pelo motivo do critério 1

## Análise das opções rejeitadas

**Enriquecedor de propriedades, como o ADR-0009 sugeria.** Rejeitado pelo critério 1: o enriquecedor só altera propriedades. A mensagem renderizada e o texto de exceção vão para a saída por outros caminhos, e o teste do formatador mostra o vazamento pela exceção.

**Pacote de terceiros de mascaramento para Serilog.** Rejeitado: os disponíveis funcionam como enriquecedor e herdam a mesma limitação, com uma dependência a mais.

**Mascarar só as propriedades com nome de conta ou cliente.** Rejeitado pelo critério 1: o caminho `/api/v1/accounts/{id}/credits` carrega a conta num campo de texto qualquer. O teste de caminho completo reprova essa implementação.

**Deixar o log íntegro e proteger o acesso a ele.** Rejeitado pelo princípio do [ADR-0009](./ADR-0009-seguranca-e-privilegio-minimo.md): controle que depende de ninguém copiar o log para onde não devia é disciplina, não controle.

**Logs pelo OpenTelemetry em vez do Serilog.** Contraria o ADR-0002 sem ganho agora: a exportação OTLP de traços e métricas não depende de onde o log é formatado. *Voltaria a ser considerado* com um backend central que receba as três fontes por OTLP.

**Exportador Prometheus para as métricas.** Rejeitado pelo critério 5: em 2026-10-04, só existe em versão beta (`1.19.1-beta.1`). *Voltaria a ser considerado* quando sair versão estável.

## Validação

- `LogMaskingTests` (8): conta truncada na propriedade e dentro de texto; correlação e mensagem da outbox íntegras; CPF com e sem pontuação; `Bearer` e JWT; texto de exceção mascarado; uma linha de JSON por entrada
- `LogPipelineTests`: API e PostgreSQL reais, quatro requisições (crédito, débito recusado, posição, extrato) e a publicação do despachante. Reprova se a conta aparecer íntegra em qualquer formato, se alguma entrada não for JSON, se alguma entrada da requisição vier sem a correlação devolvida no cabeçalho, ou se faltar a linha de resumo de alguma requisição
- Poder de detecção, três mutações, todas reprovadas: sem o middleware de correlação no log; sem mascarar GUID; mensagem renderizada preservada
- No `docker compose`, depois da coleção do Insomnia (43 testes): 100% das linhas da API em JSON e nenhum GUID íntegro fora dos campos preservados

## Gatilho de revisão

1. Identificador de pessoa em formato que não seja GUID, CPF ou token
2. Backend central de observabilidade que receba log, traço e métrica por OTLP
3. Exportador Prometheus estável
4. Custo de formatação aparecendo em medição de carga

## Revisão de 2026-10-05 (card 33.1): rastreamento implementado

### O que mudou em relação ao texto acima

**O span do caso de uso sai de um decorador, e não do núcleo.** O §4 dizia que o núcleo emitiria spans com `ActivitySource`. Na execução, isso teria um custo medido em outro controle: código de instrumentação dentro de `LedgerService` não é observado pelos testes de domínio, e os mutantes dele sobreviveriam, puxando a pontuação de mutação para perto do limite de 85% ([ADR-0010](./ADR-0010-estrategia-de-testes.md), revisão do card 36). O decorador `ObservedLedgerService`, na API, envolve `ILedgerService`, vê comando, resultado e exceção, e serve também às métricas do card 33.2. O núcleo continua sem nenhuma linha de telemetria.

### Decisões

- **Três níveis no mesmo traço:** requisição HTTP (instrumentação do ASP.NET Core), caso de uso (`ledger.post`, `ledger.reverse`, `ledger.balance`, `ledger.statement`) e comandos do PostgreSQL (`Npgsql.OpenTelemetry`)
- **Nenhum identificador nos spans.** O caminho da requisição (`url.path`) é mascarado pelo mesmo critério do log; o nome do span usa a rota com o parâmetro (`{accountId:guid}`). Rejeição registrada pelo tipo da exceção (`ledger.rejection`), nunca pela mensagem
- **Só comando dentro de um traço é rastreado.** O despachante de outbox consulta o banco a cada segundo, fora de qualquer requisição; sem o filtro do Npgsql, cada consulta viraria um traço de um span só. Abertura física de conexão também não é rastreada
- **Painel Aspire 13.6.0** no `docker compose` (`pacioli-observability`, `http://localhost:18888`), recebendo OTLP/gRPC. Autenticação desligada, só no ambiente local. A API exporta para ele pela variável `OTEL_EXPORTER_OTLP_ENDPOINT`; sem a variável, nada é exportado

### Alternativas rejeitadas nesta revisão

**Instrumentação dentro de `LedgerService`, como o §4 previa.** Rejeitada pelo custo medido no teste de mutação, descrito acima.

**Jaeger para os traços, e Prometheus com Grafana para as métricas.** Três contêineres, contra um que recebe as duas coisas por OTLP. O Prometheus ainda exigiria o exportador em beta. *Voltaria a ser considerado* em ambiente que já tenha essa pilha.

**Rastrear tudo e filtrar no coletor.** Rejeitada: o custo de criar e exportar os spans do despachante continuaria no processo, só para serem descartados depois.

### Validação feita

- `TracingTests` (2), API e PostgreSQL reais, exportador em memória: uma escrita produz o span da requisição, o do caso de uso como filho dele e os do PostgreSQL como filhos do caso de uso, no mesmo traço; nenhum span do PostgreSQL sem pai; nenhum identificador de conta íntegro em nome ou tag de span; a rejeição marca o span com status de erro e o tipo da exceção
- Poder de detecção, quatro mutações, todas reprovadas: sem o decorador; sem a instrumentação do PostgreSQL; caminho sem mascarar; sem o filtro de comando
- No `docker compose`, o painel mostrou só traços com raiz em requisição: a escrita com 10 spans, a consulta de posição com 4. Antes do filtro, mostrava dezenas de traços soltos do despachante

