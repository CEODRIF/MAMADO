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
        Assert.Contains("NEVER invent", prompt);           // no-fabrication guardrail
        Assert.Contains("German", prompt);                 // answer language
        Assert.Contains("[CV]", prompt);
    }

    [Fact]
    public void Prompt_DefaultsToCefrLevelB1()
    {
        var req = new AnswerRequest("Why you?", null, CandidateContext.Empty, AnswerLength.Normal, "de");
        var prompt = ContextManager.BuildPrompt(req);
        Assert.Contains("CEFR B1", prompt);
    }

    [Theory]
    [InlineData(CefrLevel.A1, "CEFR A1")]
    [InlineData(CefrLevel.A2, "CEFR A2")]
    [InlineData(CefrLevel.B2, "CEFR B2")]
    [InlineData(CefrLevel.C1, "CEFR C1")]
    [InlineData(CefrLevel.C2, "CEFR C2")]
    public void Prompt_ReflectsSelectedCefrLevel(CefrLevel level, string label)
    {
        var req = new AnswerRequest("Why you?", null, CandidateContext.Empty, AnswerLength.Normal, "de", Level: level);
        var prompt = ContextManager.BuildPrompt(req);
        Assert.Contains(label, prompt);
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
