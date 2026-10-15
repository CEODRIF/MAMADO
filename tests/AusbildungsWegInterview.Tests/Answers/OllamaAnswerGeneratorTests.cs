using AusbildungsWegInterview.Answers;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Core.Common;
using AusbildungsWegInterview.Core.Domain;
using AusbildungsWegInterview.Tests.Fakes;
using Xunit;

namespace AusbildungsWegInterview.Tests.Answers;

public class OllamaAnswerGeneratorTests
{
    [Fact]
    public async Task Generates_Answer_WhenAvailable()
    {
        var fake = new FakeOllamaClient { Available = true, GenerateResponse = "I am strong at X." };
        var gen = new OllamaAnswerGenerator(fake, "llama3.1:8b");
        var req = new AnswerRequest("What is your strength?", null,
            new CandidateContext("I am good at X", null, "Test"), AnswerLength.Normal, "de");

        var s = await gen.GenerateAsync(req);

        Assert.Equal("I am strong at X.", s.Answer);
        Assert.Equal("llama3.1:8b", s.Model);
        Assert.False(s.IsProvisional);
        Assert.Single(fake.Prompts);
    }

    [Fact]
    public async Task Unavailable_ReturnsCautiousMessage_AndNoFabricatedAnswer()
    {
        var fake = new FakeOllamaClient { Available = false, GenerateResponse = "SHOULD_NOT_APPEAR" };
        var gen = new OllamaAnswerGenerator(fake, "llama3.1:8b");
        var req = new AnswerRequest("What is your strength?", null, CandidateContext.Empty, AnswerLength.Normal, "de");

        var s = await gen.GenerateAsync(req);

        Assert.DoesNotContain("SHOULD_NOT_APPEAR", s.Answer);
        Assert.Contains("Ollama", s.Answer);
        Assert.Empty(fake.Prompts); // never called the model
    }

    [Fact]
    public async Task MissingModel_ReturnsPullGuidance()
    {
        var fake = new FakeOllamaClient
        {
            Available = true,
            Models = Array.Empty<string>(),
            GenerateResponse = "SHOULD_NOT_APPEAR"
        };
        var gen = new OllamaAnswerGenerator(fake, "llama3.1:8b");
        var req = new AnswerRequest("Why this Ausbildung?", null, CandidateContext.Empty, AnswerLength.Normal, "de");

        var s = await gen.GenerateAsync(req);

        Assert.Contains("ollama pull", s.Answer);
        Assert.DoesNotContain("SHOULD_NOT_APPEAR", s.Answer);
        Assert.Empty(fake.Prompts);
    }

    [Fact]
    public async Task GenerateFailure_ReturnsClearMessage()
    {
        var fake = new FakeOllamaClient
        {
            Available = true,
            Models = new[] { "m" },
            GenerateException = new System.TimeoutException("request timeout")
        };
        var gen = new OllamaAnswerGenerator(fake, "m");
        var req = new AnswerRequest("Why this Ausbildung?", null, CandidateContext.Empty, AnswerLength.Normal, "de");

        var s = await gen.GenerateAsync(req);

        Assert.Contains("failed", s.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.Prompts);
    }

    [Fact]
    public async Task EmptyResponse_ReturnsClearMessage()
    {
        var fake = new FakeOllamaClient { Available = true, Models = new[] { "m" }, GenerateResponse = "" };
        var gen = new OllamaAnswerGenerator(fake, "m");
        var req = new AnswerRequest("Why this Ausbildung?", null, CandidateContext.Empty, AnswerLength.Normal, "de");

        var s = await gen.GenerateAsync(req);

        Assert.Contains("empty", s.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Prompt_IsGroundedInCvAndJobDescription()
    {
        var fake = new FakeOllamaClient { Available = true, Models = new[] { "m" }, GenerateResponse = "ok" };
        var gen = new OllamaAnswerGenerator(fake, "m");
        var req = new AnswerRequest(
            "Warum E-Commerce?", "Why e-commerce?",
            new CandidateContext("Abitur 2025, FSJ im Einzelhandel", "Online-Shop: E-Commerce-Ausbildung", "Test"),
            AnswerLength.Normal, "de");

        await gen.GenerateAsync(req);

        var prompt = Assert.Single(fake.Prompts);
        Assert.Contains("Abitur 2025, FSJ im Einzelhandel", prompt); // CV fact grounded
        Assert.Contains("Online-Shop: E-Commerce-Ausbildung", prompt); // JD grounded
    }

    [Fact]
    public void Prompt_ContainsNoFabricationGuardrails()
    {
        var prompt = ContextManager.BuildPrompt(new AnswerRequest(
            "q?", null, new CandidateContext("X", null, null), AnswerLength.Normal, "de"));

        Assert.Contains("NEVER invent", prompt);
        Assert.Contains("STRICT HONESTY RULES", prompt);
        Assert.Contains("neutral, general answer", prompt);
    }

    [Fact]
    public void Prompt_ReflectsStyle()
    {
        var concise = ContextManager.BuildPrompt(new AnswerRequest("q?", null, CandidateContext.Empty, AnswerLength.Normal, "de", AnswerStyle.Concise));
        var professional = ContextManager.BuildPrompt(new AnswerRequest("q?", null, CandidateContext.Empty, AnswerLength.Normal, "de", AnswerStyle.Professional));
        Assert.Contains("concise and direct", concise);
        Assert.Contains("professional", professional);
    }

    [Fact]
    public void Prompt_IncludesRecentConversationContext()
    {
        var prompt = ContextManager.BuildPrompt(new AnswerRequest(
            "Warum wechseln Sie?", null, CandidateContext.Empty, AnswerLength.Normal, "de",
            AnswerStyle.Natural, new[] { "Q: Wer sind Sie? | A: Ich bin Test." }));

        Assert.Contains("Recent conversation", prompt);
        Assert.Contains("Ich bin Test.", prompt);
    }

    [Fact]
    public void Prompt_WithoutMaterial_WarnsAgainstFabrication()
    {
        var prompt = ContextManager.BuildPrompt(new AnswerRequest(
            "q?", null, CandidateContext.Empty, AnswerLength.Normal, "de"));
        Assert.Contains("No candidate material was provided", prompt);
        Assert.DoesNotContain("[CV]", prompt);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var fake = new FakeOllamaClient { Available = true, Models = new[] { "m" } };
        var gen = new OllamaAnswerGenerator(fake, "m");
        var req = new AnswerRequest("q?", null, CandidateContext.Empty, AnswerLength.Normal, "de");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gen.GenerateAsync(req, cts.Token));
    }

    [Fact]
    public async Task Availability_IsReported()
    {
        var fake = new FakeOllamaClient { Available = true };
        var gen = new OllamaAnswerGenerator(fake, "m");
        await gen.RefreshAvailabilityAsync();
        Assert.True(gen.IsAvailable);
    }
}
