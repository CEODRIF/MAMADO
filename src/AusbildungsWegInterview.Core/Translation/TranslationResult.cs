namespace AusbildungsWegInterview.Core.Translation;

/// <summary>
/// A translation of a transcript segment into the user-selected target language.
/// Provisional translations mirror the provisional state of the source transcript.
/// </summary>
public sealed record TranslationResult(
    string SourceText,
    string TargetText,
    string SourceLanguage,
    string TargetLanguage,
    bool IsProvisional,
    string? Provider,
    string? StatusMessage = null,
    TranslationStatus? Status = null)
{
    public bool HasText => !string.IsNullOrWhiteSpace(TargetText);

    /// <summary>
    /// Honest label flag: true ONLY when the provider actually produced a
    /// translation. Unchanged text, pass-through mode, and failures are never
    /// labeled as translated.
    /// </summary>
    public bool IsTranslated => HasText && string.IsNullOrEmpty(StatusMessage);

    /// <summary>
    /// Why the target text may be identical to / empty versus the source
    /// (e.g. "Ollama unavailable — showing original", "Translation disabled
    /// (pass-through)"). Null when a real translation was produced.
    /// </summary>
    public string? StatusText => StatusMessage;
}
