using AusbildungsWegInterview.Core.Common;
using Xunit;

namespace AusbildungsWegInterview.Tests.Core;

/// <summary>
/// The MAMADO shell renders one panel per configured language, so the settings
/// string must never produce blank, duplicated or unreachable panels.
/// </summary>
public class LanguageSetTests
{
    [Fact]
    public void Parse_OfBlank_IsEmpty()
    {
        Assert.Empty(LanguageSet.Parse(null));
        Assert.Empty(LanguageSet.Parse(""));
        Assert.Empty(LanguageSet.Parse("   "));
        Assert.Empty(LanguageSet.Parse(",;,"));
    }

    [Fact]
    public void Parse_NormalizesAndDeduplicates()
    {
        Assert.Equal(new[] { "en", "ar" }, LanguageSet.Parse("EN, ar ;  EN"));
        Assert.Equal(new[] { "en", "ar" }, LanguageSet.Parse("en,ar,en,ar"));
    }

    [Fact]
    public void Parse_AppendsRequiredLanguagesThatAreMissing()
    {
        // e.g. the configured primary target must always have a panel.
        Assert.Equal(new[] { "ar", "en" }, LanguageSet.Parse("ar", "en"));
        Assert.Equal(new[] { "en", "ar" }, LanguageSet.Parse("en,ar", "en"));
    }

    [Fact]
    public void WithPrimaryFirst_MovesTheGeneratedLanguageToTheFront()
    {
        Assert.Equal(new[] { "de", "en", "ar" }, LanguageSet.WithPrimaryFirst(new[] { "en", "ar", "de" }, "de"));
        Assert.Equal(new[] { "de", "en", "ar" }, LanguageSet.WithPrimaryFirst(new[] { "de", "en", "ar" }, "de"));
        // A primary that is not in the list still gets its panel (prepended),
        // so the generated language can never be missing from the answer trio.
        Assert.Equal(new[] { "fr", "en", "ar" }, LanguageSet.WithPrimaryFirst(new[] { "en", "ar" }, "FR"));
        Assert.Equal(new[] { "en", "ar" }, LanguageSet.WithPrimaryFirst(new[] { "en", "ar" }, null));
    }

    [Fact]
    public void Normalize_TrimsAndLowers()
    {
        Assert.Equal("en", LanguageSet.Normalize(" EN "));
        Assert.Null(LanguageSet.Normalize("  "));
        Assert.Null(LanguageSet.Normalize(null));
    }
}
