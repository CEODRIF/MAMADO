namespace AusbildungsWegInterview.Infrastructure.Ollama;

/// <summary>
/// Minimal client for a local Ollama server (http://127.0.0.1:11434 by default).
/// Ollama is an OPTIONAL local runtime: the app must remain usable for audio
/// capture and transcription when it is not running. No API key is involved.
/// </summary>
public interface IOllamaClient : IAsyncDisposable
{
    /// <summary>Base URL of the local Ollama endpoint.</summary>
    string BaseUrl { get; }

    /// <summary>True if the local Ollama server is reachable. Never throws.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Lists model names available on the server. Empty if unreachable.</summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);

    /// <summary>Runs a one-shot text generation and returns the produced text.</summary>
    Task<string> GenerateAsync(string model, string prompt, double? temperature = null, CancellationToken ct = default);
}
