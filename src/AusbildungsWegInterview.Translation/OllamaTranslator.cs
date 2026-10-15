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
            return new TranslationResult(text, string.Empty, src, targetLanguage, true, ProviderName);

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
        catch
        {
            return new TranslationResult(text, string.Empty, src, targetLanguage, true, ProviderName);
        }

        translated = (translated ?? string.Empty).Trim();
        return new TranslationResult(text, translated, src, targetLanguage, false, ProviderName);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
