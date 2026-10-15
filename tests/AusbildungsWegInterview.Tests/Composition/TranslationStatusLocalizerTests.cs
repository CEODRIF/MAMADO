using AusbildungsWegInterview.App;
using AusbildungsWegInterview.Core.Translation;
using AusbildungsWegInterview.Localization;
using Xunit;

namespace AusbildungsWegInterview.Tests.Composition;

/// <summary>
/// Every TranslationStatus code must map to a localized, honest message in
/// ALL four UI languages (and a genuine translation must show no status at
/// all). Placeholders ({0}/{1}) are substituted from the result.
/// </summary>
public class TranslationStatusLocalizerTests
{
    private static TranslationResult R(TranslationStatus? status, string src = "de", string tgt = "en")
        => new("Hallo", string.Empty, src, tgt, false, "Test",
            "some raw english provider text that must never reach the UI", status);

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("ar")]
    public void EveryStatus_IsLocalized_InAllCultures(string culture)
    {
        var loc = new LocalizationManager(culture);

        var cases = new (TranslationStatus Status, string Key)[]
        {
            (TranslationStatus.PassThrough, "TranslStatusPassThrough"),
            (TranslationStatus.Echo, "TranslStatusEcho"),
            (TranslationStatus.ProviderUnavailable, "TranslStatusProviderUnavailable"),
            (TranslationStatus.ProviderFailed, "TranslStatusProviderFailed"),
            (TranslationStatus.NoOutput, "TranslStatusNoOutput"),
            (TranslationStatus.NeedsPinnedSource, "TranslStatusNeedsSource"),
            (TranslationStatus.PairNotSupported, "TranslStatusPairNotSupported"),
            (TranslationStatus.CliLaunchFailed, "TranslStatusCliLaunch"),
            (TranslationStatus.CliTimeout, "TranslStatusCliTimeout"),
            (TranslationStatus.CliError, "TranslStatusCliError"),
            (TranslationStatus.WhisperEnglishOnly, "TranslStatusWhisperEnglishOnly"),
            (TranslationStatus.WhisperEngineUnsupported, "TranslStatusWhisperEngine"),
        };

        foreach (var (status, key) in cases)
        {
            // The two keys with placeholders are compared against their
            // formatted form (R() defaults: source "de", target "en").
            string expected = status switch
            {
                TranslationStatus.PairNotSupported => loc.F("TranslStatusPairNotSupported", "de", "en"),
                TranslationStatus.WhisperEnglishOnly => loc.F("TranslStatusWhisperEnglishOnly", "en"),
                _ => loc[key]
            };
            string actual = TranslationStatusLocalizer.Localize(loc, R(status));
            Assert.True(!string.IsNullOrWhiteSpace(actual), $"status line is empty for {key} in {culture}");
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void GenuineTranslation_ShowNoStatus()
    {
        var loc = new LocalizationManager("en");
        var r = new TranslationResult("Hallo", "Hello", "de", "en", false, "Test");
        Assert.Empty(TranslationStatusLocalizer.Localize(loc, r));
    }

    [Fact]
    public void StatusMessageWithoutCode_FallsBackToGeneric_NotRawText()
    {
        // Defensive: a provider that sets a raw message but no machine code
        // must not leak the raw text into the UI.
        var loc = new LocalizationManager("en");
        var r = new TranslationResult("Hallo", string.Empty, "de", "en", false, "Test",
            "raw provider detail C:\\x\\y", null);
        string msg = TranslationStatusLocalizer.Localize(loc, r);
        Assert.Equal(loc["TranslStatusProviderFailed"], msg);
        Assert.DoesNotContain("raw provider detail", msg);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("ar")]
    public void Placeholders_AreSubstituted(string culture)
    {
        var loc = new LocalizationManager(culture);

        string pair = TranslationStatusLocalizer.Localize(loc, R(TranslationStatus.PairNotSupported, "de", "fr"));
        Assert.Contains("de", pair);
        Assert.Contains("fr", pair);

        string en = TranslationStatusLocalizer.Localize(loc, R(TranslationStatus.WhisperEnglishOnly, "de", "fr"));
        Assert.Contains("fr", en);
    }

    [Fact]
    public void ArabicStatus_IsNotEmptyAndLocalized()
    {
        var loc = new LocalizationManager("ar");
        string msg = TranslationStatusLocalizer.Localize(loc, R(TranslationStatus.ProviderUnavailable));
        Assert.NotEmpty(msg);
        Assert.NotEqual("TranslStatusProviderUnavailable", msg);
        // sanity: an Arabic line for the Arabic UI (RTL handled by FlowDirection)
        Assert.True(msg.Any(c => c >= '\u0600' && c <= '\u06FF'), "Arabic script expected in the Arabic status line.");
    }
}
