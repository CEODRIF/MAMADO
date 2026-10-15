namespace AusbildungsWegInterview.Core.Translation;

/// <summary>
/// Machine-readable outcome codes for a translation attempt. Providers set
/// <see cref="TranslationResult.Status"/> to one of these; the UI layer maps
/// them to localized messages (see TranslationStatusLocalizer). The human
/// English <c>StatusMessage</c> stays on the result for logs/tests, but is
/// never the user-facing string — it must work in all four UI languages.
/// </summary>
public enum TranslationStatus
{
    /// <summary>No problem (a genuine translation was produced, or nothing to report).</summary>
    None,

    /// <summary>Translation is disabled; the original is shown (pass-through).</summary>
    PassThrough,

    /// <summary>The engine returned the input unchanged (e.g. source already equals target).</summary>
    Echo,

    /// <summary>The translation engine (Ollama) is not running / unreachable.</summary>
    ProviderUnavailable,

    /// <summary>The engine call failed (runtime error, malformed response, ...).</summary>
    ProviderFailed,

    /// <summary>The engine produced no output.</summary>
    NoOutput,

    /// <summary>This engine needs a pinned (non-auto) source language.</summary>
    NeedsPinnedSource,

    /// <summary>The source/target pair is not covered by the configured model(s).</summary>
    PairNotSupported,

    /// <summary>The external CLI could not be launched.</summary>
    CliLaunchFailed,

    /// <summary>The external CLI timed out.</summary>
    CliTimeout,

    /// <summary>The external CLI exited with an error / produced no usable output.</summary>
    CliError,

    /// <summary>whisper translation supports English targets only.</summary>
    WhisperEnglishOnly,

    /// <summary>The active speech engine does not support whisper translate mode.</summary>
    WhisperEngineUnsupported
}
