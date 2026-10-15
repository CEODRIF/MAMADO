using AusbildungsWegInterview.Core.Common;

namespace AusbildungsWegInterview.Core.Config;

/// <summary>
/// Application configuration. This model holds no secrets and no credentials:
/// the default workflow requires no API key. Persistence is handled by the
/// Infrastructure layer (local-only).
/// </summary>
public sealed class AppConfiguration
{
    // --- languages ---
    /// <summary>Source language (ISO-639-1). Null = auto-detect (used for speech + translation).</summary>
    public string? SourceLanguage { get; set; }

    /// <summary>Language the question is translated into.</summary>
    public string TranslationLanguage { get; set; } = "en";

    /// <summary>When false, translation is disabled and the original is shown (clearly labeled as pass-through).</summary>
    public bool TranslationEnabled { get; set; } = true;

    /// <summary>Which local engine performs translation (see <see cref="TranslationProvider"/>).</summary>
    public TranslationProvider TranslationProvider { get; set; } = TranslationProvider.Ollama;

    // --- external CLI translation engine (e.g. opus-mt / mariannet) ---
    /// <summary>Path to a standalone translation CLI. Text goes via stdin; first stdout line is the translation.</summary>
    public string? TranslatorCliPath { get; set; }

    /// <summary>
    /// Argument template for the external CLI. Placeholders: {source}, {target}.
    /// Example (mariannet/opus-mt): "mariannet -m C:\models\opus-mt-de-en.bin -s {source} -t {target} -i - -o -"
    /// </summary>
    public string? TranslatorCliArgsTemplate { get; set; }

    /// <summary>Comma-separated supported pairs, e.g. "de-en,en-de,de-fr,en-ar". Unsupported pairs are reported, not faked.</summary>
    public string TranslatorSupportedPairs { get; set; } = "de-en,en-de";

    /// <summary>Per-translation timeout for the external CLI (seconds).</summary>
    public int TranslatorTimeoutSeconds { get; set; } = 30;

    /// <summary>Language generated answers are written in.</summary>
    public string AnswerLanguage { get; set; } = "de";

    public AnswerLength AnswerLength { get; set; } = AnswerLength.Normal;

    /// <summary>Tone of generated answers.</summary>
    public AnswerStyle AnswerStyle { get; set; } = AnswerStyle.Natural;

    // --- speech (whisper.cpp) ---
    /// <summary>whisper.cpp model name (e.g. "tiny", "base", "small").</summary>
    public string WhisperModel { get; set; } = "base";

    /// <summary>
    /// Path to a whisper.cpp executable (e.g. whisper-cli / main). When set and
    /// present, the CLI recognizer is used; otherwise the native-lib recognizer.
    /// </summary>
    public string? WhisperExecutablePath { get; set; }

    /// <summary>Explicit model file path override. Null = resolve from <see cref="WhisperModel"/> name.</summary>
    public string? WhisperModelPath { get; set; }

    /// <summary>Threads for whisper.cpp inference (CPU).</summary>
    public int WhisperThreads { get; set; } = 2;

    /// <summary>How often (seconds of new audio) a partial re-transcription runs.</summary>
    public double PartialIntervalSeconds { get; set; } = 1.0;

    // --- Ollama (optional local LLM) ---
    /// <summary>Ollama model used for answers/translation (optional runtime).</summary>
    public string OllamaModel { get; set; } = "llama3.1:8b";

    /// <summary>Local Ollama endpoint. Always localhost; never a paid service.</summary>
    public string OllamaBaseUrl { get; set; } = "http://127.0.0.1:11434";

    /// <summary>Per-request timeout for Ollama calls (seconds).</summary>
    public int OllamaRequestTimeoutSeconds { get; set; } = 60;

    public QualityProfile QualityProfile { get; set; } = QualityProfile.Balanced;

    /// <summary>Selected system output device to capture from loopback. Null = default.</summary>
    public string? OutputDeviceId { get; set; }

    public bool AlwaysOnTop { get; set; }

    // --- pipeline tuning ---
    public double ChunkSeconds { get; set; } = 0.5;
    public int TargetSampleRate { get; set; } = 16000;
    public double SilenceRmsThreshold { get; set; } = 0.01;
    /// <summary>Trailing silence (seconds) that finalizes an active segment.</summary>
    public double SilenceFinalizeSeconds { get; set; } = 0.8;
    /// <summary>Keep this much tail (seconds) so sentence endings aren't cut off.</summary>
    public double TrailingSilenceGuardSeconds { get; set; } = 0.15;
    public int MinQuestionChars { get; set; } = 8;
    /// <summary>How many recent Q/A turns to include as conversation context (bounded).</summary>
    public int RecentTurns { get; set; } = 3;
    /// <summary>How many capture auto-restarts to attempt after a fatal device error.</summary>
    public int MaxCaptureRestarts { get; set; } = 1;

    // --- privacy defaults: capture and transcripts are NOT persisted ---
    public bool PersistAudio { get; set; } = false;
    public bool PersistTranscripts { get; set; } = false;

    public static AppConfiguration Default() => new();

    public AppConfiguration Clone() => (AppConfiguration)MemberwiseClone();
}
