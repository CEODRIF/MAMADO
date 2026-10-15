using AusbildungsWegInterview.Core.Speech;
using AusbildungsWegInterview.Core.Translation;
using AusbildungsWegInterview.Tests.Fakes;
using AusbildungsWegInterview.Translation;
using Xunit;

namespace AusbildungsWegInterview.Tests.Translation;

/// <summary>
/// Every provider outcome must carry the right machine-readable
/// <see cref="TranslationStatus"/> (the UI localizes from the code; the raw
/// English StatusMessage is log-only). Honest labeling is preserved:
/// IsTranslated is true ONLY for a genuine translation.
/// </summary>
public class TranslationStatusCodeTests
{
    private sealed class FakeSegment : ISegmentTranslator
    {
        public string? Result { get; set; }
        public Task<string?> TranslateSegmentToEnglishAsync(CancellationToken ct = default)
            => Task.FromResult(Result);
    }

    [Fact]
    public async Task Ollama_StatusCodes_MatchOutcomes()
    {
        // unavailable
        var off = new OllamaTranslator(new FakeOllamaClient { Available = false }, "m");
        await off.PrepareAsync("de", "en");
        var r1 = await off.TranslateAsync("Hallo", "de", "en");
        Assert.Equal(TranslationStatus.ProviderUnavailable, r1.Status);
        Assert.False(r1.IsTranslated);

        // engine call fails
        var failing = new OllamaTranslator(new FakeOllamaClient
        {
            Available = true,
            GenerateException = new InvalidOperationException("Ollama error: boom")
        }, "m");
        await failing.PrepareAsync("de", "en");
        var r2 = await failing.TranslateAsync("Hallo", "de", "en");
        Assert.Equal(TranslationStatus.ProviderFailed, r2.Status);
        Assert.False(r2.IsTranslated);

        // no output produced
        var empty = new OllamaTranslator(new FakeOllamaClient { Available = true, GenerateResponse = "   " }, "m");
        await empty.PrepareAsync("de", "en");
        var r3 = await empty.TranslateAsync("Hallo", "de", "en");
        Assert.Equal(TranslationStatus.NoOutput, r3.Status);
        Assert.False(r3.IsTranslated);

        // model echoes the input (source == target)
        var echo = new OllamaTranslator(new FakeOllamaClient { Available = true, GenerateResponse = "Guten Tag" }, "m");
        await echo.PrepareAsync("de", "de");
        var r4 = await echo.TranslateAsync("Guten Tag", "de", "de");
        Assert.Equal(TranslationStatus.Echo, r4.Status);
        Assert.False(r4.IsTranslated);

        // genuine translation
        var ok = new OllamaTranslator(new FakeOllamaClient { Available = true, GenerateResponse = "Hello" }, "m");
        await ok.PrepareAsync("de", "en");
        var r5 = await ok.TranslateAsync("Hallo", "de", "en");
        Assert.Null(r5.Status);
        Assert.True(r5.IsTranslated);
    }

    [Fact]
    public async Task PassThrough_StatusCode_IsSet()
    {
        var t = new PassThroughTranslator();
        var r = await t.TranslateAsync("Hallo", "de", "en");
        Assert.Equal(TranslationStatus.PassThrough, r.Status);
        Assert.False(r.IsTranslated);
        Assert.Equal("Hallo", r.TargetText);
    }

    [Fact]
    public async Task WhisperAdapter_StatusCodes_MatchOutcomes()
    {
        var nonEn = new WhisperTranslateAdapter(new FakeSegment { Result = "x" });
        var r1 = await nonEn.TranslateAsync("Warum?", "de", "fr");
        Assert.Equal(TranslationStatus.WhisperEnglishOnly, r1.Status);

        var noEngine = new WhisperTranslateAdapter(null);
        var r2 = await noEngine.TranslateAsync("Warum?", "de", "en");
        Assert.Equal(TranslationStatus.WhisperEngineUnsupported, r2.Status);

        var echo = new WhisperTranslateAdapter(new FakeSegment { Result = "Hello there." });
        var r3 = await echo.TranslateAsync("Hello there.", "en", "en");
        Assert.Equal(TranslationStatus.Echo, r3.Status);

        var empty = new WhisperTranslateAdapter(new FakeSegment { Result = null });
        var r4 = await empty.TranslateAsync("Warum?", "de", "en");
        Assert.Equal(TranslationStatus.NoOutput, r4.Status);

        var ok = new WhisperTranslateAdapter(new FakeSegment { Result = "Why?" });
        var r5 = await ok.TranslateAsync("Warum?", "de", "en");
        Assert.Null(r5.Status);
        Assert.True(r5.IsTranslated);
    }

    [Fact]
    public async Task ExternalCli_StatusCodes_MatchOutcomes()
    {
        // auto source (needs a pinned language)
        var auto = new ExternalCliTranslator("/bin/true", "a", "de-en");
        var r1 = await auto.TranslateAsync("Hallo", null, "en");
        Assert.Equal(TranslationStatus.NeedsPinnedSource, r1.Status);

        // unsupported pair
        var pair = new ExternalCliTranslator("/bin/true", "a", "de-en");
        var r2 = await pair.TranslateAsync("Hallo", "de", "fr");
        Assert.Equal(TranslationStatus.PairNotSupported, r2.Status);

        // missing executable at translate time (launch failure)
        var missing = new ExternalCliTranslator(
            Path.Combine(Path.GetTempPath(), "awg-no-such-cli-" + Guid.NewGuid().ToString("N") + ".exe"),
            "a", "de-en");
        var r3 = await missing.TranslateAsync("Hallo", "de", "en");
        Assert.Equal(TranslationStatus.CliLaunchFailed, r3.Status);
        Assert.False(r3.IsTranslated);
    }
}
