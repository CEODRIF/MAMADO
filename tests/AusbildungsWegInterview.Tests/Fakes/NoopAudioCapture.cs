using AusbildungsWegInterview.Core.Audio;

namespace AusbildungsWegInterview.Tests.Fakes;

/// <summary>Deterministic audio capture for tests (no real device).</summary>
public sealed class NoopAudioCapture : IAudioCapture
{
    public AudioCaptureState State { get; private set; } = AudioCaptureState.Stopped;
    public string? CurrentDeviceId { get; private set; }
    public bool StartCalled { get; private set; }
    public bool StopCalled { get; private set; }

#pragma warning disable CS0067 // required by IAudioCapture; unused in this fake
    public event EventHandler<AudioFrameEventArgs>? FrameCaptured;
    public event EventHandler<AudioDeviceEventArgs>? DeviceChanged;
    public event EventHandler<CaptureErrorEventArgs>? Error;
#pragma warning restore CS0067

    public IReadOnlyList<AudioDevice> GetOutputDevices()
        => new[] { new AudioDevice("default", "Default Output", true) };

    public Task StartAsync(string? deviceId = null, CancellationToken ct = default)
    {
        StartCalled = true;
        State = AudioCaptureState.Running;
        CurrentDeviceId = deviceId;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        StopCalled = true;
        State = AudioCaptureState.Stopped;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
