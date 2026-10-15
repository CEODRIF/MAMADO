using AusbildungsWegInterview.Core.Common;

namespace AusbildungsWegInterview.Core.Config;

/// <summary>
/// Application configuration. This model holds no secrets and no credentials:
/// the default workflow requires no API key. Persistence is handled by the
/// Infrastructure layer (local-only).
/// </summary>
public sealed class AppConfiguration
{
    /// <summary>Source language (ISO-639-1). Null = auto-detect.</summary>
    public string? SourceLanguage { get; set; }

    /// <summary>Language the question is translated into.</summary>
    public string TranslationLanguage { get; set; } = "en";

    /// <summary>Language generated answers are written in.</summary>
    public string AnswerLanguage { get; set; } = "de";

    public AnswerLength AnswerLength { get; set; } = AnswerLength.Normal;

    /// <summary>whisper.cpp model name (e.g. "tiny", "base", "small").</summary>
    public string WhisperModel { get; set; } = "base";

    /// <summary>Ollama model used for answers (optional runtime).</summary>
    public string OllamaModel { get; set; } = "llama3.1:8b";

    /// <summary>Local Ollama endpoint. Always localhost; never a paid service.</summary>
    public string OllamaBaseUrl { get; set; } = "http://127.0.0.1:11434";

    public QualityProfile QualityProfile { get; set; } = QualityProfile.Balanced;

    /// <summary>Selected system output device to capture from loopback. Null = default.</summary>
    public string? OutputDeviceId { get; set; }

    public bool AlwaysOnTop { get; set; }

    // --- pipeline tuning ---
    public double ChunkSeconds { get; set; } = 1.0;
    public int TargetSampleRate { get; set; } = 16000;
    public double SilenceRmsThreshold { get; set; } = 0.01;
    public int MinQuestionChars { get; set; } = 8;

    // --- privacy defaults: capture and transcripts are NOT persisted ---
    public bool PersistAudio { get; set; } = false;
    public bool PersistTranscripts { get; set; } = false;

    public static AppConfiguration Default() => new();

    public AppConfiguration Clone() => (AppConfiguration)MemberwiseClone();
}
