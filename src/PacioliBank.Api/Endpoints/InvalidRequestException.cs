namespace PacioliBank.Api.Endpoints;

/// <summary>
/// Requisicao estruturalmente invalida: campo obrigatorio ausente ou cursor
/// ilegivel. Erro de protocolo, nao de regra de negocio, por isso vive no
/// adaptador HTTP e nao no dominio.
/// </summary>
public sealed class InvalidRequestException : Exception
{
    public InvalidRequestException()
        : base("Requisicao invalida.")
    {
    }

    public InvalidRequestException(string message)
        : base(message)
    {
    }

    public InvalidRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
