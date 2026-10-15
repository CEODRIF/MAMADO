using AusbildungsWegInterview.Core.Common;
using AusbildungsWegInterview.Core.Domain;

namespace AusbildungsWegInterview.Core.Answers;

/// <summary>
/// Everything a generator needs to produce a contextual answer suggestion.
/// </summary>
public sealed record AnswerRequest(
    string QuestionOriginal,
    string? QuestionTranslated,
    CandidateContext Context,
    AnswerLength Length,
    string AnswerLanguage,
    AnswerStyle Style = AnswerStyle.Natural,
    IReadOnlyList<string>? RecentContext = null,
    CefrLevel Level = CefrLevel.B1)
{
    public bool HasContext => Context is not null && !Context.IsEmpty;
    public bool HasRecentContext => RecentContext is { Count: > 0 };
}
