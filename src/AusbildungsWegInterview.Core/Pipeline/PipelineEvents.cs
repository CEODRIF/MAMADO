using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Core.Speech;
using AusbildungsWegInterview.Core.Translation;

namespace AusbildungsWegInterview.Core.Pipeline;

/// <summary>A transcription result was produced (partial or final).</summary>
public sealed class TranscriptUpdatedEventArgs(TranscriptionResult result) : EventArgs
{
    public TranscriptionResult Result { get; } = result;
}

/// <summary>The translation of the current question was updated.</summary>
public sealed class TranslationUpdatedEventArgs(TranslationResult result) : EventArgs
{
    public TranslationResult Result { get; } = result;
}

/// <summary>An answer suggestion was produced or refined.</summary>
public sealed class AnswerUpdatedEventArgs(AnswerSuggestion suggestion) : EventArgs
{
    public AnswerSuggestion Suggestion { get; } = suggestion;
}

/// <summary>Panel state of one language in the answer/translation panel sets.</summary>
public enum PanelState
{
    /// <summary>Waiting for the final text (translations run on the finalized answer).</summary>
    Pending,

    /// <summary>Real text produced by the provider.</summary>
    Ready,

    /// <summary>The provider could not translate (offline model, unsupported pair, …).</summary>
    Unavailable
}

/// <summary>
/// One language panel of the detected question (the mockup's Original /
/// Translation (English) / Translation (Arabic) trio).
/// </summary>
public sealed record QuestionLanguagePanel(
    string Language,
    PanelState State,
    string Text,
    TranslationStatus? Status = null,
    string? StatusMessage = null);

/// <summary>
/// The full detected-question set: the original text plus a panel per
/// configured language. Emitted alongside <see cref="TranslationUpdatedEventArgs"/>
/// (which stays the single primary translation used for grounding).
/// </summary>
public sealed class QuestionSetUpdatedEventArgs(
    string original,
    string? sourceLanguage,
    bool isProvisional,
    IReadOnlyList<QuestionLanguagePanel> translations) : EventArgs
{
    public string Original { get; } = original;
    public string? SourceLanguage { get; } = sourceLanguage;
    public bool IsProvisional { get; } = isProvisional;
    public IReadOnlyList<QuestionLanguagePanel> Translations { get; } = translations;
}

/// <summary>
/// One language panel of the suggested answer. <paramref name="AnswerStatus"/>
/// carries the generation outcome of the primary panel; <paramref name="Status"/>
/// carries a translation outcome of a translated panel (both are machine codes,
/// localized by the UI; raw messages are log-only).
/// </summary>
public sealed record AnswerLanguagePanel(
    string Language,
    PanelState State,
    string Text,
    bool IsPrimary = false,
    TranslationStatus? Status = null,
    string? StatusMessage = null,
    AnswerStatus? AnswerStatus = null,
    string? Model = null);

/// <summary>
/// The full answer set: the generated answer (primary language) plus the
/// locally translated panels. Status codes are localized by the UI; the raw
/// status message is log-only.
/// </summary>
public sealed class AnswersUpdatedEventArgs(
    string question,
    bool isProvisional,
    IReadOnlyList<AnswerLanguagePanel> panels) : EventArgs
{
    public string Question { get; } = question;
    public bool IsProvisional { get; } = isProvisional;
    public IReadOnlyList<AnswerLanguagePanel> Panels { get; } = panels;
}

/// <summary>Live microphone-free audio level (mean amplitude 0..1) for the waveform.</summary>
public sealed class AudioLevelUpdatedEventArgs(double level) : EventArgs
{
    public double Level { get; } = level;
}

/// <summary>The pipeline state changed.</summary>
public sealed class PipelineStateChangedEventArgs(PipelineState state) : EventArgs
{
    public PipelineState State { get; } = state;
}
