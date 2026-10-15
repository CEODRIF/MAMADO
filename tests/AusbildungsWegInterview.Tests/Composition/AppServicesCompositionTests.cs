using AusbildungsWegInterview.App;
using AusbildungsWegInterview.Core.Common;
using AusbildungsWegInterview.Core.Config;
using AusbildungsWegInterview.Core.Domain;
using AusbildungsWegInterview.Core.Pipeline;
using AusbildungsWegInterview.Infrastructure.Config;
using AusbildungsWegInterview.Infrastructure.Models;
using AusbildungsWegInterview.Tests.Fakes;
using Xunit;

namespace AusbildungsWegInterview.Tests.Composition;

/// <summary>
/// Composition-root regression tests for <see cref="AppServices"/>.
///
/// Background: <c>AppServices.Create()</c> used to construct AppServices
/// WITHOUT a built pipeline and only called <c>BuildSpeechAndPipeline()</c>
/// afterwards — while the constructor threw
/// <c>InvalidOperationException("pipeline is built via BuildSpeechAndPipeline()")</c>.
/// The WPF app therefore crashed immediately at startup on Windows.
///
/// AppServices is now built INSIDE the constructor (as its last step) and
/// lives in the WPF-free Composition project, so the real composition root
/// can be exercised here with deterministic fakes on any OS.
/// </summary>
public class AppServicesCompositionTests : IDisposable
{
    private readonly string _dir;

    public AppServicesCompositionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "awg-comp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private AppServices BuildServices(AppConfiguration? config = null)
    {
        var noAudio = new NoopAudioCapture();
        var noOllama = new FakeOllamaClient();
        var services = new AppServices(
            config ?? AppConfiguration.Default(),
            new ConfigurationStore(Path.Combine(_dir, "settings.json")),
            new WhisperModelManager(Path.Combine(_dir, "models")),
            noAudio,
            noOllama);
        return services;
    }

    /// <summary>
    /// THE regression test: construction must not throw, and every service
    /// must be usable immediately (pipeline present, idle, not running).
    /// </summary>
    [Fact]
    public void Construct_InitializesAllServices_PipelineIdleAndUsable()
    {
        AppServices services = BuildServices(); // threw before the fix

        Assert.NotNull(services.Pipeline);
        Assert.Equal(PipelineState.Idle, services.Pipeline.State);
        Assert.False(services.Pipeline.IsRunning);

        // The bundle must expose exactly the dependencies it was created with.
        Assert.NotNull(services.Config);
        Assert.NotNull(services.ConfigStore);
        Assert.NotNull(services.Models);
        Assert.NotNull(services.Audio);
        Assert.NotNull(services.Ollama);

        // Context (CV / job description) is settable on the live pipeline.
        services.Pipeline.Context = CandidateContext.Empty.WithCv("test");
        Assert.Equal("test", services.Pipeline.Context.CvText);
    }

    [Fact]
    public async Task Construct_TranslationDisabled_StillFullyInitialized()
    {
        var config = AppConfiguration.Default();
        config.TranslationEnabled = false; // pass-through branch

        await using var services = BuildServices(config);

        Assert.NotNull(services.Pipeline);
        Assert.False(services.Pipeline.IsRunning);
        await services.DisposeAsync();
    }

    [Fact]
    public async Task Construct_ExternalCliWithoutPath_HonestPassThrough_NotCrash()
    {
        var config = AppConfiguration.Default();
        config.TranslationProvider = TranslationProvider.ExternalCli;
        config.TranslatorCliPath = null; // misconfiguration must not crash startup

        await using var services = BuildServices(config);

        Assert.NotNull(services.Pipeline);
        Assert.False(services.Pipeline.IsRunning);
        await services.DisposeAsync();
    }

    [Fact]
    public async Task RebuildPipeline_ReplacesPipeline_PreservesCandidateContext()
    {
        var config = AppConfiguration.Default();
        await using var services = BuildServices(config);

        var firstPipeline = services.Pipeline;
        firstPipeline.Context = CandidateContext.Empty.WithCv("my cv").WithJobDescription("my jd");

        // Simulate a runtime settings change (translation engine switch).
        config.TranslationProvider = TranslationProvider.ExternalCli;
        config.TranslatorCliPath = null;
        services.RebuildPipeline();

        Assert.NotSame(firstPipeline, services.Pipeline); // genuinely rebuilt
        Assert.Equal("my cv", services.Pipeline.Context.CvText); // context preserved
        Assert.Equal("my jd", services.Pipeline.Context.JobDescription);
        Assert.False(services.Pipeline.IsRunning);
        Assert.Equal(PipelineState.Idle, services.Pipeline.State);

        await services.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CompletesWithoutError_WhenNeverStarted()
    {
        var services = BuildServices();
        await services.DisposeAsync(); // pipeline was never started: must be a clean no-op-ish path
    }
}
