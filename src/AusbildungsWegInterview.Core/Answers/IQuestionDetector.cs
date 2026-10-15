namespace AusbildungsWegInterview.Core.Answers;

/// <summary>
/// Decides whether a transcript contains enough context to answer. Implementations
/// may be heuristic or model-based; the pipeline only depends on this contract.
/// </summary>
public interface IQuestionDetector
{
    /// <summary>
    /// True when the transcript is a usable question. <paramref name="isFinal"/>
    /// indicates the segment is finalized (less strict); otherwise a provisional
    /// "good enough" question is allowed.
    /// </summary>
    bool IsSufficient(string transcript, int minChars, bool isFinal);
}
