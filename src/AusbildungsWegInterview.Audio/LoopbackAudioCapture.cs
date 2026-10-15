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
///  * Device removal / capture failure is reported via <see cref="Error"/> and
///    surfaced as a fatal state; the UI shows a clear message.
/// </summary>
public sealed class LoopbackAudioCapture : IAudioCapture
{
    // Fully-qualified on purpose: NAudio also defines WasapiLoopbackCapture,
    // which would otherwise collide with this type's original name.
    private NAudio.Wave.WasapiLoopbackCapture? _capture;
    private MMDevice? _device;
    private WaveFormat _format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    public AudioCaptureState State { get; private set; } = AudioCaptureState.Stopped;
    public string? CurrentDeviceId { get; private set; }

    public event EventHandler<AudioFrameEventArgs>? FrameCaptured;
#pragma warning disable CS0067 // live device-change notification is a Step-4 refinement
    public event EventHandler<AudioDeviceEventArgs>? DeviceChanged;
#pragma warning restore CS0067
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
            State = AudioCaptureState.Running;
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
            CurrentDeviceId = null;
            State = AudioCaptureState.Stopped;
        }
        return Task.CompletedTask;
    }

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
