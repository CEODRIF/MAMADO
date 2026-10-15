namespace AusbildungsWegInterview.Core.Pipeline;

/// <summary>High-level state machine of the interview pipeline, surfaced to the UI.</summary>
public enum PipelineState
{
    Idle = 0,
    Preparing = 1,
    Listening = 2,
    Processing = 3,
    Stopping = 4,
    Stopped = 5,
    Faulted = 6
}
