namespace AusbildungsWegInterview.Core.Speech;

/// <summary>
/// Whether a transcription result is still provisional (partial) or finalized.
/// </summary>
public enum TranscriptKind
{
    /// <summary>Still changing as more audio is processed.</summary>
    Partial = 0,

    /// <summary>Considered stable / finalized for the current segment.</summary>
    Final = 1
}
