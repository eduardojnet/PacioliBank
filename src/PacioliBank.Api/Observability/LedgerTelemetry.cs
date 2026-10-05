using System.Diagnostics;

namespace PacioliBank.Api.Observability;

/// <summary>Nomes e fontes de telemetria do ledger (ADR-0012).</summary>
public static class LedgerTelemetry
{
    /// <summary>Fonte dos spans de caso de uso e nome do medidor de negocio.</summary>
    public const string Name = "PacioliBank.Ledger";

    internal static readonly ActivitySource Source = new(Name);
}
