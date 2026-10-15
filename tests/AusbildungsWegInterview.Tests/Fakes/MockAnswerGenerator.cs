using AusbildungsWegInterview.Core.Answers;

namespace AusbildungsWegInterview.Tests.Fakes;

/// <summary>Deterministic answer generator that records requests.</summary>
public sealed class MockAnswerGenerator : IAnswerGenerator
{
    public bool IsAvailable => true;
    public string RuntimeName => "Mock";
    public string? ModelName => "mock-model";
    public string AnswerToReturn { get; set; } = "MOCK_ANSWER";
    public int DelayMs { get; set; }
    public List<AnswerRequest> Requests { get; } = new();
    /// <summary>Token passed to each generation (used to verify cancellation of obsolete work).</summary>
    public List<CancellationToken> Tokens { get; } = new();

    public async Task<AnswerSuggestion> GenerateAsync(AnswerRequest request, CancellationToken ct = default)
    {
        lock (Requests)
        {
            Requests.Add(request);
            Tokens.Add(ct);
        }
        if (DelayMs > 0)
            await Task.Delay(DelayMs, ct);
        ct.ThrowIfCancellationRequested();
        return new AnswerSuggestion(request.QuestionOriginal, AnswerToReturn, request.Length, request.AnswerLanguage, false, ModelName, 1.0);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
