using System.Text;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Speech;

namespace AusbildungsWegInterview.Speech;

/// <summary>
/// whisper.cpp-based local real-time recognizer (no API key).
///
/// Behavior:
///  * Loads the native libwhisper at runtime. If the native library or the model
///    file is missing, <see cref="IsReady"/> is false and <see cref="PrepareAsync"/>
///    throws a clear, actionable error that the pipeline surfaces to the user.
///  * Buffers incoming 16 kHz mono PCM and transcribes when enough audio has
///    accumulated, emitting a Final result for the segment.
///  * Supports a model name and language; explicit language pinning via the
///    whisper_state API is completed in the transcription milestone (Step 5).
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
    public string? Language { get; private set; }

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
        try
        {
            if (!File.Exists(_modelPath))
            {
                IsReady = false;
                throw new FileNotFoundException(
                    $"Whisper model not found: '{_modelPath}'. Use the app's model downloader to fetch it explicitly.",
                    _modelPath);
            }

            if (_ctx != IntPtr.Zero)
            {
                WhisperNative.whisper_free(_ctx);
                _ctx = IntPtr.Zero;
            }

            _ctx = WhisperNative.whisper_init_from_file_v2(_modelPath, _threads);
            if (_ctx == IntPtr.Zero)
            {
                IsReady = false;
                throw new InvalidOperationException("Failed to initialize the whisper.cpp model (returned null context).");
            }
            IsReady = true;
        }
        catch (System.DllNotFoundException ex)
        {
            IsReady = false;
            throw new InvalidOperationException(
                "The native whisper runtime (libwhisper) is not available on this machine. " +
                "Build whisper.cpp and place the shared library next to the app to enable transcription.", ex);
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
            string text = RunWhisper();
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

        string text = RunWhisper();
        _pcm.Clear();
        return Task.FromResult(string.IsNullOrWhiteSpace(text)
            ? TranscriptionResult.Empty
            : TranscriptionResult.Final(text, Language));
    }

    private string RunWhisper()
    {
        int rc = 0;
        try
        {
            var array = _pcm.ToArray();
            var @params = WhisperNative.CreateDefaultParams(_threads);
            rc = WhisperNative.whisper_full(_ctx, ref @params, array, array.Length);
        }
        catch (System.DllNotFoundException)
        {
            IsReady = false;
            return string.Empty;
        }

        if (rc != 0 || _ctx == IntPtr.Zero)
            return string.Empty;

        var sb = new StringBuilder();
        int n = WhisperNative.whisper_full_n_segments(_ctx);
        for (int i = 0; i < n; i++)
        {
            sb.Append(WhisperNative.PtrToString(WhisperNative.whisper_full_get_segment_text(_ctx, i))).Append(' ');
        }
        return sb.ToString().Trim();
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
        return ValueTask.CompletedTask;
    }
}
