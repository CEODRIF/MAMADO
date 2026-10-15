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

    public async Task<AnswerSuggestion> GenerateAsync(AnswerRequest request, CancellationToken ct = default)
    {
        lock (Requests) Requests.Add(request);
        if (DelayMs > 0)
            await Task.Delay(DelayMs, ct);
        return new AnswerSuggestion(request.QuestionOriginal, AnswerToReturn, request.Length, request.AnswerLanguage, false, ModelName, 1.0);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
