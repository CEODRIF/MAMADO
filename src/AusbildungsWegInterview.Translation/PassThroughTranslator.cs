using AusbildungsWegInterview.Core.Translation;

namespace AusbildungsWegInterview.Translation;

/// <summary>
/// Fallback translator that returns the source text unchanged. Useful when the
/// source and target languages are identical, or as a deterministic stand-in.
/// </summary>
public sealed class PassThroughTranslator : ITranslator
{
    public string ProviderName { get; }

    public PassThroughTranslator(string providerName = "Pass-through")
        => ProviderName = providerName;

    public Task PrepareAsync(string? sourceLanguage, string targetLanguage, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<TranslationResult> TranslateAsync(
        string text, string? sourceLanguage, string targetLanguage, CancellationToken ct = default)
    {
        var src = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage;
        // Explicit pass-through: the text is shown unchanged and clearly labeled
        // as NOT translated.
        var result = new TranslationResult(text, text ?? string.Empty, src, targetLanguage, false,
            ProviderName, "Translation disabled - showing original (pass-through)");
        return Task.FromResult(result);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
