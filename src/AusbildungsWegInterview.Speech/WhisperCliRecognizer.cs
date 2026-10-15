using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Speech;

namespace AusbildungsWegInterview.Speech;

/// <summary>
/// whisper.cpp integration via the <c>whisper-cli</c> (or legacy <c>main</c>)
/// executable, driven as a local subprocess. This is the documented, portable
/// setup: point the app at a whisper.cpp build and a model file.
///
/// Honest behavior notes:
///  * This transcribes DISCRETE segments of accumulated audio (it re-runs whisper
///    on the current segment buffer on a cadence). It is NOT token-level streaming.
///  * Partial results are provisional; <see cref="FinalizeAsync"/> produces the
///    Final result for the segment (the pipeline calls it on sustained silence).
///  * The full segment text is re-transcribed each pass; the pipeline REPLACES the
///    shown text (no accumulation), and deduplicates unchanged text.
///
/// Everything runs locally. No audio, model, or transcript ever leaves the machine
/// except the local subprocess invocation.
/// </summary>
public sealed class WhisperCliRecognizer : ISpeechRecognizer
{
    private readonly string _executable;
    private readonly string _modelPath;
    private readonly int _threads;
    private readonly double _partialIntervalSeconds;
    private readonly double _maxSegmentSeconds;
    private readonly int _timeoutMs;

    private readonly List<byte> _audio = new();
    private readonly string _workDir;
    private double _audioSeconds;
    private double _lastInferSeconds;
    private int _sampleRate = 16000;
    private TranscriptionResult? _lastResult;

    private bool _workDirOwned;

    public bool IsReady { get; private set; }
    public string ModelName { get; }
    public string? Language { get; private set; }

    public WhisperCliRecognizer(
        string executable,
        string modelPath,
        int threads = 2,
        double partialIntervalSeconds = 1.0,
        double maxSegmentSeconds = 30.0,
        int timeoutMs = 30000,
        string? workDir = null)
    {
        _executable = executable ?? throw new ArgumentNullException(nameof(executable));
        _modelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
        _threads = Math.Max(1, threads);
        _partialIntervalSeconds = Math.Max(0.2, partialIntervalSeconds);
        _maxSegmentSeconds = Math.Max(1.0, maxSegmentSeconds);
        _timeoutMs = Math.Max(1000, timeoutMs);
        ModelName = Path.GetFileNameWithoutExtension(modelPath);
        _workDir = workDir ?? Path.Combine(Path.GetTempPath(), "AusbildungsWegInterview-speech", Guid.NewGuid().ToString("N"));
        _workDirOwned = workDir is null;
    }

    public Task PrepareAsync(string model, string? language, CancellationToken ct = default)
    {
        Language = NormalizeLanguage(language);

        if (!File.Exists(_executable))
        {
            IsReady = false;
            throw new FileNotFoundException(
                $"whisper.cpp executable not found at '{_executable}'. " +
                "Build whisper.cpp (or install it) and set the executable path in settings.", _executable);
        }

        if (!File.Exists(_modelPath))
        {
            IsReady = false;
            throw new FileNotFoundException(
                $"whisper.cpp model not found at '{_modelPath}'. Use the app's model downloader or set a model path.", _modelPath);
        }

        Directory.CreateDirectory(_workDir);
        IsReady = true;
        return Task.CompletedTask;
    }

    public async Task<TranscriptionResult> ProcessAudioAsync(AudioFrame chunk, CancellationToken ct = default)
    {
        if (!IsReady)
            return TranscriptionResult.Empty;

        AppendAudio(chunk);

        bool due = (_audioSeconds - _lastInferSeconds) >= _partialIntervalSeconds;
        if (!due)
            return _lastResult ?? TranscriptionResult.Empty;

        _lastInferSeconds = _audioSeconds;
        try
        {
            _lastResult = await RunWhisperAsync(final: false, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Keep the previous result; surface nothing here (pipeline reports errors).
        }
        return _lastResult ?? TranscriptionResult.Empty;
    }

    public Task<TranscriptionResult> FinalizeAsync(CancellationToken ct = default)
    {
        if (!IsReady)
            return Task.FromResult(TranscriptionResult.Empty);
        return RunWhisperAsync(final: true, ct);
    }

    public void Reset()
    {
        _audio.Clear();
        _audioSeconds = 0;
        _lastInferSeconds = 0;
        _lastResult = null;
    }

    private async Task<TranscriptionResult> RunWhisperAsync(bool final, CancellationToken ct)
    {
        if (_audio.Count == 0)
            return TranscriptionResult.Empty;

        string wavPath = Path.Combine(_workDir, "segment.wav");
        string prefix = Path.Combine(_workDir, "segment");
        WavWriter.Write16BitMono(wavPath, _audio.ToArray(), _sampleRate);

        var psi = new ProcessStartInfo
        {
            FileName = _executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-m"); psi.ArgumentList.Add(_modelPath);
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(wavPath);
        if (!string.IsNullOrEmpty(Language))
        {
            psi.ArgumentList.Add("-l"); psi.ArgumentList.Add(Language);
        }
        psi.ArgumentList.Add("-oj");
        psi.ArgumentList.Add("-of"); psi.ArgumentList.Add(prefix);
        psi.ArgumentList.Add("-t"); psi.ArgumentList.Add(_threads.ToString());
        psi.ArgumentList.Add("-np"); // no prints (keeps stdout quiet)

        Process proc;
        try
        {
            proc = Process.Start(psi)
                  ?? throw new InvalidOperationException("Failed to start the whisper.cpp process.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(
                "Could not launch the whisper.cpp executable (incompatible binary or missing runtime?): " + ex.Message, ex);
        }

        using (proc)
        {
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_timeoutMs);

            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                throw new InvalidOperationException("whisper.cpp transcription timed out or was cancelled.", null);
            }

            string stderr = await stderrTask.ConfigureAwait(false);
            string jsonPath = prefix + ".json";
            string json = File.Exists(jsonPath) ? File.ReadAllText(jsonPath) : string.Empty;

            if (proc.ExitCode != 0 && string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException(
                    $"whisper.cpp exited with code {proc.ExitCode}. {Truncate(stderr.Trim(), 300)}");
            }

            try { File.Delete(jsonPath); } catch { }
            try { File.Delete(prefix + ".txt"); } catch { }

            var (text, language, timestamps) = ParseWhisperJson(json);
            var kind = final ? TranscriptKind.Final : TranscriptKind.Partial;
            return new TranscriptionResult(text, kind, language ?? Language, 0, timestamps);
        }
    }

    private static (string Text, string? Language, string? Timestamps) ParseWhisperJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (string.Empty, null, null);

        try
        {
            using var doc = JsonDocument.Parse(json);
            string? language = null;
            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("language", out var lang) && lang.ValueKind == JsonValueKind.String)
            {
                language = lang.GetString();
            }

            var sb = new StringBuilder();
            double? from = null;
            double? to = null;

            // The transcription array is at the TOP level in whisper.cpp JSON output.
            var transcription = doc.RootElement.TryGetProperty("transcription", out var t) ? t : default;
            if (transcription.ValueKind == JsonValueKind.Array)
            {
                foreach (var seg in transcription.EnumerateArray())
                {
                    if (seg.TryGetProperty("text", out var textEl))
                        sb.Append(textEl.GetString());
                    if (seg.TryGetProperty("offsets", out var off))
                    {
                        if (off.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.Number)
                            from ??= f.GetDouble() / 1000.0;
                        if (off.TryGetProperty("to", out var e) && e.ValueKind == JsonValueKind.Number)
                            to = e.GetDouble() / 1000.0;
                    }
                }
            }

            string? ts = (from.HasValue && to.HasValue)
                ? $"[{from.Value:F2}s -> {to.Value:F2}s]"
                : null;
            return (sb.ToString().Trim(), language, ts);
        }
        catch (JsonException)
        {
            return (string.Empty, null, null);
        }
    }

    private void AppendAudio(AudioFrame chunk)
    {
        if (chunk.BitsPerSample != 16 || chunk.Channels != 1)
            throw new ArgumentException("WhisperCliRecognizer expects 16-bit mono PCM (pipeline guarantees this).");

        _sampleRate = chunk.SampleRate;
        _audio.AddRange(chunk.Pcm);
        _audioSeconds += chunk.DurationMs / 1000.0;

        // Bound memory: keep only the most recent maxSegmentSeconds.
        int maxBytes = (int)(_maxSegmentSeconds * _sampleRate * 2);
        if (_audio.Count > maxBytes)
        {
            int drop = _audio.Count - maxBytes;
            _audio.RemoveRange(0, drop);
            _audioSeconds = maxBytes / (double)(_sampleRate * 2);
        }
    }

    private static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || language.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
            return null; // let whisper auto-detect
        return language.Trim();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_workDir) && _workDirOwned)
                Directory.Delete(_workDir, recursive: true);
        }
        catch { /* best effort */ }
        await Task.CompletedTask;
    }
}
