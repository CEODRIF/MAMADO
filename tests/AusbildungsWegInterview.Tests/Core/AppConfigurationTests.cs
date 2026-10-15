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
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var c = AppConfiguration.Default();
        var c2 = c.Clone();
        c2.AnswerLanguage = "fr";
        c2.WhisperModel = "small";
        Assert.NotEqual(c.AnswerLanguage, c2.AnswerLanguage);
        Assert.NotEqual(c.WhisperModel, c2.WhisperModel);
    }
}
