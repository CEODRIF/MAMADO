using AusbildungsWegInterview.Core.Speech;
using AusbildungsWegInterview.Core.Translation;

namespace AusbildungsWegInterview.Translation;

/// <summary>
/// Translation via whisper.cpp <c>-tr</c> (translate) mode: any source language
/// → **English only**, reusing the speech model — zero extra dependency.
///
/// Honest limitations (surfaced as status, never faked):
///  * Target must be English (other targets → clear "use Ollama" message).
///  * The recognizer must support <see cref="ISegmentTranslator"/> (the CLI
///    recognizer does; the native-lib recognizer does not).
///  * The pipeline invokes this only for FINAL segments (see InterviewPipeline).
/// </summary>
public sealed class WhisperTranslateAdapter : ITranslator
{
    private readonly ISegmentTranslator? _segment;

    public WhisperTranslateAdapter(ISegmentTranslator? segment) => _segment = segment;

    public string ProviderName => "whisper.cpp (translate)";

    public Task PrepareAsync(string? sourceLanguage, string targetLanguage, CancellationToken ct = default)
        => Task.CompletedTask; // the recognizer is prepared by the pipeline

    public async Task<TranslationResult> TranslateAsync(
        string text, string? sourceLanguage, string targetLanguage, CancellationToken ct = default)
    {
        var src = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage;

        if (string.IsNullOrWhiteSpace(text))
            return new TranslationResult(text ?? string.Empty, string.Empty, src, targetLanguage, false, ProviderName);

        if (!targetLanguage.Equals("en", StringComparison.OrdinalIgnoreCase))
            return new TranslationResult(text, string.Empty, src, targetLanguage, false, ProviderName,
                $"whisper translation supports English only - use Ollama for target '{targetLanguage}'");

        if (_segment is null)
            return new TranslationResult(text, string.Empty, src, "en", false, ProviderName,
                "The active speech engine does not support whisper translate mode - set the whisper CLI path or use Ollama");

        string? translated;
        try
        {
            translated = await _segment.TranslateSegmentToEnglishAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            translated = null;
        }

        if (string.IsNullOrWhiteSpace(translated))
            return new TranslationResult(text, string.Empty, src, "en", false, ProviderName,
                "No translation produced - original text shown (not translated)");

        // Source already English → whisper echoes the original: say so, don't label it translated.
        if (string.Equals(translated.Trim(), text.Trim(), StringComparison.Ordinal))
            return new TranslationResult(text, translated, src, "en", false, ProviderName,
                "Text returned unchanged (source already English - not a translation)");

        return new TranslationResult(text, translated, src, "en", false, ProviderName);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
