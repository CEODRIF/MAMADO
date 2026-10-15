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
    string? Provider)
{
    public bool HasText => !string.IsNullOrWhiteSpace(TargetText);
}
