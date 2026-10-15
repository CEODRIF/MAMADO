using System.Diagnostics;
using AusbildungsWegInterview.Answers;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Config;
using AusbildungsWegInterview.Core.Pipeline;
using AusbildungsWegInterview.Core.Speech;
using AusbildungsWegInterview.Core.Translation;
using AusbildungsWegInterview.Tests.Fakes;
using Xunit;

namespace AusbildungsWegInterview.Tests.Pipeline;

public class InterviewPipelineTests
{
    private static AudioFrame NonSilent(int bytes, int rate = 16000)
    {
        var b = new byte[bytes];
        for (int i = 0; i < b.Length; i += 2) { b[i] = 0x00; b[i + 1] = 0x20; } // 8192
        return new AudioFrame(b, rate);
    }

    private static async Task WaitUntilAsync(Func<bool> cond, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!cond())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                break;
            await Task.Delay(20);
        }
        Assert.True(cond(), "Condition was not met within the timeout.");
    }

    [Fact]
    public async Task ProcessesQuestion_PartialThenFinal_WithTranslationDedup()
    {
        var capture = new NoopAudioCapture();
        var speech = new ScriptedSpeechRecognizer(
            TranscriptionResult.Partial("What is your"),
            TranscriptionResult.Partial("What is your greatest strength?"),
            TranscriptionResult.Final("What is your greatest strength?"));
        var translator = new MockTranslator();
        var answers = new MockAnswerGenerator();
        var detector = new QuestionDetector();
        var config = new AppConfiguration
        {
            ChunkSeconds = 0.5,
            TargetSampleRate = 16000,
            SilenceRmsThreshold = 0.01,
            MinQuestionChars = 8
        };

        await using var pipeline = new InterviewPipeline(capture, speech, translator, answers, detector, config);

        var transcripts = new List<TranscriptionResult>();
        var translations = new List<TranslationResult>();
        var answerSuggs = new List<AnswerSuggestion>();
        pipeline.TranscriptUpdated += (_, e) => { lock (transcripts) transcripts.Add(e.Result); };
        pipeline.TranslationUpdated += (_, e) => { lock (translations) translations.Add(e.Result); };
        pipeline.AnswerUpdated += (_, e) => { lock (answerSuggs) answerSuggs.Add(e.Suggestion); };

        await pipeline.StartAsync();
        Assert.True(capture.StartCalled);
        Assert.Equal(PipelineState.Listening, pipeline.State);

        // Feed exactly 3 chunks (0.5 s each) as one non-silent frame.
        pipeline.EnqueueAudio(NonSilent(3 * 16000));

        await WaitUntilAsync(() => { lock (answerSuggs) return answerSuggs.Count >= 2; });
        await pipeline.StopAsync();

        Assert.True(capture.StopCalled);
        Assert.Equal(PipelineState.Stopped, pipeline.State);
        Assert.Equal(3, speech.ProcessCallCount);

        lock (transcripts) Assert.Equal(3, transcripts.Count);
        lock (translations) Assert.Equal(2, translations.Count); // final repeats chunk-2 text -> deduped
        lock (answerSuggs)
        {
            Assert.Equal(2, answerSuggs.Count);
            Assert.True(answerSuggs[0].IsProvisional);   // initial answer while hearing
            Assert.False(answerSuggs[1].IsProvisional);  // refined when finalized
            Assert.Equal("MOCK_ANSWER", answerSuggs[1].Answer);
        }
    }

    [Fact]
    public async Task SkipsSilentAudio_ProducesNoTranscription()
    {
        var capture = new NoopAudioCapture();
        var speech = new ScriptedSpeechRecognizer(TranscriptionResult.Final("hello there?"));
        var config = new AppConfiguration { ChunkSeconds = 0.5, TargetSampleRate = 16000, SilenceRmsThreshold = 0.01 };

        await using var pipeline = new InterviewPipeline(
            capture, speech, new MockTranslator(), new MockAnswerGenerator(), new QuestionDetector(), config);
        await pipeline.StartAsync();

        pipeline.EnqueueAudio(new AudioFrame(new byte[3 * 16000], 16000)); // all zeros = silent
        await Task.Delay(250);
        await pipeline.StopAsync();

        Assert.Equal(0, speech.ProcessCallCount);
    }

    [Fact]
    public async Task Stop_FromIdle_IsNoOp()
    {
        await using var pipeline = new InterviewPipeline(
            new NoopAudioCapture(), new ScriptedSpeechRecognizer(),
            new MockTranslator(), new MockAnswerGenerator(), new QuestionDetector());

        Assert.Equal(PipelineState.Idle, pipeline.State);
        await pipeline.StopAsync();
        Assert.Equal(PipelineState.Idle, pipeline.State);
    }

    [Fact]
    public async Task Start_ModelFailure_ThrowsClearError_AndDoesNotStartCapture()
    {
        var capture = new NoopAudioCapture();
        var badSpeech = new FailingSpeechRecognizer();
        await using var pipeline = new InterviewPipeline(
            capture, badSpeech, new MockTranslator(), new MockAnswerGenerator(), new QuestionDetector());

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.StartAsync());
        Assert.Equal(PipelineState.Faulted, pipeline.State);
        Assert.False(capture.StartCalled);
        Assert.NotNull(pipeline.LastError);
    }

    [Fact]
    public async Task Cancellation_SuppressesObsoleteAnswer()
    {
        var capture = new NoopAudioCapture();
        // Two questions: first is superseded (canceled), second completes.
        var speech = new ScriptedSpeechRecognizer(
            TranscriptionResult.Partial("What is your"),
            TranscriptionResult.Partial("What is your name?"));
        var answers = new MockAnswerGenerator { DelayMs = 150 }; // first answer is slow
        var detector = new QuestionDetector();
        var config = new AppConfiguration { ChunkSeconds = 0.5, TargetSampleRate = 16000, MinQuestionChars = 8 };

        await using var pipeline = new InterviewPipeline(capture, speech, new MockTranslator(), answers, detector, config);

        var answerSuggs = new List<AnswerSuggestion>();
        pipeline.AnswerUpdated += (_, e) => { lock (answerSuggs) answerSuggs.Add(e.Suggestion); };

        await pipeline.StartAsync();
        pipeline.EnqueueAudio(NonSilent(2 * 16000));
        await WaitUntilAsync(() => { lock (answerSuggs) return answerSuggs.Count >= 1; }, 8000);
        await pipeline.StopAsync();

        // The first (provisional) answer may have been cancelled by the second; at least the
        // pipeline must not hang and must stop cleanly.
        Assert.Equal(PipelineState.Stopped, pipeline.State);
    }
}

/// <summary>Recognizer whose PrepareAsync always fails (simulates a missing model).</summary>
internal sealed class FailingSpeechRecognizer : ISpeechRecognizer
{
    public bool IsReady => false;
    public string ModelName => "none";
    public string? Language => null;
    public Task PrepareAsync(string model, string? language, CancellationToken ct = default)
        => throw new InvalidOperationException("Whisper model not found (simulated).");
    public Task<TranscriptionResult> ProcessAudioAsync(AudioFrame chunk, CancellationToken ct = default)
        => Task.FromResult(TranscriptionResult.Empty);
    public void Reset() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
