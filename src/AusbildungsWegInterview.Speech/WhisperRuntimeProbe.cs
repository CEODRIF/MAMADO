namespace AusbildungsWegInterview.Speech;

/// <summary>Outcome of a non-destructive native runtime check.</summary>
public enum WhisperRuntimeState
{
    /// <summary>No whisper shared library could be located next to the app.</summary>
    Missing,

    /// <summary>Library found and its version matches the pinned ABI (1.9.x).</summary>
    Ready,

    /// <summary>Library found but its version does NOT match the pinned ABI.</summary>
    VersionMismatch
}

/// <summary>Result of <see cref="WhisperRuntimeProbe.Probe"/>.</summary>
public sealed record WhisperRuntimeProbeResult(WhisperRuntimeState State, string? FoundVersion);

/// <summary>
/// Non-destructive first-run check for the bundled whisper.cpp runtime:
/// locates the shared library (the same resolver the recognizer uses) and
/// compares whisper_version() against the pinned major.minor. Never loads a
/// model, never transcribes, never throws — safe to call on startup and on
/// every settings refresh.
/// </summary>
public static class WhisperRuntimeProbe
{
    public static WhisperRuntimeProbeResult Probe()
    {
        try
        {
            string? library = WhisperNative.TryLocateLibrary();
            if (library is null)
                return new WhisperRuntimeProbeResult(WhisperRuntimeState.Missing, null);

            string version = WhisperNative.Version();
            return new WhisperRuntimeProbeResult(
                IsPinnedVersion(version) ? WhisperRuntimeState.Ready : WhisperRuntimeState.VersionMismatch,
                version);
        }
        catch
        {
            // A probe must never take the app down: treat any load failure
            // as "missing" (the recognizer will surface the real error later).
            return new WhisperRuntimeProbeResult(WhisperRuntimeState.Missing, null);
        }
    }

    /// <summary>
    /// True when the reported version is the pinned build: exactly the
    /// pinned version, or anything else with the pinned major.minor
    /// (e.g. "1.9.5-dev" from a non-tagged checkout).
    /// </summary>
    // Same acceptance rule as the recognizer's ABI guard: the pinned version,
    // a bare pinned major.minor, or any 1.9.x.
    internal static bool IsPinnedVersion(string? version)
        => !string.IsNullOrEmpty(version)
           && (version == WhisperNative.PinnedVersion
               || version == WhisperNative.PinnedMajorMinor
               || version.StartsWith(WhisperNative.PinnedMajorMinor + ".", StringComparison.Ordinal));
}
