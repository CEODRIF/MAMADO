namespace AusbildungsWegInterview.Core.Answers;

/// <summary>
/// Machine-readable outcome of an answer-generation attempt. The UI renders these
/// codes through <c>AnswerStatusLocalizer</c> in all four UI languages; the raw
/// English text on <see cref="AnswerSuggestion.Answer"/> is log/test-only and is
/// never displayed. <see cref="None"/> means a real (possibly provisional)
/// suggestion was produced.
/// </summary>
public enum AnswerStatus
{
    /// <summary>A real suggestion (provisional or final).</summary>
    None,

    /// <summary>The local AI runtime (Ollama) is not running / unreachable.</summary>
    RuntimeUnavailable,

    /// <summary>The runtime is up but the configured model is not installed.</summary>
    ModelMissing,

    /// <summary>Generation failed (HTTP error, Ollama error field, malformed JSON).</summary>
    GenerationFailed,

    /// <summary>Generation exceeded the configured request timeout.</summary>
    GenerationTimeout,

    /// <summary>The model returned an empty or unusable response.</summary>
    EmptyResponse
}
