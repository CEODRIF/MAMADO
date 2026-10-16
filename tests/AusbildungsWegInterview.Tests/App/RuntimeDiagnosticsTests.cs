using AusbildungsWegInterview.App;
using AusbildungsWegInterview.Speech;
using Xunit;

namespace AusbildungsWegInterview.Tests.App;

/// <summary>
/// First-run prerequisite classification (WPF-free): required = native
/// runtime + speech model; Ollama is optional and must never block the app.
/// </summary>
public class RuntimeDiagnosticsTests
{
    private static RuntimeReport AllOk()
        => RuntimeDiagnostics.Build(WhisperRuntimeState.Ready, "1.9.5", modelExists: true, ollamaServerAvailable: true, ollamaModelAvailable: true);

    [Fact]
    public void AllPrerequisitesPresent_NoIssuesAtAll()
    {
        var r = AllOk();

        Assert.False(r.HasRequiredIssues);
        Assert.False(r.HasAnyIssues);
        Assert.Equal(SetupItemState.Ok, r.Runtime.State);
        Assert.Equal(SetupItemState.Ok, r.SpeechModel.State);
        Assert.Equal(SetupItemState.Ok, r.Ollama.State);
    }

    [Fact]
    public void RuntimeMissing_IsRequiredIssue()
    {
        var r = RuntimeDiagnostics.Build(WhisperRuntimeState.Missing, null, true, true, true);

        Assert.True(r.HasRequiredIssues);
        Assert.Equal(SetupItemState.Missing, r.Runtime.State);
        Assert.Null(r.Runtime.Detail);
    }

    [Fact]
    public void RuntimeVersionMismatch_IsRequiredIssue_AndKeepsFoundVersion()
    {
        var r = RuntimeDiagnostics.Build(WhisperRuntimeState.VersionMismatch, "1.5.0", true, true, true);

        Assert.True(r.HasRequiredIssues);
        Assert.Equal(SetupItemState.Mismatch, r.Runtime.State);
        Assert.True(r.Runtime.Detail == "1.5.0", "the found version must reach the UI for an actionable message");
    }

    [Fact]
    public void SpeechModelMissing_IsRequiredIssue_EvenWithRuntimeReady()
    {
        var r = RuntimeDiagnostics.Build(WhisperRuntimeState.Ready, "1.9.5", modelExists: false, true, true);

        Assert.True(r.HasRequiredIssues);
        Assert.Equal(SetupItemState.Missing, r.SpeechModel.State);
    }

    [Fact]
    public void RuntimeReadyButModelMissing_OnlyModelLineShown()
    {
        var r = RuntimeDiagnostics.Build(WhisperRuntimeState.Ready, "1.9.5", modelExists: false, true, true);

        Assert.Equal(SetupItemState.Ok, r.Runtime.State);
        Assert.Equal(SetupItemState.Missing, r.SpeechModel.State);
    }

    [Fact]
    public void OllamaOffline_IsOptional_NotRequiredIssue_ButShown()
    {
        var r = RuntimeDiagnostics.Build(WhisperRuntimeState.Ready, "1.9.5", true, ollamaServerAvailable: false, ollamaModelAvailable: false);

        Assert.False(r.HasRequiredIssues, "Ollama is optional: core transcription must never be blocked");
        Assert.True(r.HasAnyIssues, "the user should still see that answers/AI-translation need Ollama");
        Assert.Equal(SetupItemState.OptionalUnavailable, r.Ollama.State);
    }

    [Fact]
    public void OllamaUpModelNotPulled_IsOptionalMissing_ButShown()
    {
        var r = RuntimeDiagnostics.Build(WhisperRuntimeState.Ready, "1.9.5", true, ollamaServerAvailable: true, ollamaModelAvailable: false);

        Assert.False(r.HasRequiredIssues);
        Assert.True(r.HasAnyIssues);
        Assert.Equal(SetupItemState.Missing, r.Ollama.State);
    }

    [Fact]
    public void WorstCase_AllThreeIssues_Coexist()
    {
        var r = RuntimeDiagnostics.Build(WhisperRuntimeState.VersionMismatch, "1.5.0", false, false, false);

        Assert.True(r.HasRequiredIssues);
        Assert.True(r.HasAnyIssues);
        Assert.Equal(SetupItemState.Mismatch, r.Runtime.State);
        Assert.Equal(SetupItemState.Missing, r.SpeechModel.State);
        Assert.Equal(SetupItemState.OptionalUnavailable, r.Ollama.State);
    }

    [Fact]
    public void ClassifySpeechModel_BothDirections()
    {
        Assert.Equal(SetupItemState.Ok, RuntimeDiagnostics.ClassifySpeechModel(true).State);
        Assert.Equal(SetupItemState.Missing, RuntimeDiagnostics.ClassifySpeechModel(false).State);
    }

    [Fact]
    public void ClassifyOllama_Matrix()
    {
        Assert.Equal(SetupItemState.Ok, RuntimeDiagnostics.ClassifyOllama(true, true).State);
        Assert.Equal(SetupItemState.Missing, RuntimeDiagnostics.ClassifyOllama(true, false).State);
        Assert.True(RuntimeDiagnostics.ClassifyOllama(false, true).State == SetupItemState.OptionalUnavailable,
            "server down means model state is irrelevant");
        Assert.Equal(SetupItemState.OptionalUnavailable, RuntimeDiagnostics.ClassifyOllama(false, false).State);
    }
}
