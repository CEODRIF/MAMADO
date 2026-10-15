using AusbildungsWegInterview.Core.Speech;
using Xunit;

namespace AusbildungsWegInterview.Tests.Core;

public class TranscriptionResultTests
{
    [Fact]
    public void Partial_IsProvisional()
    {
        var p = TranscriptionResult.Partial("  hello    world ");
        Assert.Equal("hello world", p.Text);
        Assert.True(p.IsProvisional);
        Assert.False(p.IsFinal);
    }

    [Fact]
    public void Final_IsFinalized()
    {
        var f = TranscriptionResult.Final("hi there", "de");
        Assert.True(f.IsFinal);
        Assert.Equal("de", f.DetectedLanguage);
    }

    [Fact]
    public void Empty_HasNoText()
        => Assert.True(string.IsNullOrWhiteSpace(TranscriptionResult.Empty.Text));
}
