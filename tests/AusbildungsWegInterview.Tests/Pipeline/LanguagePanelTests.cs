using System.Diagnostics;
using AusbildungsWegInterview.Answers;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Common;
using AusbildungsWegInterview.Core.Config;
using AusbildungsWegInterview.Core.Pipeline;
using AusbildungsWegInterview.Core.Speech;
using AusbildungsWegInterview.Core.Translation;
using AusbildungsWegInterview.Tests.Fakes;
using Xunit;

namespace AusbildungsWegInterview.Tests.Pipeline;

/// <summary>
/// MAMADO shell: the detected-question trio and the answer trio must be driven by
/// REAL pipeline data — one panel per configured language, translated locally,
/// with honest pending/unavailable states and no duplicate provider work.
/// </summary>
public class LanguagePanelTests
{
    private const string Question = "What is your greatest strength?";

    private static AudioFrame NonSilent(int bytes, int rate = 16000)
    {
        var b = new byte[bytes];
        for (int i = 0; i < b.Length; i += 2) { b[i] = 0x00; b[i + 1] = 0x20; }
        return new AudioFrame(b, rate);
    }

    private static AudioFrame Silence(int bytes, int rate = 16000) => new(new byte[bytes], rate);

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

    private static AppConfiguration Config() => new()
    {
        ChunkSeconds = 0.5,
        TargetSampleRate = 16000,
        MinQuestionChars = 8
    };

    private static InterviewPipeline Build(
        ScriptedSpeechRecognizer speech, ITranslator translator, MockAnswerGenerator answers, AppConfiguration config)
        => new(new NoopAudioCapture(), speech, translator, answers, new QuestionDetector(), config);

    [Fact]
    public async Task QuestionSet_EmitsOnePanelPerLanguage_TranslatesEachOnce()
    {
        // Default shell set: TranslationLanguage "en" + QuestionLanguages "en,ar".
        var translator = new MockTranslator();
        var speech = new ScriptedSpeechRecognizer(
            new[] { TranscriptionResult.Partial(Question) },
            new[] { Question });
        await using var pipeline = Build(speech, translator, new MockAnswerGenerator(), Config());

        QuestionSetUpdatedEventArgs? last = null;
        pipeline.QuestionSetUpdated += (_, e) => last = e;

        await pipeline.StartAsync();
        pipeline.EnqueueAudio(NonSilent(16000));
        pipeline.EnqueueAudio(Silence(16000));
        pipeline.EnqueueAudio(Silence(16000));
        await WaitUntilAsync(() => last is not null);
        await pipeline.StopAsync();

        Assert.NotNull(last);
        Assert.Equal(Question, last!.Original);
        Assert.Equal(new[] { "en", "ar" }, last.Translations.Select(p => p.Language).ToArray());
        Assert.All(last.Translations, p =>
        {
            Assert.Equal(PanelState.Ready, p.State);
            Assert.StartsWith("[tr]", p.Text);
        });

        // The configured primary target is reused from the incremental pass: the
        // question text must reach the provider exactly twice (en + ar), never 3x.
        lock (translator.Translated)
            Assert.Equal(2, translator.Translated.Count(t => t == Question));
    }

    [Fact]
    public async Task AnswerPanels_ArePendingOnProvisional_AndTranslatedOnFinal()
    {
        var translator = new MockTranslator();
        var speech = new ScriptedSpeechRecognizer(
            new[] { TranscriptionResult.Partial(Question) },
            new[] { Question });
        var answers = new MockAnswerGenerator { AnswerToReturn = "Ich bin Entwickler." };
        await using var pipeline = Build(speech, translator, answers, Config());

        var panelSets = new List<AnswersUpdatedEventArgs>();
        pipeline.AnswersUpdated += (_, e) => { lock (panelSets) panelSets.Add(e); };

        await pipeline.StartAsync();
        pipeline.EnqueueAudio(NonSilent(16000));
        pipeline.EnqueueAudio(Silence(16000));
        pipeline.EnqueueAudio(Silence(16000));
        await WaitUntilAsync(() => { lock (panelSets) return panelSets.Count >= 2; });
        await pipeline.StopAsync();

        AnswersUpdatedEventArgs provisional, final;
        lock (panelSets)
        {
            provisional = panelSets.First(e => e.IsProvisional);
            final = panelSets.Last(e => !e.IsProvisional);
        }

        // Default answer set: de (generated) + en + ar (locally translated).
        Assert.Equal(new[] { "de", "en", "ar" }, final.Panels.Select(p => p.Language).ToArray());
        var primary = final.Panels[0];
        Assert.True(primary.IsPrimary);
        Assert.Equal(PanelState.Ready, primary.State);
        Assert.Equal("Ich bin Entwickler.", primary.Text);

        // Provisional: only the generated panel is filled; the others wait for the
        // final answer (no translation work is wasted on a provisional).
        Assert.All(provisional.Panels.Skip(1), p => Assert.Equal(PanelState.Pending, p.State));
        Assert.Equal("Ich bin Entwickler.", provisional.Panels[0].Text);

        // Final: the other languages really went through the local translator.
        Assert.All(final.Panels.Skip(1), p =>
        {
            Assert.Equal(PanelState.Ready, p.State);
            Assert.Equal("[tr]Ich bin Entwickler.", p.Text);
        });
        lock (translator.Translated)
            Assert.Contains("Ich bin Entwickler.", translator.Translated);
    }

    [Fact]
    public async Task Regenerate_ReRunsGeneration_WithExplicitLengthOverride()
    {
        var speech = new ScriptedSpeechRecognizer(
            new[] { TranscriptionResult.Partial(Question) },
            new[] { Question });
        var answers = new MockAnswerGenerator { AnswerToReturn = "Antwort" };
        await using var pipeline = Build(speech, new MockTranslator(), answers, Config());

        var lengths = new List<AnswerLength>();
        pipeline.AnswerUpdated += (_, e) => { lock (lengths) lengths.Add(e.Suggestion.Length); };

        await pipeline.StartAsync();
        pipeline.EnqueueAudio(NonSilent(16000));
        pipeline.EnqueueAudio(Silence(16000));
        pipeline.EnqueueAudio(Silence(16000));
        // Regeneration is only available once a question has actually been answered.
        await WaitUntilAsync(() => { lock (lengths) return lengths.Count >= 1; });

        // "Shorter" -> one-off regeneration at Concise for the SAME question.
        Assert.True(await pipeline.RegenerateAnswerAsync(AnswerLength.Concise));
        await pipeline.StopAsync();

        lock (lengths)
        {
            Assert.Contains(AnswerLength.Concise, lengths);
            Assert.True(lengths.Count >= 2, "regeneration must emit a fresh suggestion");
        }
        lock (answers.Requests)
        {
            Assert.True(answers.Requests.Count >= 2);
            Assert.All(answers.Requests, r => Assert.Equal(Question, r.QuestionOriginal));
            Assert.Equal(AnswerLength.Concise, answers.Requests[^1].Length);
        }
    }

    [Fact]
    public async Task AudioLevelUpdated_CarriesRealCaptureLevels()
    {
        var speech = new ScriptedSpeechRecognizer(TranscriptionResult.Partial("hello there"));
        await using var pipeline = Build(speech, new MockTranslator(), new MockAnswerGenerator(), Config());

        var levels = new List<double>();
        pipeline.AudioLevelUpdated += (_, e) => { lock (levels) levels.Add(e.Level); };

        await pipeline.StartAsync();
        pipeline.EnqueueAudio(NonSilent(32000));
        await WaitUntilAsync(() => { lock (levels) return levels.Count >= 1; });
        await pipeline.StopAsync();

        lock (levels)
        {
            Assert.NotEmpty(levels);
            Assert.All(levels, l => Assert.InRange(l, 0d, 1d));
            Assert.Contains(levels, l => l > 0d); // the non-silent frame is visible
        }
    }

    [Fact]
    public async Task PanelSet_ReportsUnavailable_WhenTranslationProviderFails()
    {
        // A translator that never produces a real translation (offline engine):
        // every panel must carry the honest Unavailable state, never fake text.
        var speech = new ScriptedSpeechRecognizer(
            new[] { TranscriptionResult.Partial(Question) },
            new[] { Question });
        await using var pipeline = Build(speech, new FailingTranslator(), new MockAnswerGenerator(), Config());

        QuestionSetUpdatedEventArgs? last = null;
        pipeline.QuestionSetUpdated += (_, e) => last = e;

        await pipeline.StartAsync();
        pipeline.EnqueueAudio(NonSilent(16000));
        pipeline.EnqueueAudio(Silence(16000));
        pipeline.EnqueueAudio(Silence(16000));
        await WaitUntilAsync(() => last is not null);
        await pipeline.StopAsync();

        Assert.NotNull(last);
        Assert.All(last!.Translations, p =>
        {
            Assert.Equal(PanelState.Unavailable, p.State);
            Assert.NotNull(p.Status); // machine code for the localized status line
        });
    }

    /// <summary>Translator that reports an offline provider (no fake translations).</summary>
    private sealed class FailingTranslator : ITranslator
    {
        public string ProviderName => "offline";

        public Task PrepareAsync(string? sourceLanguage, string targetLanguage, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<TranslationResult> TranslateAsync(
            string text, string? sourceLanguage, string targetLanguage, CancellationToken ct = default)
            => Task.FromResult(new TranslationResult(
                text, string.Empty, sourceLanguage ?? "auto", targetLanguage, false, ProviderName,
                "provider unavailable", TranslationStatus.ProviderUnavailable));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
