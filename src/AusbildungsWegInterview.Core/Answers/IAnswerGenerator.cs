namespace AusbildungsWegInterview.Core.Answers;

/// <summary>
/// Produces contextual answer suggestions. The default local runtime (Ollama) is
/// OPTIONAL: when it is unavailable, <see cref="IsAvailable"/> is false and the
/// pipeline continues to transcribe and translate. No API key is ever required.
/// </summary>
public interface IAnswerGenerator : IAsyncDisposable
{
    /// <summary>Whether the underlying runtime is currently available.</summary>
    bool IsAvailable { get; }

    /// <summary>Runtime label shown in the UI (e.g. "Ollama", "Unavailable").</summary>
    string RuntimeName { get; }

    /// <summary>Local model in use, or null when unavailable.</summary>
    string? ModelName { get; }

    Task<AnswerSuggestion> GenerateAsync(AnswerRequest request, CancellationToken ct = default);
}
