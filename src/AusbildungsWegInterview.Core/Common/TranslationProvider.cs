namespace AusbildungsWegInterview.Core.Common;

/// <summary>
/// Which local engine translates detected questions. All options are offline
/// and local; none requires a cloud service or an API key.
/// </summary>
public enum TranslationProvider
{
    /// <summary>Local Ollama LLM (full multilingual; needs Ollama running + a pulled model).</summary>
    Ollama = 0,

    /// <summary>
    /// whisper.cpp translate mode (<c>-tr</c>): any source language → English only,
    /// zero extra dependency (reuses the speech model).
    /// </summary>
    WhisperTranslate = 1,

    /// <summary>
    /// A standalone translation CLI (e.g. opus-mt / mariannet) for dedicated
    /// language pairs (DE↔FR, EN↔AR, ...). User-provided binary + models.
    /// </summary>
    ExternalCli = 2,

    /// <summary>Translation disabled: the original is shown, clearly labeled (pass-through).</summary>
    Off = 3
}
