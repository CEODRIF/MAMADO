using AusbildungsWegInterview.Core.Translation;

namespace AusbildungsWegInterview.Tests.Fakes;

/// <summary>Deterministic translator (no Ollama) that records what it translated.</summary>
public sealed class MockTranslator : ITranslator
{
    public string ProviderName => "Mock";
    public List<string> Translated { get; } = new();

    public Task PrepareAsync(string? sourceLanguage, string targetLanguage, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<TranslationResult> TranslateAsync(string text, string? sourceLanguage, string targetLanguage, CancellationToken ct = default)
    {
        lock (Translated) Translated.Add(text);
        return Task.FromResult(new TranslationResult(text, "[tr]" + text, sourceLanguage ?? "auto", targetLanguage, false, ProviderName));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
