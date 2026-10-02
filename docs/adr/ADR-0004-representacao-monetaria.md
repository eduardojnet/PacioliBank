# ADR-0004: Representar valor monetário como `decimal` e `numeric(19,4)`, encapsulado no Value Object `Money`

- **Status:** Aceito
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
