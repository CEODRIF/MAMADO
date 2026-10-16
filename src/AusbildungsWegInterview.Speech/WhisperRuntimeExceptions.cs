namespace AusbildungsWegInterview.Speech;

/// <summary>
/// The native whisper runtime (whisper.dll / libwhisper.so + ggml) could not be
/// located. Actionable by design: rebuild the package with
/// scripts/build-whisper.ps1 or place the pinned runtime next to the app.
/// </summary>
public sealed class WhisperRuntimeMissingException : Exception
{
    public WhisperRuntimeMissingException()
        : base("The whisper runtime library (whisper.dll on Windows, libwhisper.so on Linux) " +
               "could not be found next to the application. Rebuild the distribution package " +
               "(scripts\\build-whisper.ps1) or copy the pinned whisper.cpp runtime next to the app.")
    {
    }

    public WhisperRuntimeMissingException(string message) : base(message) { }
}

/// <summary>
/// A whisper runtime was found, but its version does not match the pinned ABI
/// this build was compiled against. Running it would be undefined behavior,
/// so the app refuses and says exactly what it found vs. what it needs.
/// </summary>
public sealed class WhisperVersionMismatchException : Exception
{
    public WhisperVersionMismatchException(string expected, string found)
        : base($"whisper runtime version mismatch: expected {expected}, found {found}. " +
               $"Rebuild the runtime from the pinned whisper.cpp version (scripts\\build-whisper.ps1).")
    {
        Expected = expected;
        Found = found;
    }

    public string Expected { get; }
    public string Found { get; }
}
