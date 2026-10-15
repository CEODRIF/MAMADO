using System.Diagnostics;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Infrastructure.Ollama;

namespace AusbildungsWegInterview.Answers;

/// <summary>
/// Generates grounded answer suggestions using a local Ollama model.
///
/// Graceful degradation (each case returns a clear, non-fabricated message, and the
/// rest of the app keeps working):
///  * Ollama not running  -> "start Ollama" message.
///  * Model not pulled     -> "ollama pull &lt;model&gt;" message.
///  * Error / timeout       -> "generation failed" message.
///  * Empty / malformed     -> "empty answer" message.
///
/// Answers are grounded in the supplied CV / job description (see ContextManager)
/// and never invent facts.
/// </summary>
public sealed class OllamaAnswerGenerator : IAnswerGenerator
{
    private readonly IOllamaClient _client;
    private readonly string _model;
    private bool _availabilityChecked;
    private bool? _modelAvailable;

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
        _modelAvailable = null; // re-evaluate when availability changes
    }

    public async Task<AnswerSuggestion> GenerateAsync(AnswerRequest request, CancellationToken ct = default)
    {
        if (!_availabilityChecked)
            await RefreshAvailabilityAsync(ct).ConfigureAwait(false);

        if (!IsAvailable)
            return StatusMessage(request,
                "The local AI (Ollama) isn't running, so I can't draft a suggested answer right now. " +
                "You can still see the live transcription and translation. Start Ollama to enable answer suggestions.");

        if (!_modelAvailable.HasValue)
            _modelAvailable = await _client.ModelAvailableAsync(_model, ct).ConfigureAwait(false);
        if (!_modelAvailable.Value)
            return StatusMessage(request,
                $"The Ollama model '{_model}' isn't pulled on this machine. Run `ollama pull {_model}` " +
                "(or choose a smaller model in settings), then try again.");

        var sw = Stopwatch.StartNew();
        string answer;
        try
        {
            answer = await _client.GenerateAsync(_model, ContextManager.BuildPrompt(request), 0.3, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // superseded or stopped
        }
        catch
        {
            sw.Stop();
            return StatusMessage(request,
                "Answer generation failed (Ollama error or timeout). Transcription and translation still work.");
        }
        sw.Stop();

        answer = CleanAnswer(answer);
        if (string.IsNullOrWhiteSpace(answer))
            return StatusMessage(request, "Ollama returned an empty answer. Try again or select a different model.");

        return new AnswerSuggestion(
            request.QuestionOriginal, answer, request.Length, request.AnswerLanguage,
            IsProvisional: false, Model: _model, LatencyMs: sw.Elapsed.TotalMilliseconds);
    }

    private static AnswerSuggestion StatusMessage(AnswerRequest r, string message)
        => new(r.QuestionOriginal, message, r.Length, r.AnswerLanguage, IsProvisional: false, Model: null, LatencyMs: null);

    /// <summary>Strips common model artifacts (prompt echo, surrounding quotes). Never invents content.</summary>
    private static string CleanAnswer(string answer)
    {
        var s = (answer ?? string.Empty).Trim();
        if (s.StartsWith("Suggested answer:", StringComparison.OrdinalIgnoreCase))
            s = s["Suggested answer:".Length..].Trim();
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '“' && s[^1] == '”')))
            s = s[1..^1].Trim();
        return s;
    }

    // The Ollama client is shared with the translator and owned by the
    // composition root (AppServices); do NOT dispose it here.
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
