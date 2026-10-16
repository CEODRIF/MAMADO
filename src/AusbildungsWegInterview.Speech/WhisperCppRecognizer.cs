using System.Text;
using System.Runtime.InteropServices;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Speech;

namespace AusbildungsWegInterview.Speech;

/// <summary>
/// whisper.cpp-based local real-time recognizer (no API key) — NATIVE path.
///
/// Behavior:
///  * Resolves the pinned native runtime (see WhisperNative). If it is missing,
///    <see cref="PrepareAsync"/> throws <see cref="WhisperRuntimeMissingException"/>;
///    if its version does not match the pinned ABI, it throws
///    <see cref="WhisperVersionMismatchException"/>. Both surface as clear,
///    localized UI errors — there is no silent empty-transcription path.
///  * Buffers incoming 16 kHz mono PCM and transcribes when enough audio has
///    accumulated, emitting a Final result for the segment.
///  * Honors the configured language (explicit pin) or auto-detects.
///
/// No audio is persisted; the in-memory buffer is cleared per segment.
/// </summary>
public sealed class WhisperCppRecognizer : ISpeechRecognizer
{
    private readonly string _modelPath;
    private readonly int _threads;
    private readonly int _minSamples;

    private IntPtr _ctx = IntPtr.Zero;
    private readonly List<float> _pcm = new();

    public bool IsReady { get; private set; }
    public string ModelName { get; }
    /// <summary>Configured language, or — when configured as null — the language
    /// auto-detected on the first transcription call (pinned here for later calls).</summary>
    public string? Language { get; private set; }
    private bool _configuredLanguage;

    public WhisperCppRecognizer(string modelPath, int threads = 2, int minAudioMs = 1500)
    {
        _modelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
        ModelName = Path.GetFileNameWithoutExtension(modelPath);
        _threads = Math.Max(1, threads);
        _minSamples = Math.Max(160, minAudioMs * 16); // 16 kHz
    }

    public Task PrepareAsync(string model, string? language, CancellationToken ct = default)
    {
        Language = language;
        _configuredLanguage = !string.IsNullOrEmpty(language);
        try
        {
            if (!File.Exists(_modelPath))
            {
                IsReady = false;
                throw new FileNotFoundException(
                    $"Whisper model not found: '{_modelPath}'. Use the app's model downloader to fetch it explicitly.",
                    _modelPath);
            }

            // Preflight: the native runtime must exist BEFORE any P/Invoke, so the
            // user gets one clear, actionable error instead of a crash later.
            if (WhisperNative.TryLocateLibrary() is null)
            {
                IsReady = false;
                throw new WhisperRuntimeMissingException();
            }

            if (_ctx != IntPtr.Zero)
            {
                WhisperNative.whisper_free(_ctx);
                _ctx = IntPtr.Zero;
            }

            _ctx = WhisperNative.whisper_init_from_file_with_params(
                _modelPath, WhisperNative.CreateContextParams());
            if (_ctx == IntPtr.Zero)
            {
                IsReady = false;
                throw new InvalidOperationException(
                    "Failed to initialize the whisper.cpp model (the model file may be corrupt or incompatible).");
            }

            // ABI guard: this P/Invoke mirror targets the pinned major.minor. A
            // mismatched runtime would read the params struct with the wrong
            // layout (undefined behavior) — refuse instead.
            string found = WhisperNative.Version();
            if (!found.StartsWith(WhisperNative.PinnedMajorMinor + ".", StringComparison.Ordinal) &&
                found != WhisperNative.PinnedMajorMinor)
            {
                WhisperNative.whisper_free(_ctx);
                _ctx = IntPtr.Zero;
                IsReady = false;
                throw new WhisperVersionMismatchException(WhisperNative.PinnedMajorMinor, found);
            }

            IsReady = true;
        }
        catch (System.EntryPointNotFoundException)
        {
            // A runtime exists but is too old/different for this ABI (e.g. no
            // whisper_init_from_file_with_params export). Report it as a
            // version mismatch, not a raw P/Invoke crash.
            IsReady = false;
            throw new WhisperVersionMismatchException(
                WhisperNative.PinnedMajorMinor,
                "incompatible (missing pinned API exports)");
        }
        return Task.CompletedTask;
    }

    public Task<TranscriptionResult> ProcessAudioAsync(AudioFrame chunk, CancellationToken ct = default)
    {
        if (!IsReady)
            return Task.FromResult(TranscriptionResult.Empty);

        AppendPcm(chunk);

        if (_pcm.Count >= _minSamples)
        {
            string text = RunWhisper(); // throws on runtime failure (no silent empty)
            _pcm.Clear();
            if (!string.IsNullOrWhiteSpace(text))
                return Task.FromResult(TranscriptionResult.Final(text, Language));
        }
        return Task.FromResult(TranscriptionResult.Empty);
    }

    public Task<TranscriptionResult> FinalizeAsync(CancellationToken ct = default)
    {
        if (!IsReady || _pcm.Count == 0)
            return Task.FromResult(TranscriptionResult.Empty);

        string text = RunWhisper(); // throws on runtime failure (no silent empty)
        _pcm.Clear();
        return Task.FromResult(string.IsNullOrWhiteSpace(text)
            ? TranscriptionResult.Empty
            : TranscriptionResult.Final(text, Language));
    }

    private string RunWhisper()
    {
        var array = _pcm.ToArray();

        // No configured language? The pinned API's detect_language=1 flag returns
        // WITHOUT transcribing, so the first call only detects (rc 0, no
        // segments), we pin the result, and re-run the SAME audio with it —
        // the caller clears the buffer once, per RunWhisper call.
        bool detect = !_configuredLanguage && string.IsNullOrEmpty(Language);

        IntPtr langPtr = IntPtr.Zero;
        try
        {
            string? runLang = detect ? null : (Language ?? "auto");
            if (!string.IsNullOrEmpty(runLang))
                langPtr = Marshal.StringToHGlobalAnsi(runLang);

            var parameters = WhisperNative.CreateFullParams(_threads, langPtr, detect);
            // whisper_full takes the sample COUNT (array.Length is the byte size
            // of the float buffer — the pinned C API would otherwise read 2x).
            int rc = WhisperNative.whisper_full(_ctx, parameters, array, array.Length / 4);
            if (rc != 0)
                throw new InvalidOperationException($"whisper_full failed with code {rc}.");
            if (_ctx == IntPtr.Zero)
                throw new InvalidOperationException("whisper context lost during transcription.");

            if (detect)
            {
                string? detected = DetectedLanguage();
                if (!string.IsNullOrEmpty(detected))
                {
                    Language = detected;
                    _configuredLanguage = true; // pinned: never re-detect
                }
                // Transcribe the same audio now (detected language, or "auto").
                Marshal.FreeHGlobal(langPtr);
                langPtr = Marshal.StringToHGlobalAnsi(Language ?? "auto");
                var second = WhisperNative.CreateFullParams(_threads, langPtr, detectLanguage: false);
                rc = WhisperNative.whisper_full(_ctx, second, array, array.Length / 4);
                if (rc != 0)
                    throw new InvalidOperationException($"whisper_full failed with code {rc}.");
            }

            var sb = new StringBuilder();
            int n = WhisperNative.whisper_full_n_segments(_ctx);
            for (int i = 0; i < n; i++)
                sb.Append(WhisperNative.PtrToString(WhisperNative.whisper_full_get_segment_text(_ctx, i))).Append(' ');
            return sb.ToString().Trim();
        }
        finally
        {
            if (langPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(langPtr);
        }
    }

    private string? DetectedLanguage()
    {
        // NOTE: whisper_lang_id("en") is 0 in the pinned C API — only negative
        // means "unknown", so the guard must be < 0, not <= 0.
        int id = WhisperNative.whisper_full_lang_id(_ctx);
        if (id < 0)
            return null;
        return WhisperNative.PtrToString(WhisperNative.whisper_lang_str(id));
    }

    private void AppendPcm(AudioFrame chunk)
    {
        int samples = chunk.Pcm.Length / 2;
        for (int i = 0; i < samples; i++)
        {
            short s = (short)(chunk.Pcm[i * 2] | (chunk.Pcm[i * 2 + 1] << 8));
            _pcm.Add(s / 32768f);
        }
    }

    public void Reset() => _pcm.Clear();

    public ValueTask DisposeAsync()
    {
        if (_ctx != IntPtr.Zero)
        {
            try { WhisperNative.whisper_free(_ctx); }
            catch { /* best effort */ }
            _ctx = IntPtr.Zero;
        }
        IsReady = false;
        return ValueTask.CompletedTask;
    }
}
