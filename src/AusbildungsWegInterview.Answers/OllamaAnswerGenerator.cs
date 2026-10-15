using System.Diagnostics;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Infrastructure.Ollama;

namespace AusbildungsWegInterview.Answers;

/// <summary>
/// Generates grounded answer suggestions using a local Ollama model.
/// Ollama is OPTIONAL: when the server is unavailable, the generator returns a
/// clear, non-fabricated "unavailable" suggestion and the rest of the app keeps
/// working (transcription + translation are unaffected).
/// </summary>
public sealed class OllamaAnswerGenerator : IAnswerGenerator
{
    private readonly IOllamaClient _client;
    private readonly string _model;
    private bool _availabilityChecked;

    public string RuntimeName => "Ollama (local)";
    public string? ModelName => _model;
    public bool IsAvailable { get; private set; }

    public OllamaAnswerGenerator(IOllamaClient client, string model)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model required.", nameof(model));
        _model = model;
    }

    public async Task RefreshAvailabilityAsync(CancellationToken ct = default)
    {
        IsAvailable = await _client.IsAvailableAsync(ct).ConfigureAwait(false);
        _availabilityChecked = true;
    }

    public async Task<AnswerSuggestion> GenerateAsync(AnswerRequest request, CancellationToken ct = default)
    {
        if (!_availabilityChecked)
            await RefreshAvailabilityAsync(ct).ConfigureAwait(false);

        if (!IsAvailable)
            return Unavailable(request);

        var sw = Stopwatch.StartNew();
        var prompt = ContextManager.BuildPrompt(request);
        string answer;
        try
        {
            answer = await _client.GenerateAsync(_model, prompt, 0.3, ct).ConfigureAwait(false);
        }
        catch
        {
            return Unavailable(request);
        }
        sw.Stop();

        answer = (answer ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(answer))
            return Unavailable(request);

        return new AnswerSuggestion(
            request.QuestionOriginal,
            answer,
            request.Length,
            request.AnswerLanguage,
            IsProvisional: false,
            Model: _model,
            LatencyMs: sw.Elapsed.TotalMilliseconds);
    }

    private static AnswerSuggestion Unavailable(AnswerRequest request) => new(
        request.QuestionOriginal,
        "The local AI (Ollama) isn't running, so I can't draft a suggested answer right now. " +
        "You can still see the live transcription and translation. Start Ollama to enable answer suggestions.",
        request.Length,
        request.AnswerLanguage,
        IsProvisional: false,
        Model: null,
        LatencyMs: null);

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
