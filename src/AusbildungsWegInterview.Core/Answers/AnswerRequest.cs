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
    string AnswerLanguage)
{
    public bool HasContext => Context is not null && !Context.IsEmpty;
}
