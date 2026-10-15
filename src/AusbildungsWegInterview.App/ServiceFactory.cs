using AusbildungsWegInterview.Answers;
using AusbildungsWegInterview.Audio;
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

namespace AusbildungsWegInterview.App;

/// <summary>
/// Composition root: builds the pipeline from concrete local providers and owns
/// their lifecycle. The UI only sees abstractions + this service bundle.
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

    public static AppServices Create()
    {
        var store = new ConfigurationStore(ConfigurationStore.DefaultPath);
        var config = store.Load();

        // Local-only Ollama client with a configurable per-request timeout.
        var ollama = new OllamaClient(config.OllamaBaseUrl, requestTimeoutMs: Math.Max(5, config.OllamaRequestTimeoutSeconds) * 1000);
        var models = new WhisperModelManager(WhisperModelManager.DefaultDirectory);
        var audio = new LoopbackAudioCapture();

        var services = new AppServices(config, store, models, audio, ollama);
        services.BuildSpeechAndPipeline();

        return services;
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

            BuildSpeechAndPipeline();
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

    private ITranslator CreateTranslator(AppConfiguration config, ISpeechRecognizer recognizer)
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

            _ => new OllamaTranslator(Ollama, config.OllamaModel)
        };
    }

    private void BuildSpeechAndPipeline()
    {
        var config = Config;

        string modelPath = !string.IsNullOrWhiteSpace(config.WhisperModelPath)
            ? config.WhisperModelPath
            : Models.ResolvePath(config.WhisperModel);

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
            ? CreateTranslator(config, recognizer)
            : new PassThroughTranslator();

        var answers = new OllamaAnswerGenerator(Ollama, config.OllamaModel);
        var detector = new QuestionDetector();

        _recognizer = recognizer;
        _translator = translator;
        _answers = answers;
        _pipeline = new InterviewPipeline(Audio, recognizer, translator, answers, detector, config,
            AusbildungsWegInterview.Infrastructure.Logging.AppLogging.CreateFileLogger());
    }

    private AppServices(
        AppConfiguration config,
        ConfigurationStore configStore,
        WhisperModelManager models,
        IAudioCapture audio,
        IOllamaClient ollama,
        InterviewPipeline? pipeline = null,
        ISpeechRecognizer? recognizer = null,
        ITranslator? translator = null,
        IAnswerGenerator? answers = null)
    {
        Config = config;
        ConfigStore = configStore;
        Models = models;
        Audio = audio;
        Ollama = ollama;
        _pipeline = pipeline ?? throw new InvalidOperationException("pipeline is built via BuildSpeechAndPipeline()");
        _recognizer = recognizer ?? throw new InvalidOperationException("recognizer is built via BuildSpeechAndPipeline()");
        _translator = translator ?? throw new InvalidOperationException("translator is built via BuildSpeechAndPipeline()");
        _answers = answers ?? throw new InvalidOperationException("answers is built via BuildSpeechAndPipeline()");
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
