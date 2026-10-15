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
    double? LatencyMs,
    AnswerStatus Status = AnswerStatus.None)
{
    /// <summary>
    /// True only for a REAL model suggestion. Status outcomes (unavailable,
    /// model missing, failed, timeout, empty) carry a raw log message in
    /// <see cref="Answer"/> but are NOT answers — they must not be recorded as
    /// conversation turns or shown as copyable content.
    /// </summary>
    public bool HasAnswer => Status is AnswerStatus.None && !string.IsNullOrWhiteSpace(Answer);
}
