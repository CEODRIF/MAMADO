using AusbildungsWegInterview.Speech;

namespace AusbildungsWegInterview.App;

/// <summary>State of one first-run prerequisite item.</summary>
public enum SetupItemState
{
    /// <summary>Present and usable.</summary>
    Ok,

    /// <summary>Required item absent (or optional item's model not pulled).</summary>
    Missing,

    /// <summary>Present but incompatible (wrong native runtime version).</summary>
    Mismatch,

    /// <summary>Optional runtime not running (Ollama off).</summary>
    OptionalUnavailable
}

/// <summary>One prerequisite line for the setup panel.</summary>
public sealed record SetupItem(SetupItemState State, string? Detail);

/// <summary>
/// The full first-run prerequisite report. "Required" = native runtime +
/// speech model (transcription is the core feature). Ollama is optional:
/// its absence must never block the app.
/// </summary>
public sealed record RuntimeReport(SetupItem Runtime, SetupItem SpeechModel, SetupItem Ollama)
{
    /// <summary>True when transcription itself cannot work yet.</summary>
    public bool HasRequiredIssues =>
        Runtime.State is SetupItemState.Missing or SetupItemState.Mismatch
        || SpeechModel.State == SetupItemState.Missing;

    /// <summary>True when ANY line should be shown (required issues OR optional gaps).</summary>
    public bool HasAnyIssues =>
        HasRequiredIssues
        || Ollama.State is SetupItemState.Missing or SetupItemState.OptionalUnavailable;
}

/// <summary>
/// Pure, WPF-free classifier that maps raw probe results to the setup report.
/// All UI localization happens in the view model; this class only decides
/// states, so every diagnostic combination is unit-testable on any OS.
/// </summary>
public static class RuntimeDiagnostics
{
    public static SetupItem ClassifyRuntime(WhisperRuntimeState state, string? foundVersion) => state switch
    {
        WhisperRuntimeState.Ready => new SetupItem(SetupItemState.Ok, foundVersion),
        WhisperRuntimeState.VersionMismatch => new SetupItem(SetupItemState.Mismatch, foundVersion),
        _ => new SetupItem(SetupItemState.Missing, null)
    };

    public static SetupItem ClassifySpeechModel(bool fileExists)
        => fileExists
            ? new SetupItem(SetupItemState.Ok, null)
            : new SetupItem(SetupItemState.Missing, null);

    public static SetupItem ClassifyOllama(bool serverAvailable, bool modelAvailable) =>
        !serverAvailable
            ? new SetupItem(SetupItemState.OptionalUnavailable, null)
            : modelAvailable
                ? new SetupItem(SetupItemState.Ok, null)
                : new SetupItem(SetupItemState.Missing, null);

    public static RuntimeReport Build(
        WhisperRuntimeState runtime, string? runtimeVersion,
        bool modelExists,
        bool ollamaServerAvailable, bool ollamaModelAvailable)
        => new(
            ClassifyRuntime(runtime, runtimeVersion),
            ClassifySpeechModel(modelExists),
            ClassifyOllama(ollamaServerAvailable, ollamaModelAvailable));
}
