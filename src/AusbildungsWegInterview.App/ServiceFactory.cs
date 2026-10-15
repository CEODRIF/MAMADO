using AusbildungsWegInterview.Answers;
using AusbildungsWegInterview.Audio;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Core.Audio;
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
    public InterviewPipeline Pipeline { get; }

    private readonly ISpeechRecognizer _recognizer;
    private readonly ITranslator _translator;
    private readonly IAnswerGenerator _answers;

    private AppServices(
        AppConfiguration config,
        ConfigurationStore configStore,
        WhisperModelManager models,
        IAudioCapture audio,
        IOllamaClient ollama,
        InterviewPipeline pipeline,
        ISpeechRecognizer recognizer,
        ITranslator translator,
        IAnswerGenerator answers)
    {
        Config = config;
        ConfigStore = configStore;
        Models = models;
        Audio = audio;
        Ollama = ollama;
        Pipeline = pipeline;
        _recognizer = recognizer;
        _translator = translator;
        _answers = answers;
    }

    public static AppServices Create()
    {
        var store = new ConfigurationStore(ConfigurationStore.DefaultPath);
        var config = store.Load();

        var ollama = new OllamaClient(config.OllamaBaseUrl);
        var models = new WhisperModelManager(WhisperModelManager.DefaultDirectory);
        var modelPath = models.ResolvePath(config.WhisperModel);

        var recognizer = new WhisperCppRecognizer(modelPath, threads: Math.Max(1, Environment.ProcessorCount));
        var translator = new OllamaTranslator(ollama, config.OllamaModel);
        var answers = new OllamaAnswerGenerator(ollama, config.OllamaModel);
        var detector = new QuestionDetector();
        var audio = new LoopbackAudioCapture();

        var pipeline = new InterviewPipeline(audio, recognizer, translator, answers, detector, config);

        return new AppServices(config, store, models, audio, ollama, pipeline, recognizer, translator, answers);
    }

    public async ValueTask DisposeAsync()
    {
        // Stop and release the pipeline first.
        await Pipeline.DisposeAsync();
        await ((IAsyncDisposable)_recognizer).DisposeAsync();
        await ((IAsyncDisposable)_translator).DisposeAsync();
        await ((IAsyncDisposable)_answers).DisposeAsync();
        if (Audio is IAsyncDisposable ad)
            await ad.DisposeAsync();
        await Ollama.DisposeAsync();
    }
}
