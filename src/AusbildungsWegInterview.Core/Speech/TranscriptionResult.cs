namespace AusbildungsWegInterview.Core.Speech;

/// <summary>
/// A single transcription result for the current segment. Partial results are
/// provisional and may change; a final result is treated as the stable question.
/// </summary>
public sealed record TranscriptionResult(
    string Text,
    TranscriptKind Kind,
    string? DetectedLanguage,
    double Confidence)
{
    public bool IsProvisional => Kind == TranscriptKind.Partial;
    public bool IsFinal => Kind == TranscriptKind.Final;

    public static TranscriptionResult Partial(string text, string? language = null, double confidence = 0)
        => new(Normalize(text), TranscriptKind.Partial, language, confidence);

    public static TranscriptionResult Final(string text, string? language = null, double confidence = 0)
        => new(Normalize(text), TranscriptKind.Final, language, confidence);

    public static readonly TranscriptionResult Empty = new(string.Empty, TranscriptKind.Final, null, 0);

    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var singleSpaced = string.Concat(text.Select(c => char.IsWhiteSpace(c) ? ' ' : c));
        return string.Join(' ', singleSpaced.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
