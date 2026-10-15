using System.Threading.Channels;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Common;
using AusbildungsWegInterview.Core.Config;
using AusbildungsWegInterview.Core.Domain;
using AusbildungsWegInterview.Core.Logging;
using AusbildungsWegInterview.Core.Speech;
using AusbildungsWegInterview.Core.Translation;

namespace AusbildungsWegInterview.Core.Pipeline;

/// <summary>
/// The real-time interview pipeline. It ties the providers together:
///   audio capture -> normalize -> chunk -> silence segmentation -> speech
///   -> question detection -> translation -> grounded answer.
///
/// Hardening goals (all covered by tests):
///  * Bounded queue (DropOldest) = backpressure; no unbounded memory growth.
///  * Silence GATES segment start only. Once a segment is active, every chunk
///    (including quiet ones) is fed to the recognizer so sentence endings survive.
///  * Sustained trailing silence FINALIZES the segment (end-of-question), then resets.
///  * Translation dedup: only re-translate when the segment text actually changed.
///  * Answer policy: provisional once per question, refined on final; obsolete
///    in-flight generations are cancelled.
///  * Capture device failures trigger a bounded number of auto-restarts before faulting.
///  * Deterministic start/stop/disposal, including repeated cycles.
/// </summary>
public sealed class InterviewPipeline : IAsyncDisposable
{
    private readonly IAudioCapture _capture;
    private readonly ISpeechRecognizer _speech;
    private readonly ITranslator _translator;
    private readonly IAnswerGenerator _answers;
    private readonly IQuestionDetector _questionDetector;
    private readonly AppConfiguration _config;
    private readonly AudioChunker _chunker;
    private readonly SilenceDetector _silence;
    private readonly IAppLogger _logger;

    private readonly object _gate = new();

    private Channel<AudioFrame>? _frameQueue;
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _answerCts;
    private Task? _worker;

    /// <summary>In-flight Stop (re-entrancy guard: concurrent Stops share one task).</summary>
    private Task? _stopTask;

    // Segment-scoped state (worker thread only)
    private bool _activeSegment;
    private double _silentSeconds;
    private string _lastTranslationKey = string.Empty;
    private bool _answeredInRound;
    private string? _lastAnsweredText;     // exact text already answered in the current segment
    private AnswerSuggestion? _lastSuggestion; // that answer (to finalize in place, without a new LLM call)
    private readonly List<string> _recentTurns = new();

    // Session-scoped duplicate-question memory (worker thread only, bounded).
    private readonly Queue<string> _answeredQuestions = new();
    private const int MaxAnsweredQuestions = 5;

    // Capture lifecycle (shared: capture thread + control thread)
    private volatile bool _userStopping;
    private int _captureRestarts;

    public PipelineState State { get; private set; } = PipelineState.Idle;
    public string? LastError { get; private set; }
    public CandidateContext Context { get; set; } = CandidateContext.Empty;
    public bool IsRunning => State is PipelineState.Listening or PipelineState.Processing;

    /// <summary>Depth of the bounded input queue (backpressure / saturation metric).</summary>
    public int QueueDepth => _frameQueue?.Reader.Count ?? 0;

    /// <summary>Number of capture auto-restarts performed.</summary>
    public int CaptureRestarts
    {
        get { lock (_gate) return _captureRestarts; }
    }

    public event EventHandler<TranscriptUpdatedEventArgs>? TranscriptUpdated;
    public event EventHandler<TranslationUpdatedEventArgs>? TranslationUpdated;
    public event EventHandler<AnswerUpdatedEventArgs>? AnswerUpdated;
    public event EventHandler<PipelineStateChangedEventArgs>? StateChanged;
    public event EventHandler<CaptureErrorEventArgs>? Error;

    public InterviewPipeline(
        IAudioCapture capture,
        ISpeechRecognizer speech,
        ITranslator translator,
        IAnswerGenerator answers,
        IQuestionDetector questionDetector,
        AppConfiguration? config = null,
        IAppLogger? logger = null)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _speech = speech ?? throw new ArgumentNullException(nameof(speech));
        _translator = translator ?? throw new ArgumentNullException(nameof(translator));
        _answers = answers ?? throw new ArgumentNullException(nameof(answers));
        _questionDetector = questionDetector ?? throw new ArgumentNullException(nameof(questionDetector));
        _config = config ?? AppConfiguration.Default();
        _chunker = new AudioChunker(_config.TargetSampleRate, _config.ChunkSeconds);
        _silence = new SilenceDetector(_config.SilenceRmsThreshold);
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning)
            return;

        lock (_gate)
        {
            _userStopping = false;
            _captureRestarts = 0;
        }

        var runCts = new CancellationTokenSource();
        _runCts = runCts;
        _frameQueue = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        // Reset segment + conversation state for a clean session.
        _activeSegment = false;
        _silentSeconds = 0;
        _answeredInRound = false;
        _lastAnsweredText = null;
        _lastSuggestion = null;
        _lastTranslationKey = string.Empty;
        _lastTranslation = null;
        _recentTurns.Clear();
        _answeredQuestions.Clear();
        _chunker.Reset();
        LastError = null;

        SetState(PipelineState.Preparing);

        // 1) Prepare local runtimes. Fail fast with a clear, actionable error.
        try
        {
            if (!_speech.IsReady)
                await _speech.PrepareAsync(_config.WhisperModel, _config.SourceLanguage, ct).ConfigureAwait(false);
            await _translator.PrepareAsync(_config.SourceLanguage, _config.TranslationLanguage, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            SetState(PipelineState.Faulted);
            throw new InvalidOperationException("Failed to prepare local models: " + ex.Message, ex);
        }

        // 2) Start audio capture.
        _capture.FrameCaptured += OnFrameCaptured;
        _capture.Error += OnCaptureError;
        _capture.DeviceChanged += OnCaptureDeviceChanged;
        try
        {
            await _capture.StartAsync(_config.OutputDeviceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await UnsubscribeAndStopCaptureAsync().ConfigureAwait(false);
            LastError = ex.Message;
            SetState(PipelineState.Faulted);
            throw new InvalidOperationException("Failed to start audio capture: " + ex.Message, ex);
        }

        _worker = Task.Run(() => WorkerAsync(runCts.Token), runCts.Token);
        SetState(PipelineState.Listening);
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        if (State is PipelineState.Stopped or PipelineState.Idle)
            return Task.CompletedTask;

        // Re-entrancy: a second Stop (e.g. UI stop + app-shutdown dispose at the
        // same moment) joins the in-flight stop instead of racing the CTS/queue
        // teardown (which used to risk ObjectDisposedException).
        lock (_gate)
        {
            if (_stopTask is not null)
                return _stopTask;
            _userStopping = true;
            _stopTask = StopCoreAsync();
            return _stopTask;
        }
    }

    private async Task StopCoreAsync()
    {
        try
        {
            SetState(PipelineState.Stopping);

            // Cancel the run token FIRST. This is what makes Stop reliable:
            //  * an in-flight whisper.cpp subprocess is killed promptly (its
            //    WaitForExitAsync token is linked to the run token),
            //  * the worker exits ReadAllAsync immediately instead of draining
            //    the bounded queue (up to 128 frames of audio) after Stop,
            //  * the worker's shutdown tail-flush is skipped (guarded by the token).
            _runCts?.Cancel();
            _answerCts?.Cancel();

            await UnsubscribeAndStopCaptureAsync().ConfigureAwait(false);

            if (_frameQueue is not null)
                _frameQueue.Writer.TryComplete();

            if (_worker is not null)
            {
                try { await _worker.ConfigureAwait(false); }
                catch { /* best effort */ }
                _worker = null;
            }

            _activeSegment = false;
            SetState(PipelineState.Stopped);
        }
        finally
        {
            if (_runCts is not null)
            {
                _runCts.Dispose();
                _runCts = null;
            }
            if (_answerCts is not null)
            {
                _answerCts.Dispose();
                _answerCts = null;
            }
            _frameQueue = null;
            lock (_gate)
                _stopTask = null;
        }
    }

    /// <summary>Feeds an audio frame into the pipeline (custom sources + tests).</summary>
    public void EnqueueAudio(AudioFrame frame)
    {
        _frameQueue?.Writer.TryWrite(frame);
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync().ConfigureAwait(false); }
        catch { /* ignore */ }
        _answerCts?.Dispose();
        _answerCts = null;
    }

    // ---------------- capture events ----------------

    private void OnFrameCaptured(object? sender, AudioFrameEventArgs e)
    {
        _frameQueue?.Writer.TryWrite(e.Frame);
    }

    private void OnCaptureError(object? sender, CaptureErrorEventArgs e)
    {
        if (e.IsFatal)
        {
            // Auto-restart applies to IN-SESSION failures (device lost while
            // listening) only. A fatal error raised by the INITIAL StartAsync
            // (e.g. no output device) is already handled by StartAsync's own
            // catch (teardown + Faulted state); reacting here would schedule a
            // duplicate restart, a duplicate error, and a racing StopAsync.
            bool restart;
            lock (_gate)
            {
                restart = IsRunning && !_userStopping && _captureRestarts < _config.MaxCaptureRestarts;
                if (restart)
                    _captureRestarts++;
            }

            if (restart)
            {
                // Non-fatal from the user's perspective: we will try to recover.
                ReportError(e.Error, isFatal: false);
                _ = TryRestartCaptureAsync();
            }
            else if (IsRunning)
            {
                ReportError(e.Error, isFatal: true);
                _ = StopAsync();
            }
            else
            {
                ReportError(e.Error, isFatal: true);
            }
        }
        else
        {
            ReportError(e.Error, isFatal: false);
        }
    }

    // Device changes are handled by the capture itself (re-open on a new default,
    // fatal error when the device is lost). The pipeline only observes + logs so
    // the operator has an audit trail; a lost device already raised Error above.
    private void OnCaptureDeviceChanged(object? sender, AudioDeviceEventArgs e)
    {
        _logger.Log(LogLevel.Warning, "audio",
            "device change -> " + (e.Device?.Id ?? "(none)"));
    }

    private async Task TryRestartCaptureAsync()
    {
        var cts = _runCts;
        if (cts is null)
            return;
        bool cancelled;
        try { cancelled = cts.IsCancellationRequested; }
        catch (ObjectDisposedException) { return; } // Stop tore the run down concurrently
        if (cancelled)
            return;

        _capture.FrameCaptured -= OnFrameCaptured;
        _capture.Error -= OnCaptureError;
        _capture.DeviceChanged -= OnCaptureDeviceChanged;
        try { await _capture.StopAsync(cts.Token).ConfigureAwait(false); }
        catch { /* ignore */ }

        try
        {
            _capture.FrameCaptured += OnFrameCaptured;
            _capture.Error += OnCaptureError;
            _capture.DeviceChanged += OnCaptureDeviceChanged;
            await _capture.StartAsync(_config.OutputDeviceId, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReportError(ex, isFatal: true);
        }
    }

    private async Task UnsubscribeAndStopCaptureAsync()
    {
        _capture.FrameCaptured -= OnFrameCaptured;
        _capture.Error -= OnCaptureError;
        _capture.DeviceChanged -= OnCaptureDeviceChanged;
        try { await _capture.StopAsync().ConfigureAwait(false); }
        catch { /* ignore */ }
    }

    // ---------------- worker ----------------

    private async Task WorkerAsync(CancellationToken ct)
    {
        if (_frameQueue is null)
            return;

        var reader = _frameQueue.Reader;
        try
        {
            await foreach (var frame in reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested)
                    break;

                AudioFrame normalized;
                try
                {
                    normalized = PcmConverter.ToMono16k(frame, _config.TargetSampleRate);
                }
                catch (NotSupportedException)
                {
                    continue;
                }

                foreach (var chunk in _chunker.Push(normalized))
                {
                    await HandleChunkAsync(chunk, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on stop
        }
        finally
        {
            // On shutdown, flush the tail and finalize any in-flight segment.
            var tail = _chunker.Flush();
            if (tail is not null && !_silence.IsSilent(tail) && !ct.IsCancellationRequested)
            {
                try { await HandleChunkAsync(tail, CancellationToken.None).ConfigureAwait(false); }
                catch { /* best effort */ }
            }
            if (_activeSegment && !ct.IsCancellationRequested)
            {
                try { await FinalizeSegmentAsync(CancellationToken.None).ConfigureAwait(false); }
                catch { /* best effort */ }
            }
        }
    }

    private async Task HandleChunkAsync(AudioFrame chunk, CancellationToken ct)
    {
        if (!_speech.IsReady)
            return;

        bool silent = _silence.IsSilent(chunk);
        double chunkSeconds = chunk.DurationMs / 1000.0;

        // Segment START: silence gates entry; once active, keep feeding (incl. quiet) so endings survive.
        if (!_activeSegment)
        {
            if (silent)
                return;
            _activeSegment = true;
            _silentSeconds = 0;
            _answeredInRound = false;
            _lastAnsweredText = null;
            _lastSuggestion = null;
            _lastTranslationKey = string.Empty;
            _lastTranslation = null;
            _speech.Reset();
        }

        TranscriptionResult result;
        try
        {
            result = await _speech.ProcessAudioAsync(chunk, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ReportError(ex, isFatal: false);
            return;
        }

        if (result is not null && !string.IsNullOrWhiteSpace(result.Text))
        {
            EmitTranscript(result);
            await TranslateIncrementallyAsync(result, ct).ConfigureAwait(false);
            await MaybeAnswerAsync(result, ct).ConfigureAwait(false);
        }

        // Segment END, recognizer-driven. An EMPTY result must not count as final
        // (TranscriptionResult.Empty is Final-shaped), or silence-driven
        // finalization would never run.
        if (result is { IsFinal: true } && !string.IsNullOrWhiteSpace(result.Text))
        {
            FinalizeSegment();
            return;
        }

        // Segment END, silence-driven (sustained trailing silence).
        if (silent)
        {
            _silentSeconds += chunkSeconds;
            if (_silentSeconds >= _config.SilenceFinalizeSeconds)
            {
                await FinalizeSegmentAsync(ct).ConfigureAwait(false);
            }
        }
        else
        {
            _silentSeconds = 0;
        }
    }

    private async Task FinalizeSegmentAsync(CancellationToken ct)
    {
        if (!_activeSegment)
            return;

        TranscriptionResult? final = null;
        try
        {
            final = await _speech.FinalizeAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // stopped mid-finalize
        }
        catch (Exception ex)
        {
            ReportError(ex, isFatal: false);
        }

        // Cancellation-aware: a Stop during finalization must cancel the final
        // translation/answer too (previously these ran with CancellationToken.None
        // and could outlive the stop, updating the UI after "Stopped").
        if (final is not null && !string.IsNullOrWhiteSpace(final.Text) && !ct.IsCancellationRequested)
        {
            var f = final.IsFinal ? final : final with { Kind = TranscriptKind.Final };
            EmitTranscript(f);
            await TranslateIncrementallyAsync(f, ct).ConfigureAwait(false);
            await MaybeAnswerAsync(f, ct).ConfigureAwait(false);
        }

        FinalizeSegment();
    }

    private void FinalizeSegment()
    {
        _activeSegment = false;
        _silentSeconds = 0;
        _answeredInRound = false;
        _lastAnsweredText = null;
        _lastSuggestion = null;
        _lastTranslationKey = string.Empty;
        _lastTranslation = null;
        _speech.Reset();
    }

    private void EmitTranscript(TranscriptionResult result)
        => TranscriptUpdated?.Invoke(this, new TranscriptUpdatedEventArgs(result));

    private async Task TranslateIncrementallyAsync(TranscriptionResult result, CancellationToken ct)
    {
        // whisper (-tr) translates the segment AUDIO: expensive and only meaningful
        // once the segment is final. Partials stay untranslated on that provider.
        if (_config.TranslationProvider == TranslationProvider.WhisperTranslate && !result.IsFinal)
            return;

        if (!string.IsNullOrWhiteSpace(result.Text) && result.Text == _lastTranslationKey)
            return; // dedup: unchanged text
        if (!string.IsNullOrWhiteSpace(result.Text))
            _lastTranslationKey = result.Text;

        try
        {
            var tr = await _translator.TranslateAsync(
                result.Text, result.DetectedLanguage, _config.TranslationLanguage, ct).ConfigureAwait(false);
            // Emit when there is real text OR an honest status to display
            // (e.g. "Ollama unavailable - original shown").
            if (tr is not null && (tr.HasText || tr.StatusMessage is not null))
            {
                // Remember only GENUINE translations for the answer prompt
                // (pass-through / unchanged / failure are not translations).
                if (tr.IsTranslated)
                    _lastTranslation = tr.TargetText;
                TranslationUpdated?.Invoke(this, new TranslationUpdatedEventArgs(tr));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ReportError(ex, isFatal: false);
        }
    }

    /// <summary>Latest real translation of the current segment (null unless one was produced).</summary>
    private string? _lastTranslation;

    private async Task MaybeAnswerAsync(TranscriptionResult result, CancellationToken ct)
    {
        bool sufficient = _questionDetector.IsSufficient(result.Text, _config.MinQuestionChars, result.IsFinal);
        if (!sufficient)
            return;

        string? normalized = result.IsFinal ? NormalizeForDedup(result.Text) : null;

        // Duplicate-question protection (finalized questions only):
        //  1) the same question (normalized) was already answered in a recent
        //     segment — interviewer repeat or ASR re-segmentation. The existing
        //     answer is still on screen; a second generation is wasted work.
        if (result.IsFinal && IsRecentlyAnswered(normalized!))
            return;

        //  2) the FINAL text is identical to the provisional text that was
        //     already answered in this segment: finalize the existing suggestion
        //     in place (UI flips provisional -> final) without a new LLM call.
        if (result.IsFinal && _lastSuggestion is not null &&
            string.Equals(result.Text, _lastAnsweredText, StringComparison.Ordinal))
        {
            var finalized = _lastSuggestion with { IsProvisional = false };
            AnswerUpdated?.Invoke(this, new AnswerUpdatedEventArgs(finalized));
            if (finalized.HasAnswer)
            {
                RememberTurn(result.Text, finalized.Answer);
                RememberAnsweredQuestion(normalized!);
            }
            return;
        }

        // Provisional answer only once per question; final answer otherwise refines it.
        bool shouldGenerate = result.IsFinal || !_answeredInRound;
        if (!shouldGenerate)
            return;

        try
        {
            // Cancel any in-flight answer for a now-superseded question, and
            // release the previous token source (no unbounded CTS accumulation).
            _answerCts?.Cancel();
            _answerCts?.Dispose();
            var answerCts = new CancellationTokenSource();
            _answerCts = answerCts;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, answerCts.Token);

            var recent = SnapshotRecentTurns();
            var request = new AnswerRequest(
                result.Text,
                _lastTranslation,
                Context,
                _config.AnswerLength,
                _config.AnswerLanguage,
                _config.AnswerStyle,
                recent,
                Level: _config.AnswerLevel);

            var suggestion = await _answers.GenerateAsync(request, linked.Token).ConfigureAwait(false);

            // Stale-result guard: if Stop raced a completing generation (the
            // response arrived in the same instant the run token fired), drop
            // the result instead of updating the UI after the session stopped.
            if (suggestion is not null && !ct.IsCancellationRequested)
            {
                var emitted = result.IsFinal
                    ? suggestion with { IsProvisional = false }
                    : suggestion with { IsProvisional = true };
                AnswerUpdated?.Invoke(this, new AnswerUpdatedEventArgs(emitted));

                // Remember for the in-segment refine path (case  2) above.
                _lastAnsweredText = result.Text;
                _lastSuggestion = emitted;

                if (result.IsFinal && emitted.HasAnswer)
                {
                    RememberTurn(result.Text, emitted.Answer);
                    RememberAnsweredQuestion(normalized!);
                }
            }

            _answeredInRound = !result.IsFinal;
        }
        catch (OperationCanceledException)
        {
            // superseded or stopped
        }
        catch (Exception ex)
        {
            ReportError(ex, isFatal: false);
        }
    }

    private bool IsRecentlyAnswered(string normalized)
    {
        if (string.IsNullOrEmpty(normalized))
            return false;
        lock (_answeredQuestions)
            return _answeredQuestions.Contains(normalized);
    }

    private void RememberAnsweredQuestion(string normalized)
    {
        if (string.IsNullOrEmpty(normalized))
            return;
        lock (_answeredQuestions)
        {
            if (_answeredQuestions.Contains(normalized))
                return;
            _answeredQuestions.Enqueue(normalized);
            while (_answeredQuestions.Count > MaxAnsweredQuestions)
                _answeredQuestions.Dequeue();
        }
    }

    /// <summary>
    /// Normalizes a question for REPEAT DETECTION only (never for display):
    /// case, punctuation, and whitespace are ignored, and common Arabic
    /// orthographic variants are unified (hamza forms, ta-marbuta, alif-maqsura,
    /// diacritics), so "Was ist dein Name?" / "was ist dein name" / "Was ist
    /// dein Name" dedupe together. A deliberate heuristic — it catches literal
    /// repeats, not paraphrases.
    /// </summary>
    internal static string NormalizeForDedup(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is '?' or '؟' or '!' or '.' or ',' or ';' or ':' or '-' or '—' or '(' or ')' or '"' or '\'')
                continue;
            if (!char.IsLetterOrDigit(ch) && !char.IsWhiteSpace(ch))
                continue;

            char c = ch switch
            {
                'أ' or 'إ' or 'آ' => 'ا',
                'ة' => 'ه',
                'ى' => 'ي',
                >= '\u064B' and <= '\u0652' => '\u0000', // Arabic diacritics
                _ => ch
            };
            if (c != '\u0000')
                sb.Append(char.ToLowerInvariant(c));
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private IReadOnlyList<string> SnapshotRecentTurns()
    {
        lock (_recentTurns)
            return _recentTurns.ToList().AsReadOnly();
    }

    private void RememberTurn(string question, string answer)
    {
        lock (_recentTurns)
        {
            _recentTurns.Add($"Q: {Truncate(question, 200)} | A: {Truncate(answer, 300)}");
            int max = Math.Max(0, _config.RecentTurns);
            while (_recentTurns.Count > max)
                _recentTurns.RemoveAt(0);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    // ---------------- error/state ----------------

    private void ReportError(Exception ex, bool isFatal)
    {
        LastError = ex.Message;
        // Log only the exception TYPE (never transcript/CV/audio content).
        _logger.Log(LogLevel.Error, "pipeline", "error: " + ex.GetType().Name);
        if (isFatal)
            SetState(PipelineState.Faulted);
        Error?.Invoke(this, new CaptureErrorEventArgs(ex, isFatal));
    }

    private void SetState(PipelineState state)
    {
        lock (_gate)
        {
            if (State == state)
                return;
            State = state;
        }
        StateChanged?.Invoke(this, new PipelineStateChangedEventArgs(state));
        _logger.Log(LogLevel.Info, "pipeline", "state -> " + state);
    }
}
