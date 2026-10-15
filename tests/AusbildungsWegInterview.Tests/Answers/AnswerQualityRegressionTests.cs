using System.Diagnostics;
using AusbildungsWegInterview.Answers;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Common;
using AusbildungsWegInterview.Core.Config;
using AusbildungsWegInterview.Core.Domain;
using AusbildungsWegInterview.Core.Pipeline;
using AusbildungsWegInterview.Core.Speech;
using AusbildungsWegInterview.Tests.Fakes;
using AusbildungsWegInterview.Translation;
using Xunit;

namespace AusbildungsWegInterview.Tests.Answers;

/// <summary>
/// Phase 3 regressions: answer relevance (question-specific prompts),
/// language/length respect, bounded prompts, honest translation hand-off to the
/// answer stage, and bounded recent-conversation context.
/// </summary>
public class AnswerQualityRegressionTests
{
    // ---------- prompt construction (deterministic) ----------

    [Fact]
    public void DifferentQuestions_ProduceDifferentQuestionSpecificPrompts()
    {
        var ctx = new CandidateContext("Abitur 2025", "E-Commerce Ausbildung", null);
        var p1 = ContextManager.BuildPrompt(new AnswerRequest("Warum möchten Sie eine Ausbildung?", null, ctx, AnswerLength.Normal, "de"));
        var p2 = ContextManager.BuildPrompt(new AnswerRequest("What is your biggest weakness?", null, ctx, AnswerLength.Normal, "de"));

        Assert.NotEqual(p1, p2);
        Assert.Contains("Warum möchten Sie eine Ausbildung?", p1);
        Assert.DoesNotContain("Warum möchten Sie eine Ausbildung?", p2);
        Assert.Contains("What is your biggest weakness?", p2);
    }

    [Theory]
    [InlineData("de", AnswerLength.Normal, "German", "3-4 sentences")]
    [InlineData("fr", AnswerLength.Concise, "French", "1-2 short sentences")]
    [InlineData("en", AnswerLength.Detailed, "English", "short structured answer")]
    public void Prompt_RespectsAnswerLanguageAndLength(string lang, AnswerLength length, string langName, string lengthHint)
    {
        var p = ContextManager.BuildPrompt(new AnswerRequest("q?", null, CandidateContext.Empty, length, lang));
        Assert.Contains(langName, p);
        Assert.Contains(lengthHint, p);
    }

    [Fact]
    public void Prompt_BoundsOversizedMaterial()
    {
        var hugeCv = new string('x', 10_000);
        var hugeJd = new string('y', 10_000);
        var p = ContextManager.BuildPrompt(new AnswerRequest(
            "q?", null, new CandidateContext(hugeCv, hugeJd, null), AnswerLength.Normal, "de"));

        // Truncation caps: CV 4000 + JD 3000 + prompt scaffolding — far below the raw input.
        Assert.True(p.Length < 8500, $"Prompt must stay bounded, got {p.Length} chars.");
        Assert.Contains("…", p);
    }

    [Fact]
    public void Prompt_WithRecentContext_ContainsIt()
    {
        var p = ContextManager.BuildPrompt(new AnswerRequest(
            "Warum wechseln Sie?", null, CandidateContext.Empty, AnswerLength.Normal, "de",
            AnswerStyle.Natural, new[] { "Q: Wer sind Sie? | A: Ich bin Test." }));
        Assert.Contains("Ich bin Test.", p);
    }

    // ---------- pipeline hand-off: translation + context ----------

    private static AudioFrame NonSilent(int bytes)
    {
        var b = new byte[bytes];
        for (int i = 0; i < bytes; i += 2) { b[i] = 0; b[i + 1] = 0x20; }
        return new AudioFrame(b, 16000);
    }

    [Fact]
    public async Task AnswerRequest_ContainsLatestGenuineTranslation()
    {
        var translator = new MockTranslator();
        var answers = new MockAnswerGenerator();
        var speech = new ScriptedSpeechRecognizer(TranscriptionResult.Partial("Warum möchten Sie zu uns?"));
        await using var pipeline = new InterviewPipeline(
            new NoopAudioCapture(), speech, translator, answers, new QuestionDetector(),
            new AppConfiguration { ChunkSeconds = 0.5, TargetSampleRate = 16000, MinQuestionChars = 8 });

        await pipeline.StartAsync();
        pipeline.EnqueueAudio(NonSilent(16000));
        await WaitUntilAsync(() => { lock (answers.Requests) return answers.Requests.Count >= 1; });
        await pipeline.StopAsync();

        AnswerRequest req;
        lock (answers.Requests) req = answers.Requests[0];
        Assert.NotNull(req.QuestionTranslated);
        Assert.Contains("Warum möchten Sie zu uns?", req.QuestionTranslated);
    }

    [Fact]
    public async Task AnswerRequest_DoesNotTreatPassThroughAsTranslation()
    {
        var answers = new MockAnswerGenerator();
        var speech = new ScriptedSpeechRecognizer(TranscriptionResult.Partial("Warum möchten Sie zu uns?"));
        await using var pipeline = new InterviewPipeline(
            new NoopAudioCapture(), speech, new PassThroughTranslator(), answers, new QuestionDetector(),
            new AppConfiguration { ChunkSeconds = 0.5, TargetSampleRate = 16000, MinQuestionChars = 8 });

        await pipeline.StartAsync();
        pipeline.EnqueueAudio(NonSilent(16000));
        await WaitUntilAsync(() => { lock (answers.Requests) return answers.Requests.Count >= 1; });
        await pipeline.StopAsync();

        AnswerRequest req;
        lock (answers.Requests) req = answers.Requests[0];
        // Pass-through text is not a translation and must not be handed to the generator as one.
        Assert.Null(req.QuestionTranslated);
    }

    [Fact]
    public async Task AnswerRequest_CarriesConfiguredCefrLevel()
    {
        var answers = new MockAnswerGenerator();
        var speech = new ScriptedSpeechRecognizer(TranscriptionResult.Partial("Warum möchten Sie zu uns?"));
        var config = new AppConfiguration
        {
            ChunkSeconds = 0.5,
            TargetSampleRate = 16000,
            MinQuestionChars = 8,
            AnswerLevel = AusbildungsWegInterview.Core.Common.CefrLevel.C2
        };
        await using var pipeline = new InterviewPipeline(
            new NoopAudioCapture(), speech, new PassThroughTranslator(), answers, new QuestionDetector(), config);

        await pipeline.StartAsync();
        pipeline.EnqueueAudio(NonSilent(16000));
        await WaitUntilAsync(() => { lock (answers.Requests) return answers.Requests.Count >= 1; });
        await pipeline.StopAsync();

        AnswerRequest req;
        lock (answers.Requests) req = answers.Requests[0];
        Assert.Equal(AusbildungsWegInterview.Core.Common.CefrLevel.C2, req.Level);
    }

    [Fact]
    public async Task RecentTurns_BoundedToConfiguredCount_AndOrderPreserved()
    {
        var answers = new MockAnswerGenerator();
        var speech = new ScriptedSpeechRecognizer(
            TranscriptionResult.Final("What is your name?"),
            TranscriptionResult.Final("Why do you want to join us?"),
            TranscriptionResult.Final("What are your strengths?"),
            TranscriptionResult.Final("When can you start?"));
        var config = new AppConfiguration
        {
            ChunkSeconds = 0.5,
            TargetSampleRate = 16000,
            MinQuestionChars = 8,
            RecentTurns = 3
        };
        await using var pipeline = new InterviewPipeline(
            new NoopAudioCapture(), speech, new MockTranslator(), answers, new QuestionDetector(), config);

        await pipeline.StartAsync();
        // Four 0.5 s chunks → four finalized questions in sequence.
        for (int i = 0; i < 4; i++)
            pipeline.EnqueueAudio(NonSilent(16000));

        await WaitUntilAsync(() => { lock (answers.Requests) return answers.Requests.Count >= 4; });
        await pipeline.StopAsync();

        List<AnswerRequest> reqs;
        lock (answers.Requests) reqs = answers.Requests.ToList();

        Assert.Equal(4, reqs.Count);
        Assert.False(reqs[0].HasRecentContext);
        Assert.Single(reqs[1].RecentContext!);
        // q4's context snapshot (taken before generating q4) holds exactly q1..q3.
        var ctx3 = reqs[3].RecentContext!;
        Assert.Equal(3, ctx3.Count);
        Assert.Contains(ctx3, t => t.Contains("What is your name?"));
        Assert.Contains(ctx3, t => t.Contains("Why do you want to join us?"));
        Assert.Contains(ctx3, t => t.Contains("What are your strengths?"));
        Assert.DoesNotContain(ctx3, t => t.Contains("When can you start?")); // a question is never in its own context
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.Fail($"Timeout ({timeoutMs} ms).");
    }
}
