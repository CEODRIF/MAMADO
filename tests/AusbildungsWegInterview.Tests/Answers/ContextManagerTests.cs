using AusbildungsWegInterview.Answers;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Core.Common;
using AusbildungsWegInterview.Core.Domain;
using Xunit;

namespace AusbildungsWegInterview.Tests.Answers;

public class ContextManagerTests
{
    [Fact]
    public void Prompt_IncludesContextAndGuardrails()
    {
        var req = new AnswerRequest(
            "What is your strength?",
            "What is your strength?",
            new CandidateContext("Strong in C# and teamwork", "Looking for a developer", "Ana"),
            AnswerLength.Detailed,
            "de");

        var prompt = ContextManager.BuildPrompt(req);
        Assert.Contains("Strong in C# and teamwork", prompt);
        Assert.Contains("Looking for a developer", prompt);
        Assert.Contains("do NOT invent", prompt);          // no-fabrication guardrail
        Assert.Contains("German", prompt);                 // answer language
        Assert.Contains("[CV]", prompt);
    }

    [Fact]
    public void Prompt_NoContext_StatesGeneralAndNoFabrication()
    {
        var req = new AnswerRequest("Why you?", null, CandidateContext.Empty, AnswerLength.Concise, "en");
        var prompt = ContextManager.BuildPrompt(req);
        Assert.Contains("No candidate material was provided", prompt);
        Assert.DoesNotContain("[CV]", prompt);
    }

    [Fact]
    public void Prompt_ReflectsLengthInstruction()
    {
        var concise = ContextManager.BuildPrompt(new AnswerRequest("q?", null, CandidateContext.Empty, AnswerLength.Concise, "de"));
        var detailed = ContextManager.BuildPrompt(new AnswerRequest("q?", null, CandidateContext.Empty, AnswerLength.Detailed, "de"));
        Assert.Contains("1-2 short sentences", concise);
        Assert.Contains("structured answer", detailed);
    }

    [Fact]
    public void Prompt_TruncatesVeryLongCv()
    {
        var longCv = new string('a', 10000);
        var req = new AnswerRequest("q?", null, new CandidateContext(longCv, null, null), AnswerLength.Normal, "de");
        var prompt = ContextManager.BuildPrompt(req);
        Assert.True(prompt.Length < longCv.Length); // truncated
        Assert.Contains("…", prompt);
    }
}
