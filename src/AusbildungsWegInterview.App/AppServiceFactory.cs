using AusbildungsWegInterview.Audio;
using AusbildungsWegInterview.Infrastructure.Config;
using AusbildungsWegInterview.Infrastructure.Models;
using AusbildungsWegInterview.Infrastructure.Ollama;

namespace AusbildungsWegInterview.App;

/// <summary>
/// Windows bootstrap for the composition root. This is the ONLY place that
/// instantiates platform-specific concrete types (the WASAPI loopback
/// capture); everything else is injected into <see cref="AppServices"/> so
/// the composition root itself stays testable on any OS.
/// </summary>
public static class AppServiceFactory
{
    public static AppServices Create()
    {
        var store = new ConfigurationStore(ConfigurationStore.DefaultPath);
        var config = store.Load();

        // Local-only Ollama client with a configurable per-request timeout.
        var ollama = new OllamaClient(config.OllamaBaseUrl, requestTimeoutMs: Math.Max(5, config.OllamaRequestTimeoutSeconds) * 1000);
        var models = new WhisperModelManager(WhisperModelManager.DefaultDirectory);
        var audio = new LoopbackAudioCapture();

        return new AppServices(config, store, models, audio, ollama);
    }
}
