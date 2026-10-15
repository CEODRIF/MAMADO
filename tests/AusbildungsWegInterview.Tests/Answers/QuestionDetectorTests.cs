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
    public void Statement_Finalized_WithoutInterrogativeCue_IsNotAQuestion()
        => Assert.False(D.IsSufficient("Ich bin sehr motiviert und lerne schnell.", 8, isFinal: true));

    [Fact]
    public void Final_LongEnough_IsSufficient()
        => Assert.True(D.IsSufficient("Tell me about yourself", 8, isFinal: true));

    // Phase 2: finalized segments must carry an interrogative cue. The old
    // "final + long enough = question" behavior answered the interviewer's
    // statements and fillers, which is exactly what this must not do.
    [Theory]
    [InlineData("Okay, super, weiter.")]
    [InlineData("Ja genau, das klingt gut.")]
    [InlineData("Ich bin sehr motiviert und lerne schnell.")]
    [InlineData("Thank you for your time today.")]
    [InlineData("Merci beaucoup pour votre disponibilité.")]
    [InlineData("شكراً جزيلاً على وقتك.")]
    public void FinalizedStatementsAndFillers_AreNotQuestions(string text)
        => Assert.False(D.IsSufficient(text, 8, isFinal: true));

    // Imperative / no-interrogative-word interview questions in the four
    // supported languages must still be recognized (recall, not just precision).
    [Theory]
    [InlineData("Sagen Sie mir von Ihrer letzten Erfahrung")]
    [InlineData("Erzaehlen sie mir von einem Projekt")]
    [InlineData("Beschreiben Sie eine schwierige Situation")]
    [InlineData("Where do you see yourself in five years")]
    [InlineData("Describe your last project")]
    [InlineData("Walk me through your experience")]
    [InlineData("Parlez de votre dernier projet")]
    [InlineData("Decrivez une situation difficile")]
    [InlineData("Où vous voyez-vous dans cinq ans")]
    [InlineData("حدثني عن نفسك")]
    [InlineData("اشرح لي مهارتك")]
    [InlineData("أخبرني عن مشروع عملت عليه")]
    public void ImperativeQuestions_StillDetected(string text)
        => Assert.True(D.IsSufficient(text, 8, isFinal: true), text);

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
