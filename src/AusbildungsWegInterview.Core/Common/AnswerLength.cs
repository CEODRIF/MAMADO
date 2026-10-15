namespace AusbildungsWegInterview.Core.Common;

/// <summary>
/// Requested verbosity of a generated answer suggestion.
/// </summary>
public enum AnswerLength
{
    /// <summary>A short, 1-3 sentence suggestion. Fastest to produce.</summary>
    Concise = 0,

    /// <summary>A balanced 3-5 sentence suggestion. Default.</summary>
    Normal = 1,

    /// <summary>A longer, structured answer with a clear argument.</summary>
    Detailed = 2
}
