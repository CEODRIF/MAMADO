using AusbildungsWegInterview.Core.Audio;

namespace AusbildungsWegInterview.Tests.Fakes;

/// <summary>
/// Deterministic capture fake that fails its first <see cref="FailFirstStarts"/>
/// StartAsync calls (simulating a missing/locked audio device) and succeeds
/// afterwards (device reconnected). Also supports hanging StartAsync via
/// <see cref="StartGate"/> for start-window race tests.
/// </summary>
public sealed class FailingAudioCapture : IAudioCapture
{
    public int FailFirstStarts { get; set; }
    /// <summary>When non-null, StartAsync awaits this gate before succeeding (hang test).</summary>
    public TaskCompletionSource? StartGate { get; set; }

    public AudioCaptureState State { get; private set; } = AudioCaptureState.Stopped;
    public string? CurrentDeviceId { get; private set; }
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }

#pragma warning disable CS0067 // required by IAudioCapture; unused in this fake
    public event EventHandler<AudioFrameEventArgs>? FrameCaptured;
    public event EventHandler<AudioDeviceEventArgs>? DeviceChanged;
    public event EventHandler<CaptureErrorEventArgs>? Error;
#pragma warning restore CS0067

    public IReadOnlyList<AudioDevice> GetOutputDevices()
        => new[] { new AudioDevice("default", "Default Output", true) };

    public async Task StartAsync(string? deviceId = null, CancellationToken ct = default)
    {
        StartCount++;
        if (StartGate is not null)
            await StartGate.Task.WaitAsync(ct);
        if (StartCount <= FailFirstStarts)
        {
            ct.ThrowIfCancellationRequested();
            State = AudioCaptureState.Faulted;
            throw new InvalidOperationException("Simulated capture device failure.");
        }
        CurrentDeviceId = deviceId;
        State = AudioCaptureState.Running;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        StopCount++;
        State = AudioCaptureState.Stopped;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
