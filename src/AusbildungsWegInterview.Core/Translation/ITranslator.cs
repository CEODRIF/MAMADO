namespace AusbildungsWegInterview.Core.Translation;

/// <summary>
/// Abstraction over a translation provider. The default workflow must be local
/// (no API key); paid cloud translation APIs are never a required dependency.
/// </summary>
public interface ITranslator : IAsyncDisposable
{
    /// <summary>Human-readable provider name shown in the UI.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Preloads/warms the provider for the given language pair. Must succeed
    /// (possibly as a no-op) even if the underlying model is not yet available;
    /// failures surface on <see cref="TranslateAsync"/>.
    /// </summary>
    Task PrepareAsync(string? sourceLanguage, string targetLanguage, CancellationToken ct = default);

    /// <summary>Translates <paramref name="text"/> from source to target language.</summary>
    Task<TranslationResult> TranslateAsync(
        string text,
        string? sourceLanguage,
        string targetLanguage,
        CancellationToken ct = default);
}
