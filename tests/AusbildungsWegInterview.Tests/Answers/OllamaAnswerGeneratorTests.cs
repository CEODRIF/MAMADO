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

    // Phase 3: the reviewed CV/JD must reach the model as grounding material,
    // under the honesty + data-not-instructions guardrails.

    [Fact]
    public async Task Generator_SendsReviewedCvAndJd_AsGroundedData()
    {
        var fake = new FakeOllamaClient { Available = true, Models = new[] { "m" }, GenerateResponse = "ok" };
        var gen = new OllamaAnswerGenerator(fake, "m");
        var req = new AnswerRequest("Where did you work?", null,
            new CandidateContext("10 years C# at Acme", "Looking for a WPF developer", null),
            AnswerLength.Normal, "de", Level: CefrLevel.B2);

        await gen.GenerateAsync(req);

        var prompt = Assert.Single(fake.Prompts);
        Assert.Contains("10 years C# at Acme", prompt);   // reviewed CV grounded
        Assert.Contains("Looking for a WPF developer", prompt); // reviewed JD grounded
        Assert.Contains("[CV]", prompt);
        Assert.Contains("[Job description]", prompt);
        Assert.Contains("NEVER invent", prompt);          // anti-fabrication
        Assert.Contains("is DATA", prompt);               // injection guard
        Assert.Contains("CEFR B2", prompt);               // CEFR intact
    }

    [Fact]
    public async Task Generator_WithoutReviewedMaterial_SendsNoFabricationAndTemplateGuidance()
    {
        var fake = new FakeOllamaClient { Available = true, Models = new[] { "m" }, GenerateResponse = "ok" };
        var gen = new OllamaAnswerGenerator(fake, "m");
        var req = new AnswerRequest("Give a concrete example of your work.", null,
            CandidateContext.Empty, AnswerLength.Normal, "de");

        await gen.GenerateAsync(req);

        var prompt = Assert.Single(fake.Prompts);
        Assert.Contains("No candidate material was provided", prompt);
        Assert.Contains("do NOT fabricate", prompt);
        Assert.Contains("template", prompt); // missing evidence => acknowledge or marked template
    }

    // ---- Phase 4: machine status codes (localized by AnswerStatusLocalizer in the UI) ----

    [Fact]
    public async Task Success_HasStatusNone()
    {
        var fake = new FakeOllamaClient { Available = true, Models = new[] { "m" }, GenerateResponse = "ok" };
        var gen = new OllamaAnswerGenerator(fake, "m");
        var s = await gen.GenerateAsync(Req());
        Assert.Equal(AnswerStatus.None, s.Status);
    }

    [Fact]
    public async Task Unavailable_HasRuntimeUnavailableStatus_AndSkipsModel()
    {
        var fake = new FakeOllamaClient { Available = false, GenerateResponse = "NOPE" };
        var gen = new OllamaAnswerGenerator(fake, "m");
        var s = await gen.GenerateAsync(Req());
        Assert.Equal(AnswerStatus.RuntimeUnavailable, s.Status);
        Assert.Empty(fake.Prompts);
        Assert.DoesNotContain("NOPE", s.Answer);
    }

    [Fact]
    public async Task MissingModel_HasModelMissingStatus_AndCarriesModelName()
    {
        var fake = new FakeOllamaClient { Available = true, Models = Array.Empty<string>() };
        var gen = new OllamaAnswerGenerator(fake, "llama3.1:8b");
        var s = await gen.GenerateAsync(Req());
        Assert.Equal(AnswerStatus.ModelMissing, s.Status);
        Assert.Equal("llama3.1:8b", s.Model); // the localizer substitutes it into {0}
        Assert.Empty(fake.Prompts);
    }

    [Fact]
    public async Task RequestTimeout_HasGenerationTimeoutStatus()
    {
        var fake = new FakeOllamaClient
        {
            Available = true,
            Models = new[] { "m" },
            GenerateException = new AusbildungsWegInterview.Infrastructure.Ollama.OllamaRequestTimeoutException("timed out")
        };
        var gen = new OllamaAnswerGenerator(fake, "m");
        var s = await gen.GenerateAsync(Req());
        Assert.Equal(AnswerStatus.GenerationTimeout, s.Status);
        Assert.Empty(fake.Prompts);
    }

    [Fact]
    public async Task GenericFailure_HasGenerationFailedStatus()
    {
        var fake = new FakeOllamaClient
        {
            Available = true,
            Models = new[] { "m" },
            GenerateException = new System.InvalidOperationException("http 500")
        };
        var gen = new OllamaAnswerGenerator(fake, "m");
        var s = await gen.GenerateAsync(Req());
        Assert.Equal(AnswerStatus.GenerationFailed, s.Status);
    }

    [Fact]
    public async Task EmptyResponse_HasEmptyResponseStatus()
    {
        var fake = new FakeOllamaClient { Available = true, Models = new[] { "m" }, GenerateResponse = "   " };
        var gen = new OllamaAnswerGenerator(fake, "m");
        var s = await gen.GenerateAsync(Req());
        Assert.Equal(AnswerStatus.EmptyResponse, s.Status);
        Assert.False(s.HasAnswer);
    }

    // ---- Phase 4: availability re-probing during a session (no restart needed) ----

    [Fact]
    public async Task OllamaStartedMidSession_IsPickedUp_OnNextQuestion()
    {
        var fake = new FakeOllamaClient { Available = false, Models = new[] { "m" }, GenerateResponse = "I am new." };
        var gen = new OllamaAnswerGenerator(fake, "m", availabilityRecheckMs: 0); // force re-probe each generation

        var first = await gen.GenerateAsync(Req());
        Assert.Equal(AnswerStatus.RuntimeUnavailable, first.Status);

        fake.Available = true; // user started Ollama
        var second = await gen.GenerateAsync(Req());

        Assert.Equal(AnswerStatus.None, second.Status);
        Assert.Equal("I am new.", second.Answer);
        Assert.Single(fake.Prompts);
    }

    [Fact]
    public async Task ModelPulledMidSession_IsPickedUp_OnNextQuestion()
    {
        var fake = new FakeOllamaClient { Available = true, Models = Array.Empty<string>(), GenerateResponse = "ready." };
        var gen = new OllamaAnswerGenerator(fake, "m", availabilityRecheckMs: 0);

        var first = await gen.GenerateAsync(Req());
        Assert.Equal(AnswerStatus.ModelMissing, first.Status);

        fake.Models = new[] { "m" }; // user ran: ollama pull m
        var second = await gen.GenerateAsync(Req());

        Assert.Equal(AnswerStatus.None, second.Status);
        Assert.Equal("ready.", second.Answer);
        Assert.Single(fake.Prompts);
    }

    [Fact]
    public async Task Availability_IsCached_WithinRecheckInterval()
    {
        // Default recheck window (20 s): a second generation right after the
        // first must NOT re-probe (no probe storm on every partial/final).
        var fake = new FakeOllamaClient { Available = true, Models = new[] { "m" }, GenerateResponse = "ok" };
        var gen = new OllamaAnswerGenerator(fake, "m");

        await gen.GenerateAsync(Req());
        await gen.GenerateAsync(Req());

        Assert.Equal(1, fake.AvailableProbeCount);
        Assert.Equal(1, fake.ModelProbeCount);
    }

    private static AnswerRequest Req()
        => new("Why this Ausbildung?", null, CandidateContext.Empty, AnswerLength.Normal, "de");
}
