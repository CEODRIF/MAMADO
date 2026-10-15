namespace AusbildungsWegInterview.Core.Audio;

/// <summary>Raised for each captured audio chunk.</summary>
public sealed class AudioFrameEventArgs(AudioFrame frame) : EventArgs
{
    public AudioFrame Frame { get; } = frame;
}

/// <summary>Raised when the active output device changes (plug/unplug, default change).</summary>
public sealed class AudioDeviceEventArgs(AudioDevice? device) : EventArgs
{
    public AudioDevice? Device { get; } = device;
}

/// <summary>Raised when the capture source reports a non-fatal or fatal error.</summary>
public sealed class CaptureErrorEventArgs(Exception error, bool isFatal = false) : EventArgs
{
    public Exception Error { get; } = error;
    public bool IsFatal { get; } = isFatal;
}
