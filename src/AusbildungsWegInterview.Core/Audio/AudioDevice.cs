namespace AusbildungsWegInterview.Core.Audio;

/// <summary>
/// A capturable audio output device (system sound). Loopback capture reads the
/// signal that is being *sent to* an output device, not a microphone.
/// </summary>
public sealed record AudioDevice(string Id, string Name, bool IsDefault)
{
    public override string ToString() => IsDefault ? $"{Name} (default)" : Name;
}
