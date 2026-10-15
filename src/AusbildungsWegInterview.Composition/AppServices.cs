using AusbildungsWegInterview.Answers;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Common;
using AusbildungsWegInterview.Core.Config;
using AusbildungsWegInterview.Core.Pipeline;
using AusbildungsWegInterview.Core.Speech;
using AusbildungsWegInterview.Core.Translation;
using AusbildungsWegInterview.Infrastructure.Config;
using AusbildungsWegInterview.Infrastructure.Models;
using AusbildungsWegInterview.Infrastructure.Ollama;
using AusbildungsWegInterview.Speech;
using AusbildungsWegInterview.Translation;

// Namespace stays "AusbildungsWegInterview.App" on purpose: this IS the app's
// composition root, and keeping the namespace lets the WPF project use it
// without any using changes. The class itself has NO WPF/platform dependency.
namespace AusbildungsWegInterview.App;

/// <summary>
/// Composition root: builds the pipeline from concrete local providers and owns
/// their lifecycle. The UI only sees abstractions + this service bundle.
///
/// Invariant: after the constructor returns, EVERY service (pipeline,
/// recognizer, translator, answer generator) is fully initialized. The
/// pipeline is built inside the constructor — never after construction —
/// because a half-built AppServices used to crash the app at startup
/// ("pipeline is built via BuildSpeechAndPipeline()").
/// </summary>
public sealed class AppServices : IAsyncDisposable
{
    public AppConfiguration Config { get; }
    public ConfigurationStore ConfigStore { get; }
    public WhisperModelManager Models { get; }
    public IAudioCapture Audio { get; }
    public IOllamaClient Ollama { get; }

    private InterviewPipeline _pipeline;
    /// <summary>Replaced by <see cref="RebuildPipeline"/> when speech/translation settings change.</summary>
    public InterviewPipeline Pipeline => _pipeline;

    private ISpeechRecognizer _recognizer;
    private ITranslator _translator;
    private IAnswerGenerator _answers;

    private readonly object _rebuildGate = new();

    /// <summary>
    /// Creates a fully initialized service bundle. All platform-specific
    /// concrete types (e.g. the WASAPI loopback capture) are injected, which
    /// keeps this class testable on any OS.
    /// </summary>
    public AppServices(
        AppConfiguration config,
        ConfigurationStore configStore,
        WhisperModelManager models,
        IAudioCapture audio,
        IOllamaClient ollama)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        ConfigStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        Models = models ?? throw new ArgumentNullException(nameof(models));
        Audio = audio ?? throw new ArgumentNullException(nameof(audio));
        Ollama = ollama ?? throw new ArgumentNullException(nameof(ollama));

        // Build the pipeline as the LAST step of construction: every dependency
        // it reads (Config, Models, Audio, Ollama) is assigned by this point,
        // and all four services are assigned before the constructor exits.
        // No member can observe AppServices in a half-built state.
        (var pipeline, var recognizer, var translator, var answers) = BuiltServices.Build(config, models, audio, ollama);
        _pipeline = pipeline;
        _recognizer = recognizer;
        _translator = translator;
        _answers = answers;
    }

    /// <summary>
    /// Rebuilds the speech recognizer / translator / answer generator / pipeline
    /// from the CURRENT config (used when the user changes whisper executable,
    /// model paths, translation toggle, or answer style at runtime). The pipeline
    /// must not be running. The candidate context (CV / job description) is preserved.
    /// </summary>
    public void RebuildPipeline()
    {
        lock (_rebuildGate)
        {
            if (Pipeline.IsRunning)
                throw new InvalidOperationException("Stop listening before changing these settings.");

            var keepContext = Pipeline.Context;

            var oldPipeline = Pipeline;
            var oldRecognizer = _recognizer;
            var oldTranslator = _translator;
            var oldAnswers = _answers;

            (var pipeline, var recognizer, var translator, var answers) = BuiltServices.Build(Config, Models, Audio, Ollama);
            _pipeline = pipeline;
            _recognizer = recognizer;
            _translator = translator;
            _answers = answers;
            Pipeline.Context = keepContext;

            // Release the old instances off the critical path.
            var p = oldPipeline;
            var r = oldRecognizer;
            var t = oldTranslator;
            var a = oldAnswers;
            _ = Task.Run(async () =>
            {
                try { await p.DisposeAsync(); } catch { /* best effort */ }
                try { await r.DisposeAsync(); } catch { /* best effort */ }
                try { await t.DisposeAsync(); } catch { /* best effort */ }
                try { await a.DisposeAsync(); } catch { /* best effort */ }
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Snapshot under the gate, dispose outside (no awaits inside a lock).
        InterviewPipeline pipeline;
        ISpeechRecognizer recognizer;
        ITranslator translator;
        IAnswerGenerator answers;
        lock (_rebuildGate)
        {
            pipeline = _pipeline;
            recognizer = _recognizer;
            translator = _translator;
            answers = _answers;
        }

        await pipeline.DisposeAsync();
        await recognizer.DisposeAsync();
        await translator.DisposeAsync();
        await answers.DisposeAsync();
        if (Audio is IAsyncDisposable ad)
            await ad.DisposeAsync();
        await Ollama.DisposeAsync();
    }
}

/// <summary>
/// Pure builder for the pipeline and its providers (no lifecycle coupling).
/// Extracted so the construction logic has a single, unit-testable home:
/// the same code path is used by the constructor and by <c>RebuildPipeline</c>.
/// </summary>
internal static class BuiltServices
{
    public static (InterviewPipeline Pipeline, ISpeechRecognizer Recognizer, ITranslator Translator, IAnswerGenerator Answers)
        Build(AppConfiguration config, WhisperModelManager models, IAudioCapture audio, IOllamaClient ollama)
    {
        string modelPath = !string.IsNullOrWhiteSpace(config.WhisperModelPath)
            ? config.WhisperModelPath
            : models.ResolvePath(config.WhisperModel);

        // Prefer the whisper.cpp CLI when an executable path is configured; otherwise the native-lib adapter.
        ISpeechRecognizer recognizer;
        if (!string.IsNullOrWhiteSpace(config.WhisperExecutablePath) && System.IO.File.Exists(config.WhisperExecutablePath))
        {
            recognizer = new WhisperCliRecognizer(
                config.WhisperExecutablePath, modelPath, config.WhisperThreads, config.PartialIntervalSeconds);
        }
        else
        {
            recognizer = new WhisperCppRecognizer(modelPath, threads: Math.Max(1, config.WhisperThreads));
        }

        // Translation provider selection (all local; none requires a cloud service).
        ITranslator translator = config.TranslationEnabled
            ? CreateTranslator(config, ollama, recognizer)
            : new PassThroughTranslator();

        var answers = new OllamaAnswerGenerator(ollama, config.OllamaModel);
        var detector = new QuestionDetector();

        var pipeline = new InterviewPipeline(audio, recognizer, translator, answers, detector, config,
            AusbildungsWegInterview.Infrastructure.Logging.AppLogging.CreateFileLogger());
        return (pipeline, recognizer, translator, answers);
    }

    private static ITranslator CreateTranslator(AppConfiguration config, IOllamaClient ollama, ISpeechRecognizer recognizer)
    {
        return config.TranslationProvider switch
        {
            TranslationProvider.WhisperTranslate =>
                new WhisperTranslateAdapter(recognizer as Core.Speech.ISegmentTranslator),

            TranslationProvider.ExternalCli when
                !string.IsNullOrWhiteSpace(config.TranslatorCliPath) =>
                new ExternalCliTranslator(
                    config.TranslatorCliPath,
                    config.TranslatorCliArgsTemplate,
                    config.TranslatorSupportedPairs,
                    config.TranslatorTimeoutSeconds),

            // External CLI requested but no path configured: an Ollama fallback would
            // silently hide the misconfiguration. Pass-through with an honest label.
            TranslationProvider.ExternalCli =>
                new PassThroughTranslator("external CLI (not configured)"),

            _ => new OllamaTranslator(ollama, config.OllamaModel)
        };
    }
}
