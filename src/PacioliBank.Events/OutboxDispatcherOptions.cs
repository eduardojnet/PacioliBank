namespace PacioliBank.Events;

/// <summary>Parametros do despachante. Valores iniciais, calibraveis por RNF-032.</summary>
public sealed class OutboxDispatcherOptions
{
    /// <summary>Mensagens lidas por transacao.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Tentativas antes de a mensagem sair da fila para inspecao. Ao atingir o
    /// limite, ela deixa de ser lida e o despachante emite alerta, sem bloquear
    /// as demais (ADR-0008).
    /// </summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>Teto do recuo exponencial entre tentativas, em segundos.</summary>
    public int MaxBackoffSeconds { get; set; } = 300;
}
