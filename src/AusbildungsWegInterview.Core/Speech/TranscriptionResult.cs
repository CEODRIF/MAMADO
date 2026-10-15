namespace AusbildungsWegInterview.Core.Speech;

/// <summary>
/// A single transcription result for the current segment. Partial results are
/// provisional and may change; a final result is treated as the stable question.
/// <see cref="TimestampInfo"/> preserves segment timing when the recognizer provides it.
/// </summary>
public sealed record TranscriptionResult(
    string Text,
    TranscriptKind Kind,
    string? DetectedLanguage,
    double Confidence,
    string? TimestampInfo = null)
{
    public bool IsProvisional => Kind == TranscriptKind.Partial;
    public bool IsFinal => Kind == TranscriptKind.Final;
    public bool HasTimestamp => !string.IsNullOrWhiteSpace(TimestampInfo);

    public static TranscriptionResult Partial(string text, string? language = null, double confidence = 0, string? timestampInfo = null)
        => new(Normalize(text), TranscriptKind.Partial, language, confidence, timestampInfo);

    public static TranscriptionResult Final(string text, string? language = null, double confidence = 0, string? timestampInfo = null)
        => new(Normalize(text), TranscriptKind.Final, language, confidence, timestampInfo);

    public static readonly TranscriptionResult Empty = new(string.Empty, TranscriptKind.Final, null, 0);

    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var singleSpaced = string.Concat(text.Select(c => char.IsWhiteSpace(c) ? ' ' : c));
        return string.Join(' ', singleSpaced.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
