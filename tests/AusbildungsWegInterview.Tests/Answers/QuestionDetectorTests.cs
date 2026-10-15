using AusbildungsWegInterview.Answers;
using Xunit;

namespace AusbildungsWegInterview.Tests.Answers;

public class QuestionDetectorTests
{
    private static QuestionDetector D => new();

    [Theory]
    // English
    [InlineData("What is your greatest strength?", true)]
    [InlineData("Could you tell me about yourself", true)]
    [InlineData("Which projects have you worked on", true)]
    [InlineData("Why do you want to join us?", true)]
    // German
    [InlineData("Warum wollen Sie zu uns?", true)]
    [InlineData("Weshalb möchten Sie diese Ausbildung?", true)]
    [InlineData("Erzählen Sie von sich", true)]
    [InlineData("Können Sie mir von Ihren Erfahrungen berichten", true)]
    [InlineData("Welche Stärken haben Sie", true)]
    [InlineData("Wie sind Sie auf uns aufmerksam geworden", true)]
    // French
    [InlineData("Pourquoi postulez-vous ?", true)]
    [InlineData("Qu'est-ce que vous savez faire", true)]
    [InlineData("Pouvez-vous vous présenter", true)]
    [InlineData("Comment vous appelez-vous", true)]
    // Arabic
    [InlineData("لماذا تريد هذا التدريب؟", true)]
    [InlineData("كيف تشعر عن العمل في فريق", true)]
    [InlineData("هل يمكنك أن تتحدث عن نفسك؟", true)]
    [InlineData("ماذا درست سابقاً", true)]
    [InlineData("ما هي نقاط قوتك", true)]
    public void DetectsQuestions_Multilingual(string text, bool expected)
        => Assert.Equal(expected, D.IsSufficient(text, 4, isFinal: false));

    [Theory]
    [InlineData("What is your", true)]            // incomplete but interrogative -> provisional ok
    [InlineData("Warum sind Sie motiviert", true)]
    [InlineData("Merci", false)]                   // below minChars
    [InlineData("Ich freue mich", false)]          // no interrogative cue, provisional
    public void DetectsProvisionalQuestions(string text, bool expected)
        => Assert.Equal(expected, D.IsSufficient(text, 8, isFinal: false));

    [Theory]
    [InlineData("You are welcome to apply")]     // ordinary statement, provisional
    [InlineData("Ich bin sehr motiviert.")]
    [InlineData("Merci beaucoup pour votre temps")]
    public void Statements_NotSufficientWhenProvisional(string text)
        => Assert.False(D.IsSufficient(text, 8, isFinal: false));

    [Fact]
    public void Statement_FinalizedAndLongEnough_IsSufficient()
        => Assert.True(D.IsSufficient("Ich bin sehr motiviert und lerne schnell.", 8, isFinal: true));

    [Fact]
    public void Final_LongEnough_IsSufficient()
        => Assert.True(D.IsSufficient("Tell me about yourself", 8, isFinal: true));

    [Fact]
    public void Final_BelowMinChars_NotSufficient()
        => Assert.False(D.IsSufficient("Hi?", 8, isFinal: true));

    [Fact]
    public void ArabicQuestionMark_IsDetected()
        => Assert.True(D.IsSufficient("كيف حالك يا صديقي؟", 4, isFinal: false));

    [Fact]
    public void Whitespace_NeverSufficient()
        => Assert.False(D.IsSufficient("   ", 1, isFinal: true));

    [Fact]
    public void Empty_NeverSufficient()
        => Assert.False(D.IsSufficient("", 1, isFinal: false));
}
