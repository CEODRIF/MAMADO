using AusbildungsWegInterview.Core.Common;

namespace AusbildungsWegInterview.Core.Answers;

/// <summary>
/// A generated answer suggestion. Provisional suggestions are shown while the
/// question is still being heard; the final suggestion refines it.
/// </summary>
public sealed record AnswerSuggestion(
    string Question,
    string Answer,
    AnswerLength Length,
    string Language,
    bool IsProvisional,
    string? Model,
    double? LatencyMs)
{
    public bool HasAnswer => !string.IsNullOrWhiteSpace(Answer);
}
