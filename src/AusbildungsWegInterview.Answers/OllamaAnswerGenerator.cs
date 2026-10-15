using System.Diagnostics;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Infrastructure.Ollama;

namespace AusbildungsWegInterview.Answers;

/// <summary>
/// Generates grounded answer suggestions using a local Ollama model.
///
/// Graceful degradation (each case returns an honest <see cref="AnswerStatus"/>
/// code plus a raw English message that is LOG-ONLY — the UI renders the code
/// through AnswerStatusLocalizer in all four languages, and the rest of the
/// app keeps working):
///  * Ollama not running  -> RuntimeUnavailable ("start Ollama").
///  * Model not pulled     -> ModelMissing ("ollama pull &lt;model&gt;").
///  * Request timeout      -> GenerationTimeout (distinct from Stop/supersede,
///                            which stay silent cancellations).
///  * Error / other failure -> GenerationFailed.
///  * Empty / malformed     -> EmptyResponse.
///
/// Availability is RE-PROBED while the session runs: once the cached probe is
/// older than the recheck interval (default 20 s), the next generation checks
/// Ollama and the model again — so starting Ollama or pulling the model mid-
/// session is picked up without a restart, and a stopped Ollama yields an
/// honest status instead of a raw HTTP error.
///
/// Answers are grounded in the supplied CV / job description (see ContextManager)
/// and never invent facts.
/// </summary>
public sealed class OllamaAnswerGenerator : IAnswerGenerator
{
    private readonly IOllamaClient _client;
    private readonly string _model;
    private readonly long _availabilityRecheckMs;
    private bool _availabilityChecked;
    private bool? _modelAvailable;
    private long _lastProbeTicks; // Environment.TickCount64 of the last availability probe

    public string RuntimeName => "Ollama (local)";
    public string? ModelName => _model;
    public bool IsAvailable { get; private set; }

    public OllamaAnswerGenerator(IOllamaClient client, string model, long availabilityRecheckMs = 20_000)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model required.", nameof(model));
        _model = model;
        _availabilityRecheckMs = Math.Max(0, availabilityRecheckMs);
    }

    /// <summary>
    /// Re-checks Ollama (and resets the cached model state) WITHOUT touching
    /// the staleness window. Used by the pipeline/VM on explicit refresh.
    /// </summary>
    public async Task RefreshAvailabilityAsync(CancellationToken ct = default)
    {
        IsAvailable = await _client.IsAvailableAsync(ct).ConfigureAwait(false);
        _availabilityChecked = true;
        _lastProbeTicks = Environment.TickCount64;
        _modelAvailable = null; // re-evaluate when availability changes
    }

    private bool ProbeStale =>
        !_availabilityChecked ||
        Environment.TickCount64 - _lastProbeTicks >= _availabilityRecheckMs;

    public async Task<AnswerSuggestion> GenerateAsync(AnswerRequest request, CancellationToken ct = default)
    {
        if (ProbeStale)
            await RefreshAvailabilityAsync(ct).ConfigureAwait(false);

        if (!IsAvailable)
            return StatusMessage(request,
                AnswerStatus.RuntimeUnavailable,
                "The local AI (Ollama) isn't running, so I can't draft a suggested answer right now. " +
                "You can still see the live transcription and translation. Start Ollama to enable answer suggestions.");

        if (!_modelAvailable.HasValue)
            _modelAvailable = await _client.ModelAvailableAsync(_model, ct).ConfigureAwait(false);
        if (!_modelAvailable.Value)
            return StatusMessage(request,
                AnswerStatus.ModelMissing,
                $"The Ollama model '{_model}' isn't pulled on this machine. Run `ollama pull {_model}` " +
                "(or choose a smaller model in settings), then try again.",
                model: _model);

        var sw = Stopwatch.StartNew();
        string answer;
        try
        {
            answer = await _client.GenerateAsync(_model, ContextManager.BuildPrompt(request), 0.3, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // superseded or stopped — deliberately silent (no stale UI update)
        }
        catch (OllamaRequestTimeoutException ex)
        {
            // The request timed out while the session was still running: an
            // honest, localized "timed out" status (distinct from a stop).
            sw.Stop();
            return StatusMessage(request, AnswerStatus.GenerationTimeout, ex.Message);
        }
        catch
        {
            sw.Stop();
            return StatusMessage(request,
                AnswerStatus.GenerationFailed,
                "Answer generation failed (Ollama error or timeout). Transcription and translation still work.");
        }
        sw.Stop();

        answer = CleanAnswer(answer);
        if (string.IsNullOrWhiteSpace(answer))
            return StatusMessage(request, AnswerStatus.EmptyResponse,
                "Ollama returned an empty answer. Try again or select a different model.");

        return new AnswerSuggestion(
            request.QuestionOriginal, answer, request.Length, request.AnswerLanguage,
            IsProvisional: false, Model: _model, LatencyMs: sw.Elapsed.TotalMilliseconds,
            Status: AnswerStatus.None);
    }

    /// <summary>Raw English message (log/test only) + machine status code for the localized UI.</summary>
    private static AnswerSuggestion StatusMessage(AnswerRequest r, AnswerStatus status, string message, string? model = null)
        => new(r.QuestionOriginal, message, r.Length, r.AnswerLanguage,
            IsProvisional: false, Model: model, LatencyMs: null, Status: status);

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
