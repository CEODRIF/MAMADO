using AusbildungsWegInterview.App;
using Xunit;

namespace AusbildungsWegInterview.Tests.Composition;

/// <summary>
/// Access-code input behavior: normalization (letters/digits only, upper-cased,
/// capped at 16), four-group auto-formatting for typing AND pasting, and
/// completion state. The formatter must be idempotent so a WPF binding that
/// writes the formatted value back in cannot re-trigger reformats.
/// </summary>
public class AccessCodeFormatterTests
{
    [Theory]
    [InlineData("abcd", "ABCD")]
    [InlineData("abcd1234", "ABCD-1234")]
    [InlineData("a b c d - 1 2 3 4", "ABCD-1234")]
    [InlineData("ABCD-1234-EFGH-5678", "ABCD-1234-EFGH-5678")]
    [InlineData("abcd1234efgh5678", "ABCD-1234-EFGH-5678")]
    [InlineData("ABCD-1234-EFGH-5678-EXTRA", "ABCD-1234-EFGH-5678")]
    [InlineData("!!!@@@###", "")]
    [InlineData("a1b2c3d4e5f6g7h8i9", "A1B2-C3D4-E5F6-G7H8")]
    public void FormatInput_NormalizesAndGroups(string input, string expected)
        => Assert.Equal(expected, AccessCodeFormatter.FormatInput(input));

    [Fact]
    public void Normalize_StripsSeparatorsSymbols_AndCapsAt16()
    {
        Assert.Equal("ABCD1234EFGH5678", AccessCodeFormatter.Normalize("ABCD-1234-EFGH-5678"));
        Assert.Equal("A1B2C3D4E5F6G7H8", AccessCodeFormatter.Normalize("a1 b2!c3@d4#e5$f6%g7&h8"));
        Assert.Equal("ABCDEFGHIJKLMNOP", AccessCodeFormatter.Normalize("abcdefghijklmnopqrstuvwxyz"));
        Assert.Equal(string.Empty, AccessCodeFormatter.Normalize(null));
        Assert.Equal(string.Empty, AccessCodeFormatter.Normalize("   "));
    }

    [Fact]
    public void Format_IsIdempotent_ForAlreadyFormattedInput()
    {
        // A WPF binding writes the formatted value back on every change:
        // re-formatting it must be a no-op (no caret thrash, no state flip).
        string once = AccessCodeFormatter.FormatInput("abcd1234efgh5678");
        Assert.Equal(once, AccessCodeFormatter.FormatInput(once));
        Assert.Equal("ABCD-1", AccessCodeFormatter.FormatInput("ABCD-1"));
    }

    [Theory]
    [InlineData(null, AccessCodeState.Empty)]
    [InlineData("", AccessCodeState.Empty)]
    [InlineData("----", AccessCodeState.Empty)]
    [InlineData("A", AccessCodeState.Partial)]
    [InlineData("ABCD-1234", AccessCodeState.Partial)]
    [InlineData("ABCD-1234-EFGH-567", AccessCodeState.Partial)]
    [InlineData("ABCD-1234-EFGH-5678", AccessCodeState.Valid)]
    public void State_ReflectsCompletion(string? input, AccessCodeState expected)
        => Assert.Equal(expected, AccessCodeFormatter.State(input));

    [Theory]
    [InlineData("ABCD-1234-EFGH-5678", true)]
    [InlineData("ABCD1234EFGH5678", false)]
    [InlineData("ABCD_1234-EFGH-5678", false)]
    [InlineData("abcd-1234-EFGH-5678", false)]
    [InlineData("ABCD-1234-EFGH-567", false)]
    [InlineData("ABCD-1234-EFGH-56789", false)]
    [InlineData("", false)]
    public void IsValidDisplayFormat_StrictCanonicalForm(string? text, bool expected)
        => Assert.Equal(expected, AccessCodeFormatter.IsValidDisplayFormat(text));

    [Fact]
    public void Length_IgnoresFormatting()
    {
        Assert.Equal(0, AccessCodeFormatter.Length(null));
        Assert.Equal(4, AccessCodeFormatter.Length("ABCD"));
        Assert.Equal(4, AccessCodeFormatter.Length("ABCD-"));
        Assert.Equal(16, AccessCodeFormatter.Length("ABCD-1234-EFGH-5678"));
        Assert.Equal(16, AccessCodeFormatter.Length("ABCD-1234-EFGH-5678-more"));
    }
}
