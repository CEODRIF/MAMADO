using AusbildungsWegInterview.Core.Common;
using AusbildungsWegInterview.Core.Config;
using Xunit;

namespace AusbildungsWegInterview.Tests.Core;

public class AppConfigurationTests
{
    [Fact]
    public void Defaults_AreSafeAndLocal()
    {
        var c = AppConfiguration.Default();
        Assert.Equal("de", c.AnswerLanguage);
        Assert.Equal("en", c.TranslationLanguage);
        Assert.Equal("base", c.WhisperModel);
        Assert.Equal(AnswerLength.Normal, c.AnswerLength);
        Assert.False(c.PersistAudio);
        Assert.False(c.PersistTranscripts);
        Assert.Equal("http://127.0.0.1:11434", c.OllamaBaseUrl);

        // Phase 2 additions
        Assert.True(c.TranslationEnabled);
        Assert.Equal(AnswerStyle.Natural, c.AnswerStyle);
        Assert.Null(c.WhisperExecutablePath);
        Assert.Null(c.WhisperModelPath);
        Assert.True(c.WhisperThreads >= 1);
        Assert.True(c.PartialIntervalSeconds > 0);
        Assert.True(c.OllamaRequestTimeoutSeconds >= 5);
        Assert.True(c.SilenceFinalizeSeconds > 0);
        Assert.True(c.TrailingSilenceGuardSeconds >= 0);
        Assert.True(c.RecentTurns >= 0);
        Assert.True(c.MaxCaptureRestarts >= 0);
        Assert.True(c.ChunkSeconds > 0);
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var c = AppConfiguration.Default();
        var c2 = c.Clone();
        c2.AnswerLanguage = "fr";
        c2.WhisperModel = "small";
        c2.WhisperExecutablePath = "C:/x/whisper-cli.exe";
        c2.WhisperModelPath = "C:/x/ggml-small.bin";
        c2.TranslationEnabled = false;
        c2.AnswerStyle = AnswerStyle.Professional;
        c2.OllamaRequestTimeoutSeconds = 12;

        Assert.NotEqual(c.AnswerLanguage, c2.AnswerLanguage);
        Assert.NotEqual(c.WhisperModel, c2.WhisperModel);
        Assert.Null(c.WhisperExecutablePath);
        Assert.Null(c.WhisperModelPath);
        Assert.True(c.TranslationEnabled);
        Assert.Equal(AnswerStyle.Natural, c.AnswerStyle);
        Assert.Equal(60, c.OllamaRequestTimeoutSeconds);
    }
}
