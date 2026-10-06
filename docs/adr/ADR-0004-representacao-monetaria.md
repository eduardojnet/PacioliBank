# ADR-0004: Representar valor monetário como `decimal` e `numeric(19,4)`, encapsulado no Value Object `Money`

- **Status:** Aceito; revisado em 2026-10-06 (ver "Revisão: valor monetário como número JSON na API")
- **Data:** 2026-10-02
- **Decisor:** Eduardo J. G. do Carmo
- **Requisitos dirigentes:** RN-002, RN-007, EF §8.2, QA-005

## Contexto e problema

Valor monetário é o dado central do sistema. Erro de representação não produz falha visível: produz resultado quase certo, que passa em teste superficial e aparece meses depois em conciliação contábil, com milhares de registros já afetados.

São três decisões distintas, frequentemente confundidas em uma só:

1. Como o valor é representado **em memória**, no código C#
2. Como é **persistido** no banco de dados
3. Como **trafega** na API

Cada uma tem armadilha própria. A mais conhecida é o uso de ponto flutuante binário: em IEEE-754, `0.1 + 0.2` não resulta em `0.3`. A menos conhecida é que JSON não define precisão numérica, e parsers em JavaScript convertem todo número para ponto flutuante de dupla precisão. Um sistema pode estar perfeitamente correto internamente e perder precisão na borda, por causa do consumidor.

## Critérios de decisão

1. Exatidão em base decimal, sem erro de representação
2. Impossibilidade estrutural de operação aritmética entre moedas distintas (RN-007)
3. Preservação da precisão até o consumidor final, inclusive em JavaScript
4. Legibilidade do dado em consulta direta ao banco, para operação e auditoria
5. Faixa suficiente para valores institucionais, não apenas de varejo

## Opções consideradas

**Em memória:** `decimal`; `double`; `long` em centavos.
**Em banco:** `numeric(19,4)`; `numeric(19,2)`; `bigint` em centavos; `money`.
**Em API:** número JSON; string decimal.

## Decisão

| Dimensão | Escolha |
|---|---|
| Memória | `decimal`, sempre encapsulado em `Money` |
| Banco | `numeric(19,4)` com `CHECK (amount > 0)` |
| API | **string** decimal com duas casas: `"150.00"` |

> **Revisado em 2026-10-06 (card 47).** A linha da API foi substituída: o valor trafega como **número JSON** com exatamente as casas da moeda, `150.00`. A decisão original e seu argumento ficam preservados abaixo; a revisão está no fim deste documento. Memória e banco não mudaram.

### O Value Object `Money`

```csharp
public readonly record struct Money
{
    public decimal Amount { get; }
    public Currency Currency { get; }

    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Of(decimal amount, Currency currency)
    {
        if (decimal.Round(amount, currency.Scale) != amount)
            throw new InvalidMoneyScaleException(amount, currency);
        return new Money(amount, currency);
    }

    public static Money operator +(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new CurrencyMismatchException(left.Currency, right.Currency);
        return new Money(left.Amount + right.Amount, left.Currency);
    }
}
```

Três propriedades deliberadas:

- **O construtor é privado.** Não existe `Money` com escala inválida. A validação acontece na criação, não em cada uso.
- **A moeda viaja junto com o valor.** Soma entre moedas distintas é exceção, não resultado silencioso. Isto atende QA-005 sem custo: quando a operação multimoeda for confirmada, o modelo já a comporta.
- **É `readonly record struct`.** Semântica de valor, comparação por conteúdo e ausência de alocação em heap no caminho crítico.

### Por que escala 4 no banco e 2 na API

BRL expõe duas casas decimais. A persistência usa quatro para acomodar resultados intermediários exatos quando o sistema evoluir para cálculos com rateio, juros ou conversão cambial. Armazenar com escala 2 forçaria arredondamento em cada etapa intermediária, e arredondamento encadeado acumula erro.

A API expõe duas casas porque é o que o domínio BRL comporta. A conversão entre as duas escalas é explícita e validada por `Money.Of`.

### Por que string na API

JSON não especifica precisão numérica. `JSON.parse("150.10")` em JavaScript produz um `Number`, que é ponto flutuante de dupla precisão. Para `150.10` isso é inofensivo; em uma soma de milhares de valores no cliente, deixa de ser.

Transportar como string transfere ao consumidor a decisão explícita de como interpretar o valor. Essa explicitação é o objetivo: no contexto financeiro, a conversão deve ser visível no código do integrador, não implícita no parser.

É prática estabelecida em APIs financeiras de referência, inclusive em processadores de pagamento de grande escala. [NVI] A afirmação de prática de mercado é baseada em conhecimento geral do autor e não foi verificada contra documentação específica de fornecedor nesta data.

### Proibição estrutural de ponto flutuante

`Directory.Build.props` habilita `TreatWarningsAsErrors`, e o projeto de domínio inclui um analisador que reporta como erro qualquer uso de `float`, `double` ou `System.Single`/`System.Double` em assinatura pública ou em campo. A regra não depende de revisão humana.

## Consequências

**Positivas**

- Exatidão decimal preservada ponta a ponta, da borda da API ao armazenamento
- Operação entre moedas distintas é impossível por construção, atendendo RN-007 sem validação dispersa
- Escala validada na criação do objeto, não em cada ponto de uso
- Valor legível em consulta SQL direta, atendendo ao critério 4 para operação e auditoria
- Faixa de `numeric(19,4)` cobre valores institucionais com folga

**Negativas**

- `decimal` é aproximadamente 10 a 20 vezes mais lento que `double` em operação aritmética. Irrelevante neste perfil: o sistema é dominado por I/O, e a aritmética por lançamento são poucas operações. Trocar exatidão por desempenho aqui seria otimização sem problema a resolver
- String na API exige conversão explícita pelo integrador, com atrito na adoção. Mitigação: documentar no README com exemplo em cada linguagem relevante
- `numeric` no PostgreSQL ocupa mais espaço e é mais lento em agregação que `bigint`. Mitigação: snapshot limita o volume agregado por consulta ([ADR-0007](./ADR-0007-snapshot-e-projecao.md))
- Divergência de escala entre banco (4) e API (2) é uma assimetria que precisa estar documentada, sob pena de confundir quem lê o esquema pela primeira vez

**Neutras**

- Serializador JSON configurado com conversor dedicado para `Money`, centralizando o formato
- `Currency` é enumeração fechada, não string livre, impedindo moeda inválida em tempo de compilação

## Análise das opções rejeitadas

**`double` em memória.** Rejeitado sem ressalva. Ponto flutuante binário não representa exatamente valores decimais comuns. Em sistema financeiro, é defeito, não trade-off.

**`long` em centavos.** Tecnicamente correto, mais rápido e imune a erro de escala. Rejeitado pelo critério 4: `15010` em uma consulta de produção exige conversão mental em toda investigação, e cada conversão na borda é oportunidade de erro de fator 100, cujo sintoma é um valor cem vezes maior ou menor. *Voltaria a ser a melhor escolha* se a agregação de grandes volumes se tornasse gargalo medido, já que soma de inteiros é substancialmente mais rápida.

**Tipo `money` do PostgreSQL.** Rejeitado por depender da configuração regional do servidor, o que o torna não determinístico entre ambientes. É desaconselhado pela própria documentação do produto.

**`numeric(19,2)`.** Rejeitado por forçar arredondamento em cálculo intermediário, com acúmulo de erro em operações encadeadas futuras.

**Número JSON na API.** Rejeitado pelo critério 3, pela perda de precisão no consumidor JavaScript.

## Validação

- [BDD](../specs/BDD-comportamento.md) F01, cenários de valor inválido e de moeda divergente
- Teste de propriedade sobre `Money`: associatividade e comutatividade da soma, rejeição de escala inválida, rejeição de moeda divergente
- Analisador reprovando `float`/`double` no domínio, executado a cada build
- Teste de contrato verificando que todo campo monetário serializa como string com duas casas

## Gatilho de revisão

1. Confirmação de requisito multimoeda (QA-005), que exige definir conversão e taxa
2. Agregação de valores identificada como gargalo medido em teste de carga
3. Entrada de cálculo de juros, rateio ou tributo em escopo, que pode exigir escala superior a 4

## Revisão: valor monetário como número JSON na API (2026-10-06, card 47)

### Problema

A decisão original fazia o valor trafegar como texto (`"150.00"`), pelo critério 3: preservar a precisão até o consumidor JavaScript. O usuário determinou que os valores sejam numéricos no contrato. O motivo de negócio não foi declarado além do próprio requisito.

### Opções consideradas

1. **Número JSON na entrada e na saída, com exatamente as casas da moeda; texto recusado na entrada** (escolhida)
2. Manter texto na API (a decisão original)
3. Aceitar número ou texto na entrada, responder sempre número
4. Número na API e no evento, mas o evento em versão nova (`v2`)

### Decisão

**Opção 1**, por decisão do usuário, nos dois contratos externos: a API e o evento de integração.

- **Entrada:** só número JSON. Texto no lugar do número é recusado pelo desserializador, em modo estrito, com `400 INVALID_REQUEST`. Número com mais casas que a moeda (`10.001`) sai como `400 INVALID_AMOUNT`, pela mesma validação de `Money.Of`. O `decimal` do .NET lê o número JSON a partir do texto, sem passar por ponto flutuante
- **Saída:** número com exatamente as casas da moeda, produzido por `Money.ToContractAmount()`. O `decimal` guarda a escala e o serializador a escreve: 150 entra e `150.00` sai; `110,0000` lido do `numeric(19,4)` sai `110.00`. Vale para escrita, transferência, posição, extrato e para `availableBalance` e `requestedAmount` do saldo insuficiente
- **OpenAPI:** o gerador declara `decimal` como `"format": "double"`, o que levaria cliente gerado a ler o valor em ponto flutuante. Um transformador de esquema declara `"format": "decimal"` nos nove campos monetários
- **Evento:** `amount` e `balanceAfter` também passam a número, **mantendo `v1`**, pelo mesmo precedente do card 24.2: nenhum consumidor recebeu eventos
- **Dado gravado:** a migração 0005 converte os valores entre aspas de `idempotency_records.response_body` e de `outbox_messages.payload`, pelo papel de migração. A troca é textual e só nos campos monetários, preservando a ordem das chaves do `response_body`, que é `json` para ser devolvido byte a byte ([ADR-0006](./ADR-0006-idempotencia.md))
- **Impressão do comando inalterada:** a impressão gravada na idempotência continua usando o texto com casas fixas (`Money.ToContractString`). Comando enviado antes da mudança, reenviado agora como número, é reconhecido como o mesmo comando, e não como conflito de chave

### Opções rejeitadas

**Manter texto (opção 2).** Rejeitada por decisão do usuário: o requisito passou a ser contrato numérico.

**Aceitar os dois formatos na entrada (opção 3).** Tolerante com cliente antigo, mas são dois contratos de entrada a testar, documentar e manter, sem cliente antigo que o justifique. Voltaria a valer se houvesse integrador em produção usando texto.

**Evento em `v2` (opção 4).** Protegeria consumidores que não existem. Voltaria a ser obrigatória a partir do primeiro consumidor real: daí em diante, mudança de formato do evento é versão nova.

### Consequências

- **O critério 3 deixa de ser garantido pelo contrato.** Consumidor que lê o JSON com o parser padrão de JavaScript recebe `Number`, em ponto flutuante de dupla precisão. A leitura de um valor isolado é exata até 15 algarismos significativos, ou seja, abaixo de 10 trilhões com duas casas; o `numeric(19,4)` admite valores maiores, e acima disso o consumidor JavaScript perde precisão. Somas feitas no cliente em ponto flutuante acumulam erro. A responsabilidade de ler o número como decimal passa a ser do integrador, com parser que preserve o texto do número
- Consumidor .NET, Java (`BigDecimal`) ou Python (`Decimal`, com o parâmetro de leitura apropriado) lê o valor exato [NVI: verificado só para .NET, pelos testes]
- O painel de evidência, que é JavaScript, passa a enviar e receber números; os valores das demonstrações são pequenos, e as quatro demonstrações foram reexecutadas
- A escala fixa na saída é garantida pelo `decimal`, e não pelo formato do texto: quem trocar `ToContractAmount` por `Amount` faz `150` sair como `150` e o `numeric(19,4)` sair como `110.0000` (medido: 10 testes reprovam)

### Validação

- `MonetaryContractTests` (API e PostgreSQL reais): texto recusado no crédito e na transferência, sem gravar; escala acima da moeda recusada; `150` na entrada e `150.00` na saída da escrita, do reenvio, da posição, do extrato e do saldo insuficiente
- `MoneyTests`: escala da moeda no número do contrato, inclusive serializado
- `MigrationTests`: a 0005 converte resposta de crédito, de transferência e evento, preservando o resto do texto
- Teste de contrato: `amount` e `balanceAfter` como `number` no OpenAPI, instantâneo com `"format": "decimal"`
- Coleção do Insomnia: tipo `number` e escala no texto da resposta
- Poder de detecção medido: sem a normalização da escala, 10 testes reprovam; aceitando texto como número, 5 reprovam

### Gatilho de revisão desta revisão

1. Primeiro consumidor JavaScript real que agregue valores no cliente
2. Valor acima de 10 trilhões em qualquer conta, limite da leitura exata em ponto flutuante de dupla precisão
3. Primeiro consumidor real dos eventos: mudança de formato do evento passa a exigir versão nova
