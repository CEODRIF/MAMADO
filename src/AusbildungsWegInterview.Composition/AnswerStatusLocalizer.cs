using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Localization;

namespace AusbildungsWegInterview.App;

/// <summary>
/// Maps an <see cref="AnswerSuggestion"/> to the user-facing, LOCALIZED answer
/// line (all four UI languages). A real suggestion (<see cref="AnswerStatus.None"/>)
/// yields an empty string — the panel shows the model text itself. The raw
/// English message on the suggestion is for logs/tests only, never for the UI.
/// </summary>
public static class AnswerStatusLocalizer
{
    public static string Localize(LocalizationManager loc, AnswerSuggestion suggestion)
        => suggestion is null ? string.Empty : Localize(loc, suggestion.Status, suggestion.Model);

    /// <summary>
    /// Localizes a bare status code (used by the answer language panels, which
    /// carry the machine code without a whole suggestion).
    /// </summary>
    public static string Localize(LocalizationManager loc, AnswerStatus status, string? model = null)
    {
        if (status is AnswerStatus.None)
            return string.Empty;

        return status switch
        {
            AnswerStatus.RuntimeUnavailable => loc["AnswerStatusUnavailable"],
            AnswerStatus.ModelMissing => loc.F("AnswerStatusModelMissing", model ?? "…"),
            AnswerStatus.GenerationFailed => loc["AnswerStatusFailed"],
            AnswerStatus.GenerationTimeout => loc["AnswerStatusTimeout"],
            AnswerStatus.EmptyResponse => loc["AnswerStatusEmpty"],
            _ => string.Empty
        };
    }
}
