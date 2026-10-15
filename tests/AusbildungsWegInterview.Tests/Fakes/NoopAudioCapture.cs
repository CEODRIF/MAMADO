using AusbildungsWegInterview.Core.Audio;

namespace AusbildungsWegInterview.Tests.Fakes;

/// <summary>Deterministic audio capture for tests (no real device).</summary>
public sealed class NoopAudioCapture : IAudioCapture
{
    public AudioCaptureState State { get; private set; } = AudioCaptureState.Stopped;
    public string? CurrentDeviceId { get; private set; }
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public bool StartCalled => StartCount > 0;
    public bool StopCalled => StopCount > 0;

#pragma warning disable CS0067 // required by IAudioCapture; unused in this fake
    public event EventHandler<AudioFrameEventArgs>? FrameCaptured;
    public event EventHandler<AudioDeviceEventArgs>? DeviceChanged;
    public event EventHandler<CaptureErrorEventArgs>? Error;
#pragma warning restore CS0067

    public IReadOnlyList<AudioDevice> GetOutputDevices()
        => new[] { new AudioDevice("default", "Default Output", true) };

    public Task StartAsync(string? deviceId = null, CancellationToken ct = default)
    {
        StartCount++;
        State = AudioCaptureState.Running;
        CurrentDeviceId = deviceId;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        StopCount++;
        State = AudioCaptureState.Stopped;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
