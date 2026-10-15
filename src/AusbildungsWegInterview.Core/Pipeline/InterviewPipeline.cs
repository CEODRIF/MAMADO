using System.Threading.Channels;
using AusbildungsWegInterview.Core.Answers;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Config;
using AusbildungsWegInterview.Core.Domain;
using AusbildungsWegInterview.Core.Speech;
using AusbildungsWegInterview.Core.Translation;

namespace AusbildungsWegInterview.Core.Pipeline;

/// <summary>
/// The real-time interview pipeline. It ties the four providers together:
///   audio capture -> chunking/silence gating -> speech -> translation -> answer.
///
/// Design goals (all covered by tests):
///  * Incremental processing with a bounded queue (no unbounded memory growth).
///  * Silence gating to avoid work on quiet audio.
///  * Deduplication so minor transcript updates do not re-trigger translation/answers.
///  * Obsolete answer work is cancelled; a provisional answer is produced once per
///    question and refined when the question finalizes.
///  * No persistent audio or transcripts by default.
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

    private readonly object _gate = new();

    private Channel<AudioFrame>? _frameQueue;
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _answerCts;
    private Task? _worker;

    private string _lastTranslationKey = string.Empty;
    private bool _answeredInRound;

    public PipelineState State { get; private set; } = PipelineState.Idle;
    public string? LastError { get; private set; }
    public CandidateContext Context { get; set; } = CandidateContext.Empty;
    public bool IsRunning => State is PipelineState.Listening or PipelineState.Processing;

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
        AppConfiguration? config = null)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _speech = speech ?? throw new ArgumentNullException(nameof(speech));
        _translator = translator ?? throw new ArgumentNullException(nameof(translator));
        _answers = answers ?? throw new ArgumentNullException(nameof(answers));
        _questionDetector = questionDetector ?? throw new ArgumentNullException(nameof(questionDetector));
        _config = config ?? AppConfiguration.Default();
        _chunker = new AudioChunker(_config.TargetSampleRate, _config.ChunkSeconds);
        _silence = new SilenceDetector(_config.SilenceRmsThreshold);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning)
            return;

        var runCts = new CancellationTokenSource();
        _runCts = runCts;
        _frameQueue = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _chunker.Reset();
        _answeredInRound = false;
        _lastTranslationKey = string.Empty;
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
        try
        {
            await _capture.StartAsync(_config.OutputDeviceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await CleanupCaptureAsync().ConfigureAwait(false);
            LastError = ex.Message;
            SetState(PipelineState.Faulted);
            throw new InvalidOperationException("Failed to start audio capture: " + ex.Message, ex);
        }

        _worker = Task.Run(() => WorkerAsync(runCts.Token), runCts.Token);
        SetState(PipelineState.Listening);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (State is PipelineState.Stopped or PipelineState.Idle)
            return;

        SetState(PipelineState.Stopping);
        _answerCts?.Cancel();

        await CleanupCaptureAsync().ConfigureAwait(false);

        if (_frameQueue is not null)
            _frameQueue.Writer.TryComplete();

        if (_worker is not null)
        {
            try { await _worker.ConfigureAwait(false); }
            catch { /* best effort */ }
            _worker = null;
        }

        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = null;
        _frameQueue = null;
        SetState(PipelineState.Stopped);
    }

    /// <summary>
    /// Feeds an audio frame into the pipeline. Used by custom capture sources and
    /// by deterministic tests; the real IAudioCapture also pushes frames here.
    /// </summary>
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

    private void OnFrameCaptured(object? sender, AudioFrameEventArgs e)
    {
        _frameQueue?.Writer.TryWrite(e.Frame);
    }

    private void OnCaptureError(object? sender, CaptureErrorEventArgs e)
    {
        if (e.IsFatal)
        {
            ReportError(e.Error, isFatal: true);
            _ = StopAsync();
        }
        else
        {
            ReportError(e.Error, isFatal: false);
        }
    }

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
                    if (_silence.IsSilent(chunk))
                        continue;
                    await ProcessChunkAsync(chunk, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on stop
        }
        finally
        {
            // Flush any trailing samples once.
            var tail = _chunker.Flush();
            if (tail is not null && !_silence.IsSilent(tail) && !ct.IsCancellationRequested)
            {
                try { await ProcessChunkAsync(tail, CancellationToken.None).ConfigureAwait(false); }
                catch { /* best effort */ }
            }
        }
    }

    private async Task ProcessChunkAsync(AudioFrame chunk, CancellationToken ct)
    {
        if (!_speech.IsReady)
            return;

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

        if (result is null || string.IsNullOrWhiteSpace(result.Text))
            return;

        EmitTranscript(result);
        await TranslateIncrementallyAsync(result, ct).ConfigureAwait(false);
        await MaybeAnswerAsync(result, ct).ConfigureAwait(false);
    }

    private void EmitTranscript(TranscriptionResult result)
        => TranscriptUpdated?.Invoke(this, new TranscriptUpdatedEventArgs(result));

    private async Task TranslateIncrementallyAsync(TranscriptionResult result, CancellationToken ct)
    {
        if (result.Text == _lastTranslationKey)
            return; // dedup: unchanged text
        _lastTranslationKey = result.Text;

        try
        {
            var tr = await _translator.TranslateAsync(
                result.Text, result.DetectedLanguage, _config.TranslationLanguage, ct).ConfigureAwait(false);
            if (tr is not null && tr.HasText)
                TranslationUpdated?.Invoke(this, new TranslationUpdatedEventArgs(tr));
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

    private async Task MaybeAnswerAsync(TranscriptionResult result, CancellationToken ct)
    {
        bool sufficient = _questionDetector.IsSufficient(result.Text, _config.MinQuestionChars, result.IsFinal);
        if (!sufficient)
            return;

        // Provisional answer only once per question; final answer always refines it.
        bool shouldGenerate = result.IsFinal || !_answeredInRound;
        if (!shouldGenerate)
            return;

        try
        {
            // Cancel any in-flight answer for a now-superseded question.
            _answerCts?.Cancel();
            var answerCts = new CancellationTokenSource();
            _answerCts = answerCts;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, answerCts.Token);

            var request = new AnswerRequest(
                result.Text,
                null,
                Context,
                _config.AnswerLength,
                _config.AnswerLanguage);

            var suggestion = await _answers.GenerateAsync(request, linked.Token).ConfigureAwait(false);
            if (suggestion is not null)
            {
                var emitted = result.IsFinal
                    ? suggestion with { IsProvisional = false }
                    : suggestion with { IsProvisional = true };
                AnswerUpdated?.Invoke(this, new AnswerUpdatedEventArgs(emitted));
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

    private async Task CleanupCaptureAsync()
    {
        _capture.FrameCaptured -= OnFrameCaptured;
        _capture.Error -= OnCaptureError;
        try { await _capture.StopAsync().ConfigureAwait(false); }
        catch { /* ignore */ }
    }

    private void ReportError(Exception ex, bool isFatal)
    {
        LastError = ex.Message;
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
    }
}
