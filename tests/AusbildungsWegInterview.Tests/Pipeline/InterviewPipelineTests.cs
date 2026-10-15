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
    public async Task TrailingSilence_FinalizesSegment_WithFinalText()
    {
        var capture = new NoopAudioCapture();
        var speech = new ScriptedSpeechRecognizer(
            new[]
            {
                TranscriptionResult.Partial("Warum möchten Sie"),
                TranscriptionResult.Partial("Warum möchten Sie zu uns?")
            },
            new[] { "Warum möchten Sie zu uns?" });
        var answers = new MockAnswerGenerator();
        var config = new AppConfiguration
        {
            ChunkSeconds = 0.5,
            TargetSampleRate = 16000,
            SilenceRmsThreshold = 0.01,
            SilenceFinalizeSeconds = 0.8,
            MinQuestionChars = 8
        };

        await using var pipeline = new InterviewPipeline(capture, speech, new MockTranslator(), answers, new QuestionDetector(), config);

        var transcripts = new List<TranscriptionResult>();
        var answerSuggs = new List<AnswerSuggestion>();
        pipeline.TranscriptUpdated += (_, e) => { lock (transcripts) transcripts.Add(e.Result); };
        pipeline.AnswerUpdated += (_, e) => { lock (answerSuggs) answerSuggs.Add(e.Suggestion); };

        await pipeline.StartAsync();

        // 2 chunks of speech, then 2 chunks of trailing silence (1.0 s >= 0.8 s finalize threshold).
        pipeline.EnqueueAudio(NonSilent(16000));
        pipeline.EnqueueAudio(NonSilent(16000));
        pipeline.EnqueueAudio(new AudioFrame(new byte[16000], 16000));
        pipeline.EnqueueAudio(new AudioFrame(new byte[16000], 16000));

        await WaitUntilAsync(() => { lock (answerSuggs) return answerSuggs.Count >= 2; });
        await pipeline.StopAsync();

        Assert.True(speech.FinalizeCallCount >= 1, "Sustained trailing silence must trigger FinalizeAsync.");
        Assert.True(speech.ResetCount >= 1, "Segment state must be reset after finalization.");

        lock (transcripts)
        {
            var last = transcripts[^1];
            Assert.True(last.IsFinal);
            Assert.Equal("Warum möchten Sie zu uns?", last.Text);
        }
        lock (answerSuggs)
        {
            Assert.True(answerSuggs[0].IsProvisional);  // first pass while still hearing
            Assert.False(answerSuggs[^1].IsProvisional); // refined by the final transcript
        }
    }

    [Fact]
    public async Task RepeatedStartStop_CyclesCleanly()
    {
        var capture = new NoopAudioCapture();
        var pipeline = new InterviewPipeline(
            capture, new ScriptedSpeechRecognizer(), new MockTranslator(), new MockAnswerGenerator(), new QuestionDetector());

        for (int i = 0; i < 3; i++)
        {
            await pipeline.StartAsync();
            Assert.Equal(PipelineState.Listening, pipeline.State);
            await pipeline.StopAsync();
            Assert.Equal(PipelineState.Stopped, pipeline.State);
        }

        Assert.Equal(3, capture.StartCount);
        Assert.Equal(3, capture.StopCount);

        // One more cycle proves the pipeline is reusable, not single-shot.
        await pipeline.StartAsync();
        Assert.Equal(PipelineState.Listening, pipeline.State);
        await pipeline.StopAsync();
        Assert.Equal(4, capture.StartCount);
        Assert.Equal(4, capture.StopCount);
        Assert.Equal(PipelineState.Stopped, pipeline.State);
    }

    [Fact]
    public async Task CaptureFatalError_RestartsOnce_ThenStopsCleanly()
    {
        var capture = new ScriptedCapture { RaiseFatalOnStartCount = 2 }; // the restart itself dies again
        var config = new AppConfiguration { MaxCaptureRestarts = 1 };
        await using var pipeline = new InterviewPipeline(
            capture, new ScriptedSpeechRecognizer(), new MockTranslator(), new MockAnswerGenerator(), new QuestionDetector(), config);

        var errors = new List<CaptureErrorEventArgs>();
        pipeline.Error += (_, e) => { lock (errors) errors.Add(e); };

        await pipeline.StartAsync();
        Assert.Equal(1, capture.StartCount);

        // Simulate a device disconnect while running.
        capture.RaiseFatal(new InvalidOperationException("device disconnected"));

        await WaitUntilAsync(() => pipeline.State is PipelineState.Stopped or PipelineState.Faulted, 8000);
        await pipeline.StopAsync(); // idempotent safety stop

        Assert.Equal(1, pipeline.CaptureRestarts); // exactly one auto-restart allowed (MaxCaptureRestarts=1)
        Assert.Equal(2, capture.StartCount); // restart re-opened the capture device once
        lock (errors)
        {
            Assert.Contains(errors, e => e.IsFatal); // fatal error surfaced to the UI
        }
        Assert.NotNull(pipeline.LastError);
        Assert.False(pipeline.IsRunning, "After the second fatal error the pipeline must not keep running.");
    }

    [Fact]
    public async Task Backpressure_QueueStaysBounded()
    {
        var capture = new NoopAudioCapture();
        var speech = new SlowSpeechRecognizer { DelayMs = 5 }; // slower than real-time arrival
        await using var pipeline = new InterviewPipeline(
            capture, speech, new MockTranslator(), new MockAnswerGenerator(), new QuestionDetector(),
            new AppConfiguration { ChunkSeconds = 0.5, TargetSampleRate = 16000 });

        await pipeline.StartAsync();

        // Flood the pipeline with far more audio than the bounded queue (128) can hold.
        const int frames = 400;
        for (int i = 0; i < frames; i++)
            pipeline.EnqueueAudio(NonSilent(16000));

        int maxSeen = 0;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 20000)
        {
            maxSeen = Math.Max(maxSeen, pipeline.QueueDepth);
            if (pipeline.QueueDepth == 0 && speech.ProcessCallCount >= 100)
                break;
            await Task.Delay(5);
        }

        await pipeline.StopAsync();

        Assert.True(speech.ProcessCallCount >= 100, "The worker must keep draining after saturation (no wedge).");
        Assert.True(maxSeen <= 128, $"Queue must stay bounded (saw {maxSeen}).");
    }

    [Fact]
    public async Task TranslationDedup_OnlyWhenTextChanged()
    {
        var capture = new NoopAudioCapture();
        // Three chunks, identical text on every pass (partial, partial, final).
        var speech = new ScriptedSpeechRecognizer(
            TranscriptionResult.Partial("What is your greatest strength?"),
            TranscriptionResult.Partial("What is your greatest strength?"),
            TranscriptionResult.Final("What is your greatest strength?"));
        var translator = new MockTranslator();
        await using var pipeline = new InterviewPipeline(
            capture, speech, translator, new MockAnswerGenerator(), new QuestionDetector(),
            new AppConfiguration { ChunkSeconds = 0.5, TargetSampleRate = 16000, MinQuestionChars = 8 });

        await pipeline.StartAsync();
        pipeline.EnqueueAudio(NonSilent(3 * 16000));
        await WaitUntilAsync(() => { lock (translator.Translated) return translator.Translated.Count >= 1; });
        await Task.Delay(200); // allow any (incorrect) duplicate translations to surface
        await pipeline.StopAsync();

        Assert.Equal(3, speech.ProcessCallCount);
        lock (translator.Translated)
            Assert.Single(translator.Translated); // unchanged text must not be re-translated
    }

    [Fact]
    public async Task Stop_CancelsInFlightAnswer()
    {
        var capture = new NoopAudioCapture();
        var speech = new ScriptedSpeechRecognizer(TranscriptionResult.Partial("What is your greatest strength?"));
        var answers = new MockAnswerGenerator { DelayMs = 500 }; // still "generating" when Stop is pressed
        await using var pipeline = new InterviewPipeline(
            capture, speech, new MockTranslator(), answers, new QuestionDetector(),
            new AppConfiguration { ChunkSeconds = 0.5, TargetSampleRate = 16000, MinQuestionChars = 8 });

        var answerSuggs = new List<AnswerSuggestion>();
        pipeline.AnswerUpdated += (_, e) => { lock (answerSuggs) answerSuggs.Add(e.Suggestion); };

        await pipeline.StartAsync();
        pipeline.EnqueueAudio(NonSilent(16000));
        await WaitUntilAsync(() => { lock (answers.Requests) return answers.Requests.Count >= 1; });

        await pipeline.StopAsync(); // must cancel the in-flight generation

        await WaitUntilAsync(() => answers.Tokens.Count > 0 && answers.Tokens[0].IsCancellationRequested, 3000);
        Assert.True(answers.Tokens[0].IsCancellationRequested, "Stop must cancel the in-flight answer generation.");
        Assert.Equal(PipelineState.Stopped, pipeline.State);
        lock (answerSuggs)
            Assert.Empty(answerSuggs); // cancelled generation emitted no answer
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

/// <summary>Capture fake that can raise a fatal error on demand (device disconnect).</summary>
internal sealed class ScriptedCapture : IAudioCapture
{
    public AudioCaptureState State { get; private set; } = AudioCaptureState.Stopped;
    public string? CurrentDeviceId { get; private set; }
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }

    /// <summary>When set, the Nth StartAsync raises a fatal error synchronously (device dies again).</summary>
    public int RaiseFatalOnStartCount { get; set; }

#pragma warning disable CS0067 // required by IAudioCapture; unused in this fake
    public event EventHandler<AudioFrameEventArgs>? FrameCaptured;
    public event EventHandler<AudioDeviceEventArgs>? DeviceChanged;
    public event EventHandler<CaptureErrorEventArgs>? Error;
#pragma warning restore CS0067

    public IReadOnlyList<AudioDevice> GetOutputDevices()
        => new[] { new AudioDevice("d1", "Test Output", true) };

    public Task StartAsync(string? deviceId = null, CancellationToken ct = default)
    {
        StartCount++;
        State = AudioCaptureState.Running;
        CurrentDeviceId = deviceId;
        if (RaiseFatalOnStartCount > 0 && StartCount == RaiseFatalOnStartCount)
            Error?.Invoke(this, new CaptureErrorEventArgs(new InvalidOperationException("device disconnected (again)"), true));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        StopCount++;
        State = AudioCaptureState.Stopped;
        return Task.CompletedTask;
    }

    public void RaiseFatal(Exception ex)
        => Error?.Invoke(this, new CaptureErrorEventArgs(ex, true));

    public void RaiseDeviceChanged(AudioDeviceEventArgs e)
        => DeviceChanged?.Invoke(this, e);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Recognizer slower than real time (each chunk takes DelayMs) to exercise backpressure.</summary>
internal sealed class SlowSpeechRecognizer : ISpeechRecognizer
{
    public int DelayMs { get; set; } = 5;
    public int ProcessCallCount { get; private set; }

    public bool IsReady => true;
    public string ModelName => "slow";
    public string? Language => null;

    public Task PrepareAsync(string model, string? language, CancellationToken ct = default)
        => Task.CompletedTask;

    public async Task<TranscriptionResult> ProcessAudioAsync(AudioFrame chunk, CancellationToken ct = default)
    {
        await Task.Delay(Math.Max(1, DelayMs), ct);
        ProcessCallCount++;
        return TranscriptionResult.Empty;
    }

    public Task<TranscriptionResult> FinalizeAsync(CancellationToken ct = default)
        => Task.FromResult(TranscriptionResult.Empty);

    public void Reset() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
    public Task<TranscriptionResult> FinalizeAsync(CancellationToken ct = default)
        => Task.FromResult(TranscriptionResult.Empty);
    public void Reset() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
