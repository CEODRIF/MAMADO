using NAudio.CoreAudioApi;
using NAudio.Wave;
using AusbildungsWegInterview.Core.Audio;

namespace AusbildungsWegInterview.Audio;

/// <summary>
/// Captures audio being played through a selected Windows OUTPUT device using
/// WASAPI loopback (NAudio <c>NAudio.Wave.WasapiLoopbackCapture</c>).
///
/// Guarantees / notes:
///  * Captures SYSTEM output, never the microphone. No mic capture path exists.
///  * System-output capture may include sound from other applications.
///  * Frames are normalized to 16-bit PCM (from IEEE-float) before emission.
///  * Device removal is detected via the OS device-change notification and
///    reported via <see cref="Error"/> (fatal); the pipeline's bounded
///    auto-restart then applies.
///  * In "follow the system default" mode (no explicit device selected), a
///    changed default device triggers an automatic capture re-open on the new
///    default, announced via <see cref="DeviceChanged"/>.
///  * Device disconnection / capture failure is surfaced as an error, never as
///    a silent "still listening" state.
/// </summary>
public sealed class LoopbackAudioCapture : IAudioCapture
{
    // Fully-qualified on purpose: NAudio also defines WasapiLoopbackCapture,
    // which would otherwise collide with this type's original name.
    private NAudio.Wave.WasapiLoopbackCapture? _capture;
    private MMDevice? _device;
    private WaveFormat _format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    private readonly DeviceChangeMonitor _monitor = new();
    private readonly object _deviceCheckGate = new();
    private System.Threading.Timer? _deviceCheckTimer;
    private bool _deviceCheckRunning;
    private bool _deviceCheckBusy;
    private string? _requestedDeviceId;

    public AudioCaptureState State { get; private set; } = AudioCaptureState.Stopped;
    public string? CurrentDeviceId { get; private set; }

    public event EventHandler<AudioFrameEventArgs>? FrameCaptured;
    public event EventHandler<AudioDeviceEventArgs>? DeviceChanged;
    public event EventHandler<CaptureErrorEventArgs>? Error;

    public IReadOnlyList<AudioDevice> GetOutputDevices()
    {
        var list = new List<AudioDevice>();
        string? defaultId = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            try { defaultId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; }
            catch { /* no default device */ }

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                list.Add(new AudioDevice(device.ID, device.FriendlyName, device.ID == defaultId));
            }
        }
        catch
        {
            // No audio devices available; return empty (caller reports a friendly error).
        }
        return list;
    }

    public Task StartAsync(string? deviceId = null, CancellationToken ct = default)
    {
        if (State == AudioCaptureState.Running)
            return Task.CompletedTask;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            MMDevice render = string.IsNullOrEmpty(deviceId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(deviceId);

            _device = render;
            var capture = new NAudio.Wave.WasapiLoopbackCapture(render);
            _format = capture.WaveFormat;
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;

            capture.StartRecording();
            _capture = capture;
            CurrentDeviceId = render.ID;
            _requestedDeviceId = deviceId;
            State = AudioCaptureState.Running;

            StartDeviceCheck();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            State = AudioCaptureState.Faulted;
            Error?.Invoke(this, new CaptureErrorEventArgs(
                new InvalidOperationException("No active audio output device available to capture.", ex), isFatal: true));
            throw new InvalidOperationException("Could not start loopback capture. Is an audio output device available?", ex);
        }
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        var capture = _capture;
        _capture = null;
        try
        {
            if (capture is not null)
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
                capture.StopRecording();
                capture.Dispose();
            }
            _device?.Dispose();
            _device = null;
        }
        catch
        {
            // best effort stop
        }
        finally
        {
            StopDeviceCheck();
            _requestedDeviceId = null;
            CurrentDeviceId = null;
            State = AudioCaptureState.Stopped;
        }
        return Task.CompletedTask;
    }

    // ---------------- device-change handling ----------------

    /// <summary>
    /// Device state is polled while capturing (enumerating render endpoints is
    /// cheap). Windows has no simple managed "device changed" event for WASAPI
    /// (SystemEvents has no DeviceChange; the Win32 path needs a hidden message
    /// window), so a short-interval poll is the robust, dependency-free choice.
    /// Worst-case detection latency for a removed/changed device: ~2 s.
    /// </summary>
    private const int DeviceCheckIntervalMs = 2000;

    private void StartDeviceCheck()
    {
        if (_deviceCheckRunning)
            return;
        _deviceCheckRunning = true;
        _deviceCheckTimer = new System.Threading.Timer(_ =>
        {
            if (State != AudioCaptureState.Running)
                return;
            try { HandleDeviceChange(); }
            catch { /* the capture error path is the safety net */ }
        }, null, DeviceCheckIntervalMs, DeviceCheckIntervalMs);
    }

    private void StopDeviceCheck()
    {
        if (!_deviceCheckRunning)
            return;
        _deviceCheckRunning = false;
        try { _deviceCheckTimer?.Dispose(); } catch { /* best effort */ }
        _deviceCheckTimer = null;
    }

    private void HandleDeviceChange()
    {
        if (State != AudioCaptureState.Running)
            return;

        // Prevent overlapping checks (the restart path itself takes time).
        lock (_deviceCheckGate)
        {
            if (_deviceCheckBusy)
                return;
            _deviceCheckBusy = true;
        }
        try
        {
            HandleDeviceChangeCore();
        }
        finally
        {
            lock (_deviceCheckGate) _deviceCheckBusy = false;
        }
    }

    private void HandleDeviceChangeCore()
    {

        string current = CurrentDeviceId ?? string.Empty;
        var activeIds = new List<string>();
        string? newDefaultId = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                activeIds.Add(device.ID);
            try { newDefaultId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; }
            catch { /* no default */ }
        }
        catch
        {
            return; // cannot probe; WASAPI will report the failure itself
        }

        var (action, newDeviceId, lostDeviceId) = _monitor.Evaluate(_requestedDeviceId, current, activeIds, newDefaultId);

        switch (action)
        {
            case DeviceChangeAction.DeviceLost:
                // The device we were capturing from disappeared. Announce and
                // raise a fatal error: the pipeline's bounded restart applies
                // (it will fail again if the device really is gone, and then
                // stop cleanly with a visible error — never "still listening").
                // Device is gone; pass null (no replacement device).
                DeviceChanged?.Invoke(this, new AudioDeviceEventArgs(null));
                State = AudioCaptureState.Faulted;
                Error?.Invoke(this, new CaptureErrorEventArgs(
                    new InvalidOperationException("Audio output device was disconnected: capture stopped."), true));
                break;

            case DeviceChangeAction.RestartOnNewDefault when newDeviceId is not null:
                // Follow-default mode: silently re-open on the new default.
                _ = RestartOnDeviceAsync(newDeviceId);
                break;
        }
    }

    private async Task RestartOnDeviceAsync(string deviceId)
    {
        if (State != AudioCaptureState.Running)
            return;
        try
        {
            await StopAsync().ConfigureAwait(false);
            await StartAsync(_requestedDeviceId).ConfigureAwait(false); // re-resolve (default moved)
            var device = GetOutputDevices().FirstOrDefault(d =>
                d.Id.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                ?? new AudioDevice(CurrentDeviceId ?? deviceId, deviceId, true);
            DeviceChanged?.Invoke(this, new AudioDeviceEventArgs(device));
        }
        catch (Exception ex)
        {
            State = AudioCaptureState.Faulted;
            Error?.Invoke(this, new CaptureErrorEventArgs(
                new InvalidOperationException("Default audio device changed but capture could not be re-opened.", ex), true));
        }
    }

    // ---------------- data path ----------------

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || State != AudioCaptureState.Running)
            return;

        byte[] pcm16 = _format.Encoding == WaveFormatEncoding.IeeeFloat && _format.BitsPerSample == 32
            ? ConvertFloat32ToPcm16(e.Buffer, e.BytesRecorded)
            : e.Buffer.AsSpan(0, e.BytesRecorded).ToArray();

        var frame = new AudioFrame(pcm16, _format.SampleRate, 16, _format.Channels);
        FrameCaptured?.Invoke(this, new AudioFrameEventArgs(frame));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            State = AudioCaptureState.Faulted;
            Error?.Invoke(this, new CaptureErrorEventArgs(e.Exception, isFatal: true));
        }
        else
        {
            State = AudioCaptureState.Stopped;
        }
    }

    private static byte[] ConvertFloat32ToPcm16(byte[] buffer, int bytes)
    {
        int samples = bytes / 4;
        var result = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            float f = BitConverter.ToSingle(buffer, i * 4);
            f = Math.Clamp(f, -1f, 1f);
            short s = (short)(f * short.MaxValue);
            result[i * 2] = (byte)(s & 0xFF);
            result[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync().ConfigureAwait(false); }
        catch { /* ignore */ }
        await Task.CompletedTask;
    }
}
