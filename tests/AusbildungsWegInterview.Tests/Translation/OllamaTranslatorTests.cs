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
        Assert.Null(r.StatusMessage);
        Assert.True(r.IsTranslated);
    }

    [Fact]
    public async Task Unavailable_ReturnsStatus_AndIsNotLabeledTranslated()
    {
        var fake = new FakeOllamaClient { Available = false };
        var translator = new OllamaTranslator(fake, "m");
        await translator.PrepareAsync(null, "de");

        var r = await translator.TranslateAsync("How are you?", "en", "de");

        Assert.Equal(string.Empty, r.TargetText);
        Assert.False(r.IsTranslated);
        Assert.Contains("Ollama unavailable", r.StatusMessage);
    }

    [Fact]
    public async Task ChangedText_IsLabeledTranslated()
    {
        var fake = new FakeOllamaClient { Available = true, GenerateResponse = "Ich bin stark in C#." };
        var translator = new OllamaTranslator(fake, "m");
        await translator.PrepareAsync(null, "de");

        var r = await translator.TranslateAsync("I am strong in C#.", "en", "de");

        Assert.True(r.IsTranslated);
        Assert.Null(r.StatusMessage);
    }

    [Fact]
    public async Task UnchangedText_IsNotFalselyLabeledTranslated()
    {
        // Model echoes the input (e.g. source == target language): must not claim a translation.
        var fake = new FakeOllamaClient { Available = true, GenerateResponse = "Guten Tag" };
        var translator = new OllamaTranslator(fake, "m");
        await translator.PrepareAsync(null, "de");

        var r = await translator.TranslateAsync("Guten Tag", "de", "de");

        Assert.Equal("Guten Tag", r.TargetText);
        Assert.False(r.IsTranslated);
        Assert.Contains("unchanged", r.StatusMessage);
    }

    [Fact]
    public async Task GenerateFailure_ReturnsStatus_NotCrash()
    {
        var fake = new FakeOllamaClient
        {
            Available = true,
            GenerateException = new System.IO.IOException("simulated timeout")
        };
        var translator = new OllamaTranslator(fake, "m");
        await translator.PrepareAsync(null, "de");

        var r = await translator.TranslateAsync("How are you?", "en", "de");

        Assert.Equal(string.Empty, r.TargetText);
        Assert.False(r.IsTranslated);
        Assert.Contains("Translation failed", r.StatusMessage);
    }

    [Fact]
    public async Task EmptyModelOutput_ReturnsStatus()
    {
        var fake = new FakeOllamaClient { Available = true, GenerateResponse = "   " };
        var translator = new OllamaTranslator(fake, "m");
        await translator.PrepareAsync(null, "de");

        var r = await translator.TranslateAsync("How are you?", "en", "de");

        Assert.False(r.IsTranslated);
        Assert.Contains("No translation produced", r.StatusMessage);
    }

    [Fact]
    public async Task EmptyInput_ReturnsEmptyTarget()
    {
        var translator = new PassThroughTranslator("t");
        var r = await translator.TranslateAsync("", "en", "de");
        Assert.Equal(string.Empty, r.TargetText);
    }

    [Fact]
    public async Task PassThrough_ReturnsSourceUnchanged_AndIsLabeledPassThrough()
    {
        var translator = new PassThroughTranslator("t");
        var r = await translator.TranslateAsync("Guten Tag", "de", "en");

        Assert.Equal("Guten Tag", r.TargetText);
        Assert.False(r.IsTranslated);
        Assert.Contains("pass-through", r.StatusMessage);
    }
}
