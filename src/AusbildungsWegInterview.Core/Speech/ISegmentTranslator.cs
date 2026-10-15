namespace AusbildungsWegInterview.Core.Speech;

/// <summary>
/// Optional recognizer capability: translate the currently buffered segment
/// AUDIO to English (whisper.cpp <c>-tr</c> mode). Implemented by the
/// whisper.cpp CLI recognizer; not all recognizers support it.
///
/// This is audio-based (source audio, not text), which makes it a zero-extra-
/// dependency offline translation path for the "any language → English" case.
/// Returns null when there is no buffered audio or the translation failed.
/// </summary>
public interface ISegmentTranslator
{
    Task<string?> TranslateSegmentToEnglishAsync(CancellationToken ct = default);
}
