using AusbildungsWegInterview.Core.Audio;

namespace AusbildungsWegInterview.Core.Speech;

/// <summary>
/// Stateful real-time speech recognizer. The pipeline feeds it normalized audio
/// chunks; it returns an accumulated result for the current segment.
///
/// Segmentation model:
///  * <see cref="ProcessAudioAsync"/> — appends a chunk and returns the current
///    best result for the segment (Partial while growing, Final when the
///    recognizer believes the segment is complete).
///  * <see cref="FinalizeAsync"/> — forces a final result for the current segment
///    (used on end-of-speech / sustained silence).
///  * <see cref="Reset"/> — clears the segment so the next audio starts fresh.
///
/// Implementations are pluggable (e.g. whisper.cpp). No API key is required for
/// the default local workflow. Implementations must be honest about whether they
/// stream true partials or transcribe discrete chunks.
/// </summary>
public interface ISpeechRecognizer : IAsyncDisposable
{
    /// <summary>True once the runtime/model is loaded and ready to transcribe.</summary>
    bool IsReady { get; }

    /// <summary>Identifier of the loaded model (e.g. "base", "small").</summary>
    string ModelName { get; }

    /// <summary>Pinned source language, or null for auto-detection.</summary>
    string? Language { get; }

    /// <summary>Loads/prepares the specified model. Fails if the model is unavailable.</summary>
    Task PrepareAsync(string model, string? language, CancellationToken ct = default);

    /// <summary>
    /// Appends one audio chunk and returns the (possibly updated) transcript for
    /// the current segment. The recognizer maintains internal state between calls.
    /// </summary>
    Task<TranscriptionResult> ProcessAudioAsync(AudioFrame chunk, CancellationToken ct = default);

    /// <summary>
    /// Finalizes the current segment and returns its transcript as a Final result
    /// (used at end-of-speech). Returns <see cref="TranscriptionResult.Empty"/> if
    /// the segment produced no text.
    /// </summary>
    Task<TranscriptionResult> FinalizeAsync(CancellationToken ct = default);

    /// <summary>Clears segment state so the next audio starts a fresh question.</summary>
    void Reset();
}
