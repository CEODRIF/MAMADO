using AusbildungsWegInterview.App;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Core.Common;
using AusbildungsWegInterview.Localization;
using Xunit;

namespace AusbildungsWegInterview.Tests.Composition;

/// <summary>
/// Every AnswerStatus code must map to a localized, honest message in ALL
/// four UI languages (and a real suggestion must show no status line). The
/// raw English message on the suggestion is log-only and must never be the
/// user-facing text in a non-English culture.
/// </summary>
public class AnswerStatusLocalizerTests
{
    private const string RawEnglish = "some raw english provider text that must never reach the UI";

    private static AnswerSuggestion Suggestion(AnswerStatus status, string? model = null)
        => new("Warum?", RawEnglish, AnswerLength.Normal, "de", false, model, null, status);

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("ar")]
    public void EveryStatus_IsLocalized_InAllCultures(string culture)
    {
        var loc = new LocalizationManager(culture);

        var cases = new (AnswerStatus Status, string Key)[]
        {
            (AnswerStatus.RuntimeUnavailable, "AnswerStatusUnavailable"),
            (AnswerStatus.ModelMissing, "AnswerStatusModelMissing"),
            (AnswerStatus.GenerationFailed, "AnswerStatusFailed"),
            (AnswerStatus.GenerationTimeout, "AnswerStatusTimeout"),
            (AnswerStatus.EmptyResponse, "AnswerStatusEmpty"),
        };

        foreach (var (status, key) in cases)
        {
            string expected = status is AnswerStatus.ModelMissing
                ? loc.F(key, "m")   // placeholder filled from the suggestion's model
                : loc[key];
            string actual = AnswerStatusLocalizer.Localize(loc, Suggestion(status, model: status is AnswerStatus.ModelMissing ? "m" : null));
            Assert.True(!string.IsNullOrWhiteSpace(actual), $"status line is empty for {key} in {culture}");
            Assert.Equal(expected, actual);
            Assert.DoesNotContain(RawEnglish, actual);
        }
    }

    [Theory]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("ar")]
    public void NonEnglish_Locales_NeverRenderTheRawEnglishMessage(string culture)
    {
        var loc = new LocalizationManager(culture);
        string actual = AnswerStatusLocalizer.Localize(loc, Suggestion(AnswerStatus.RuntimeUnavailable));
        // The raw English marker phrase from the generator must not leak.
        Assert.DoesNotContain("isn't running", actual);
        Assert.DoesNotContain("I can't draft", actual);
    }

    [Fact]
    public void RealSuggestion_YieldsNoStatusLine()
    {
        var loc = new LocalizationManager("en");
        Assert.Empty(AnswerStatusLocalizer.Localize(loc, Suggestion(AnswerStatus.None)));
    }

    [Fact]
    public void ModelMissing_SubstitutesModelName()
    {
        var loc = new LocalizationManager("en");
        string actual = AnswerStatusLocalizer.Localize(loc, Suggestion(AnswerStatus.ModelMissing, model: "phi3:mini"));
        Assert.Contains("phi3:mini", actual);
    }
}
