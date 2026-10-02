using System.Globalization;
using PacioliBank.Ledger.Domain;

namespace PacioliBank.Domain.Tests;

/// <summary>
/// Cobre RN-002 e RN-007. Camada de unidade da estrategia do ADR-0010: sem I/O.
/// </summary>
public class MoneyTests
{
    [Theory]
    [InlineData("0.00")]
    [InlineData("0.01")]
    [InlineData("150.00")]
    [InlineData("99999999.99")]
    public void Of_aceita_valor_dentro_da_escala_da_moeda(string raw)
    {
        var money = Money.Parse(raw, Currency.Brl);

        Assert.Equal(raw, money.ToContractString());
        Assert.Equal(Currency.Brl, money.Currency);
    }

    [Theory]
    [InlineData("10.001")]
    [InlineData("0.005")]
    [InlineData("-3.14159")]
    public void Of_recusa_valor_com_escala_acima_da_moeda(string raw)
    {
        // decimal nao e constante valida em atributo, por isso o valor chega como texto.
        var amount = decimal.Parse(raw, CultureInfo.InvariantCulture);

        var erro = Assert.Throws<InvalidMoneyScaleException>(() => Money.Of(amount, Currency.Brl));

        Assert.Equal("BRL", erro.CurrencyCode);
    }

    [Fact]
    public void Soma_preserva_exatidao_decimal()
    {
        var a = Money.Of(0.1m, Currency.Brl);
        var b = Money.Of(0.2m, Currency.Brl);

        Assert.Equal(0.3m, Money.Add(a, b).Amount);
        Assert.Equal("0.30", (a + b).ToContractString());
    }

    [Fact]
    public void Soma_de_muitas_parcelas_nao_acumula_erro()
    {
        var total = Money.Zero(Currency.Brl);
        var parcela = Money.Of(0.01m, Currency.Brl);

        for (var i = 0; i < 10_000; i++)
        {
            total += parcela;
        }

        Assert.Equal(100.00m, total.Amount);
    }

    [Fact]
    public void Subtracao_produz_resultado_negativo_sem_erro_de_dominio()
    {
        // Money nao impede valor negativo. A invariante de posicao nao negativa
        // pertence ao agregado Conta (RN-001), nao ao valor monetario.
        var resultado = Money.Of(10.00m, Currency.Brl) - Money.Of(25.00m, Currency.Brl);

        Assert.True(resultado.IsNegative);
        Assert.Equal(-15.00m, resultado.Amount);
    }

    [Fact]
    public void Operacao_entre_moedas_distintas_e_impossivel()
    {
        var brl = Money.Of(10.00m, Currency.Brl);
        var indefinida = default(Money);

        Assert.Throws<CurrencyMismatchException>(() => Money.Add(brl, indefinida));
    }

    [Fact]
    public void Moeda_fora_do_catalogo_e_recusada()
    {
        var erro = Assert.Throws<UnsupportedCurrencyException>(() => Currency.FromCode("USD"));

        Assert.Equal("USD", erro.Code);
    }

    [Theory]
    [InlineData("brl")]
    [InlineData("BRL")]
    [InlineData(" brl ")]
    public void Codigo_de_moeda_e_resolvido_sem_depender_de_caixa_ou_espaco(string code)
    {
        Assert.Equal(Currency.Brl, Currency.FromCode(code));
    }

    [Fact]
    public void Representacao_de_contrato_usa_cultura_invariante()
    {
        // EF secao 8.2: o separador decimal nao pode depender da cultura do servidor.
        var money = Money.Of(1234.50m, Currency.Brl);

        Assert.Equal("1234.50", money.ToContractString());
    }

    [Fact]
    public void Valores_iguais_na_mesma_moeda_sao_equivalentes()
    {
        Assert.Equal(Money.Of(10.00m, Currency.Brl), Money.Of(10.00m, Currency.Brl));
        Assert.Equal(0, Money.Compare(Money.Of(10.00m, Currency.Brl), Money.Of(10.00m, Currency.Brl)));
    }

    [Fact]
    public void Comparacao_ordena_valores_da_mesma_moeda()
    {
        var menor = Money.Of(10.00m, Currency.Brl);
        var maior = Money.Of(10.01m, Currency.Brl);

        Assert.True(Money.Compare(menor, maior) < 0);
        Assert.True(Money.Compare(maior, menor) > 0);
    }
}
