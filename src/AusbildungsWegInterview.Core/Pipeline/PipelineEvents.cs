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

/// <summary>The pipeline state changed.</summary>
public sealed class PipelineStateChangedEventArgs(PipelineState state) : EventArgs
{
    public PipelineState State { get; } = state;
}
