using AusbildungsWegInterview.Core.Common;
using Xunit;

namespace AusbildungsWegInterview.Tests.Core;

public class LanguageNamesTests
{
    [Theory]
    [InlineData("de", "German")]
    [InlineData("en", "English")]
    [InlineData("fr", "French")]
    [InlineData("ar", "Arabic")]
    public void MapsKnownCodes(string code, string expected)
        => Assert.Equal(expected, LanguageNames.DisplayName(code));

    [Fact]
    public void UnknownCode_ReturnsTheCode()
        => Assert.Equal("xx", LanguageNames.DisplayName("xx"));

    [Fact]
    public void Empty_ReturnsSameLanguageHint()
        => Assert.Contains("same language", LanguageNames.DisplayName(""));
}
