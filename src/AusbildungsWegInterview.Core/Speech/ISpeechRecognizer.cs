using AusbildungsWegInterview.Core.Audio;

namespace AusbildungsWegInterview.Core.Speech;

/// <summary>
/// Stateful real-time speech recognizer. The pipeline feeds it normalized audio
/// chunks; it returns an accumulated result for the current segment. Call
/// <see cref="Reset"/> to start a new question segment.
///
/// Implementations are pluggable (e.g. whisper.cpp). No API key is required for
/// the default local workflow.
/// </summary>
public interface ISpeechRecognizer : IAsyncDisposable
{
    /// <summary>True once the model is loaded and ready to transcribe.</summary>
    bool IsReady { get; }

    /// <summary>Identifier of the loaded model (e.g. "base", "small").</summary>
    string ModelName { get; }

    /// <summary>Pinned source language, or null for auto-detection.</summary>
    string? Language { get; }

    /// <summary>Loads/prepares the specified model. Fails if the model is unavailable.</summary>
    Task PrepareAsync(string model, string? language, CancellationToken ct = default);

    /// <summary>
    /// Processes one audio chunk and returns the (possibly updated) transcript for
    /// the current segment. The recognizer maintains internal state between calls.
    /// </summary>
    Task<TranscriptionResult> ProcessAudioAsync(AudioFrame chunk, CancellationToken ct = default);

    /// <summary>Clears segment state so the next audio starts a fresh question.</summary>
    void Reset();
}
