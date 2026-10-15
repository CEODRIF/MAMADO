namespace AusbildungsWegInterview.Core.Audio;

/// <summary>Lifecycle of a capture session.</summary>
public enum AudioCaptureState
{
    Stopped = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Faulted = 4
}
