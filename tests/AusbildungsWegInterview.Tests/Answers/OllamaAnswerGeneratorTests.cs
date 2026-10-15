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
    public async Task Availability_IsReported()
    {
        var fake = new FakeOllamaClient { Available = true };
        var gen = new OllamaAnswerGenerator(fake, "m");
        await gen.RefreshAvailabilityAsync();
        Assert.True(gen.IsAvailable);
    }
}
