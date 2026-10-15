using AusbildungsWegInterview.Tests.Fakes;
using AusbildungsWegInterview.Translation;
using Xunit;

namespace AusbildungsWegInterview.Tests.Translation;

public class OllamaTranslatorTests
{
    [Fact]
    public async Task Translates_WhenAvailable()
    {
        var fake = new FakeOllamaClient { Available = true, GenerateResponse = "Wie geht es dir?" };
        var translator = new OllamaTranslator(fake, "m");
        await translator.PrepareAsync(null, "de");

        var r = await translator.TranslateAsync("How are you?", "en", "de");

        Assert.Equal("Wie geht es dir?", r.TargetText);
        Assert.Equal("en", r.SourceLanguage);
        Assert.Equal("de", r.TargetLanguage);
        Assert.False(r.IsProvisional);
    }

    [Fact]
    public async Task Unavailable_ReturnsEmptyTarget_AndProvisional()
    {
        var fake = new FakeOllamaClient { Available = false };
        var translator = new OllamaTranslator(fake, "m");
        await translator.PrepareAsync(null, "de");

        var r = await translator.TranslateAsync("How are you?", "en", "de");

        Assert.Equal(string.Empty, r.TargetText);
        Assert.True(r.IsProvisional);
    }

    [Fact]
    public async Task EmptyInput_ReturnsEmptyTarget()
    {
        var translator = new PassThroughTranslator("t");
        var r = await translator.TranslateAsync("", "en", "de");
        Assert.Equal(string.Empty, r.TargetText);
    }

    [Fact]
    public async Task PassThrough_ReturnsSourceUnchanged()
    {
        var translator = new PassThroughTranslator("t");
        var r = await translator.TranslateAsync("Guten Tag", "de", "en");
        Assert.Equal("Guten Tag", r.TargetText);
    }
}
