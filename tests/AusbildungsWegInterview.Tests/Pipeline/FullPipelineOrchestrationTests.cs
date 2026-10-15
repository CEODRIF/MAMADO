using System.Diagnostics;
using AusbildungsWegInterview.Answers;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Config;
using AusbildungsWegInterview.Core.Domain;
using AusbildungsWegInterview.Core.Pipeline;
using AusbildungsWegInterview.Core.Speech;
using AusbildungsWegInterview.Tests.Fakes;
using Xunit;
using Xunit.Abstractions;

namespace AusbildungsWegInterview.Tests.Pipeline;

/// <summary>
/// Orchestration-level tests for the complete pipeline: state transitions,
/// stop semantics, and — when a local whisper.cpp build is present — a REAL
/// end-to-end run of actual speech audio through every stage (real ASR, real
/// question detection, mocked LLM stages). The real run is clearly separate
/// from the deterministic mocked tests.
/// </summary>
public class FullPipelineOrchestrationTests
{
    private readonly ITestOutputHelper _output;
    public FullPipelineOrchestrationTests(ITestOutputHelper output) => _output = output;

    // ------------------------------------------------------------------
    // Deterministic (mocked) orchestration
    // ------------------------------------------------------------------

    [Fact]
    public async Task Stop_StopsProcessing_FramesAfterStopAreIgnored()
    {
        var capture = new NoopAudioCapture();
        var speech = new ScriptedSpeechRecognizer(); // returns Empty for everything
        await using var pipeline = new InterviewPipeline(
            capture, speech, new MockTranslator(), new MockAnswerGenerator(), new QuestionDetector(),
            new AppConfiguration { ChunkSeconds = 0.5, TargetSampleRate = 16000 });

        await pipeline.StartAsync();
        pipeline.EnqueueAudio(NonSilent(16000)); // 1 chunk
        await WaitUntilAsync(() => speech.ProcessCallCount >= 1, 5000);
        int processedAtStop = speech.ProcessCallCount;

        await pipeline.StopAsync();
        Assert.Equal(PipelineState.Stopped, pipeline.State);
        Assert.True(capture.StopCalled, "Stop must stop the capture.");
        Assert.Equal(capture.StartCount, capture.StopCount); // stops match starts

        // Frames enqueued after stop must NOT be processed (queue is completed).
        pipeline.EnqueueAudio(NonSilent(16000));
        pipeline.EnqueueAudio(NonSilent(16000));
        pipeline.EnqueueAudio(NonSilent(16000));
        await Task.Delay(200);
        Assert.True(processedAtStop == speech.ProcessCallCount, "Audio arriving after Stop must be dropped, not processed.");
    }

    [Fact]
    public async Task StateSequence_StartThroughStop_IsWellFormed()
    {
        var capture = new NoopAudioCapture();
        await using var pipeline = new InterviewPipeline(
            capture, new ScriptedSpeechRecognizer(), new MockTranslator(), new MockAnswerGenerator(), new QuestionDetector());

        var states = new List<PipelineState>();
        pipeline.StateChanged += (_, e) => { lock (states) states.Add(e.State); };

        Assert.Equal(PipelineState.Idle, pipeline.State);
        await pipeline.StartAsync();
        await pipeline.StopAsync();

        lock (states)
        {
            Assert.Contains(PipelineState.Listening, states);
            Assert.Equal(PipelineState.Stopped, states[^1]); // final observed state is Stopped
        }
    }

    [Fact]
    public async Task DeviceChange_DuringListening_DoesNotBreakPipeline()
    {
        var capture = new ScriptedCapture();
        await using var pipeline = new InterviewPipeline(
            capture, new ScriptedSpeechRecognizer(), new MockTranslator(), new MockAnswerGenerator(), new QuestionDetector());

        await pipeline.StartAsync();
        Assert.Equal(PipelineState.Listening, pipeline.State);

        // OS says the default device moved; the capture would re-open internally.
        capture.RaiseDeviceChanged(new AudioDeviceEventArgs(new AudioDevice("d2", "New Default", true)));

        await Task.Delay(150);
        Assert.True(pipeline.IsRunning, "A device change notification alone must not stop the pipeline.");
        await pipeline.StopAsync();
        Assert.Equal(PipelineState.Stopped, pipeline.State);
    }

    [Fact]
    public async Task SustainedAudio_OneMinute_Simulated_BoundedAndStable()
    {
        var capture = new NoopAudioCapture();
        var speech = new ScriptedSpeechRecognizer(); // cheap
        await using var pipeline = new InterviewPipeline(
            capture, speech, new MockTranslator(), new MockAnswerGenerator(), new QuestionDetector(),
            new AppConfiguration { ChunkSeconds = 0.5, TargetSampleRate = 16000 });

        await pipeline.StartAsync();

        // Simulate 1 minute of continuous (non-speech, all-silent) audio in a tight
        // loop: 120 frames × 0.5 s. Nothing should grow unboundedly or crash.
        for (int i = 0; i < 120; i++)
            pipeline.EnqueueAudio(new AudioFrame(new byte[16000], 16000));

        await WaitUntilAsync(() => pipeline.QueueDepth == 0, 20000);
        Assert.True(pipeline.QueueDepth <= 128);

        await pipeline.StopAsync();
        Assert.Equal(PipelineState.Stopped, pipeline.State);
    }

    // ------------------------------------------------------------------
    // REAL end-to-end: actual speech audio through the full pipeline
    // (gated on a local whisper.cpp build; skipped with a note otherwise)
    // ------------------------------------------------------------------

    [Fact]
    public async Task RealEndToEnd_JfkAudio_TranscribeDetectTranslateAnswer()
    {
        var loc = LocalWhisperAssets.Find();
        if (loc is null)
        {
            _output.WriteLine("SKIPPED: no local whisper.cpp build (real E2E requires whisper-cli + ggml-tiny.bin + jfk.wav).");
            return;
        }

        byte[] pcm = LocalWhisperAssets.ReadPcm16MonoWav(loc.JfkWav, out int rate);
        Assert.Equal(16000, rate);

        var capture = new NoopAudioCapture();
        var speech = new AusbildungsWegInterview.Speech.WhisperCliRecognizer(
            loc.Bin, loc.Model, threads: Math.Min(2, Environment.ProcessorCount),
            partialIntervalSeconds: 5.0,      // few real partials: keep the test fast
            maxSegmentSeconds: 60.0,
            timeoutMs: 120000,
            workDir: Path.Combine(Path.GetTempPath(), "awg-e2e-" + Guid.NewGuid().ToString("N")));
        var translator = new MockTranslator();
        var answers = new MockAnswerGenerator { AnswerToReturn = "Grounded answer (mock LLM)." };
        var detector = new QuestionDetector();

        var config = new AppConfiguration
        {
            ChunkSeconds = 0.5,
            TargetSampleRate = 16000,
            SilenceRmsThreshold = 0.01,
            SilenceFinalizeSeconds = 0.8,
            MinQuestionChars = 8
        };

        var sw = Stopwatch.StartNew();
        await using var pipeline = new InterviewPipeline(
            capture, speech, translator, answers, detector, config);

        var transcripts = new List<TranscriptionResult>();
        var translations = new List<string>();
        var answers2 = new List<AnswerSuggestion>();
        pipeline.TranscriptUpdated += (_, e) => { lock (transcripts) transcripts.Add(e.Result); };
        pipeline.TranslationUpdated += (_, e) => { lock (translations) translations.Add(e.Result.TargetText); };
        pipeline.AnswerUpdated += (_, e) => { lock (answers2) answers2.Add(e.Suggestion); };

        await pipeline.StartAsync();

        // Feed the real 11 s JFK clip in 0.5 s frames...
        int chunkBytes = 8000 * 2;
        for (int offset = 0; offset < pcm.Length; offset += chunkBytes)
        {
            int len = Math.Min(chunkBytes, pcm.Length - offset);
            var buf = new byte[len];
            Buffer.BlockCopy(pcm, offset, buf, 0, len);
            pipeline.EnqueueAudio(new AudioFrame(buf, rate));
        }
        // ...then 2 s of trailing silence to drive finalization.
        for (int i = 0; i < 4; i++)
            pipeline.EnqueueAudio(new AudioFrame(new byte[16000], rate));

        // Wait for the final transcript (bounded: model runs locally, 11 s audio).
        bool gotFinal = false;
        var swWait = Stopwatch.StartNew();
        while (swWait.ElapsedMilliseconds < 180000)
        {
            lock (transcripts)
                gotFinal = transcripts.Any(t => t.IsFinal && !string.IsNullOrWhiteSpace(t.Text));
            if (gotFinal) break;
            await Task.Delay(100);
        }
        await pipeline.StopAsync();
        sw.Stop();

        Assert.True(gotFinal, "The full pipeline produced no final transcript from real audio.");

        string finalText;
        lock (transcripts)
        {
            finalText = transcripts.Where(t => t.IsFinal).Last().Text;
        }
        Assert.Contains("fellow Americans", finalText, StringComparison.OrdinalIgnoreCase);

        // Question detection: "…ask not what your country can do for you…" contains
        // the interrogative word "what" → the pipeline must have generated an answer.
        lock (answers2)
        {
            Assert.NotEmpty(answers2); // real audio containing a question word produced a suggestion
            Assert.False(answers2[^1].IsProvisional, "The last suggestion after the final transcript must be final.");
        }

        // Translation ran on the real transcript (mock provider, deterministic prefix).
        lock (translations)
            Assert.NotEmpty(translations); // the pipeline translated the detected segment

        _output.WriteLine($"REAL E2E: {sw.ElapsedMilliseconds} ms total (incl. local whisper inference) | final: {finalText}");
    }

    // ------------------------------------------------------------------

    private static AudioFrame NonSilent(int bytes)
    {
        var b = new byte[bytes];
        for (int i = 0; i < bytes; i += 2) { b[i] = 0x00; b[i + 1] = 0x20; }
        return new AudioFrame(b, 16000);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 10000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        Assert.Fail($"Timeout ({timeoutMs} ms) waiting for pipeline condition.");
    }
}
