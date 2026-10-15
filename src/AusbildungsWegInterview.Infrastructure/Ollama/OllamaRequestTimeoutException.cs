namespace AusbildungsWegInterview.Infrastructure.Ollama;

/// <summary>
/// Thrown by <see cref="OllamaClient.GenerateAsync"/> when the REQUEST TIMEOUT
/// fires while the caller's token was NOT cancelled. This distinguishes "the
/// model was too slow" (honest, actionable status) from "Stop superseded the
/// generation" (silent cancellation). Inherited from <see cref="TimeoutException"/>
/// for semantic compatibility.
/// </summary>
public sealed class OllamaRequestTimeoutException : TimeoutException
{
    public OllamaRequestTimeoutException(string message)
        : base(message)
    {
    }
}
