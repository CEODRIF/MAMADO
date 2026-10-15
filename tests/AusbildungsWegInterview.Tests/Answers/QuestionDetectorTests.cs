using AusbildungsWegInterview.Answers;
using Xunit;

namespace AusbildungsWegInterview.Tests.Answers;

public class QuestionDetectorTests
{
    private static QuestionDetector D => new();

    [Theory]
    [InlineData("What is your greatest strength?", true)]
    [InlineData("Warum wollen Sie zu uns?", true)]
    [InlineData("Pourquoi postulez-vous ?", true)]
    [InlineData("What is your", true)]
    [InlineData("Warum sind Sie motiviert", true)]
    [InlineData("You are welcome to apply", false)]
    [InlineData("", false)]
    [InlineData("Hi", false)]
    public void DetectsQuestions(string text, bool expected)
        => Assert.Equal(expected, D.IsSufficient(text, 4, isFinal: false));

    [Fact]
    public void Final_LongEnough_IsSufficient()
        => Assert.True(D.IsSufficient("Tell me about yourself", 8, isFinal: true));

    [Fact]
    public void Final_BelowMinChars_NotSufficient()
        => Assert.False(D.IsSufficient("Hi?", 8, isFinal: true));

    [Fact]
    public void Whitespace_NeverSufficient()
        => Assert.False(D.IsSufficient("   ", 1, isFinal: true));
}
