namespace AusbildungsWegInterview.Core.Audio;

/// <summary>
/// Abstraction over a system-audio (loopback) capture source.
///
/// Contract:
///  * Captures the signal being sent to a selected OUTPUT device, never a microphone.
///  * Emits 16-bit PCM via <see cref="FrameCaptured"/>.
///  * Reports missing devices, device changes, mute and capture failures via events/exceptions.
///  * Never persists audio unless explicitly configured to do so by the caller.
/// </summary>
public interface IAudioCapture : IAsyncDisposable
{
    /// <summary>Current capture lifecycle state.</summary>
    AudioCaptureState State { get; }

    /// <summary>Id of the device currently being captured, or null when stopped.</summary>
    string? CurrentDeviceId { get; }

    /// <summary>Raised for each chunk of captured audio.</summary>
    event EventHandler<AudioFrameEventArgs>? FrameCaptured;

    /// <summary>Raised when the active output device changes.</summary>
    event EventHandler<AudioDeviceEventArgs>? DeviceChanged;

    /// <summary>Raised on capture errors (device removed, WASAPI failure, etc.).</summary>
    event EventHandler<CaptureErrorEventArgs>? Error;

    /// <summary>Lists capturable output devices. Never throws on an empty system.</summary>
    IReadOnlyList<AudioDevice> GetOutputDevices();

    /// <summary>
    /// Starts capture on the given output device, or the system default when <paramref name="deviceId"/> is null.
    /// </summary>
    Task StartAsync(string? deviceId = null, CancellationToken ct = default);

    /// <summary>Stops capture and releases the audio session (idempotent).</summary>
    Task StopAsync(CancellationToken ct = default);
}
