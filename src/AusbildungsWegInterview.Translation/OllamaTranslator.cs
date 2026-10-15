using AusbildungsWegInterview.Core.Common;
using AusbildungsWegInterview.Core.Translation;
using AusbildungsWegInterview.Infrastructure.Ollama;

namespace AusbildungsWegInterview.Translation;

/// <summary>
/// Local translation using the language model via Ollama (the "local translation
/// capability of the language model"). When Ollama is unavailable, translation
/// degrades gracefully (empty target) so the app keeps working without a crash.
/// A dedicated local translation model can be added later by implementing
/// <see cref="ITranslator"/>.
/// </summary>
public sealed class OllamaTranslator : ITranslator
{
    private readonly IOllamaClient _client;
    private readonly string _model;
    private bool _available = true;

    public string ProviderName => "Ollama (local)";

    public OllamaTranslator(IOllamaClient client, string model)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model required.", nameof(model));
        _model = model;
    }

    public async Task PrepareAsync(string? sourceLanguage, string targetLanguage, CancellationToken ct = default)
    {
        _available = await _client.IsAvailableAsync(ct).ConfigureAwait(false);
    }

    public async Task<TranslationResult> TranslateAsync(
        string text, string? sourceLanguage, string targetLanguage, CancellationToken ct = default)
    {
        var src = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage;

        if (string.IsNullOrWhiteSpace(text))
            return new TranslationResult(text ?? string.Empty, string.Empty, src, targetLanguage, true, ProviderName);

        if (!_available)
            return new TranslationResult(text, string.Empty, src, targetLanguage, false, ProviderName,
                "Ollama unavailable - original text shown (not translated)");

        var prompt =
            "Translate the following text. Output ONLY the translation, no explanations or quotes.\n" +
            $"Source language: {src}\n" +
            $"Target language: {LanguageNames.DisplayName(targetLanguage)}\n\n" +
            "Text: " + text;

        string translated;
        try
        {
            translated = await _client.GenerateAsync(_model, prompt, 0.0, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new TranslationResult(text, string.Empty, src, targetLanguage, false, ProviderName,
                "Translation failed - original text shown (not translated)");
        }

        translated = (translated ?? string.Empty).Trim();
        if (translated.Length == 0)
            return new TranslationResult(text, string.Empty, src, targetLanguage, false, ProviderName,
                "No translation produced - original text shown (not translated)");

        // Never falsely label unchanged text as translated (same language, model echo, ...).
        if (string.Equals(translated, text.Trim(), StringComparison.Ordinal))
            return new TranslationResult(text, translated, src, targetLanguage, false, ProviderName,
                "Text returned unchanged by the model (not a translation)");

        return new TranslationResult(text, translated, src, targetLanguage, false, ProviderName);
    }

    // The Ollama client is shared with the answer generator and owned by the
    // composition root (AppServices); do NOT dispose it here.
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
