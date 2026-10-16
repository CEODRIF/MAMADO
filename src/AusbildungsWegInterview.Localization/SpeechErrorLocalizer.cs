namespace AusbildungsWegInterview.Localization;

/// <summary>
/// Maps runtime exceptions from the capture / speech / translation / answer
/// layers to short, LOCALIZED, actionable UI messages (all four UI languages).
///
/// Design rules:
///  * Classification is category-based: exception type + well-known message
///    markers emitted by the app's own providers. No free-form parsing.
///  * The raw exception text is NEVER shown to the user: it can contain local
///    file paths (privacy) and is English-only (localization).
///  * The exception chain is walked; specific categories beat the generic
///    wrappers ("Failed to prepare local models: ...", "Failed to start audio
///    capture: ..."), so the most precise message wins.
///  * Anything unrecognized maps to the generic runtime message, never to raw
///    text.
/// </summary>
public static class SpeechErrorLocalizer
{
    /// <summary>Returns the localized error message for <paramref name="error"/> (null-safe).</summary>
    public static string Localize(LocalizationManager loc, Exception? error)
    {
        if (error is null)
            return loc["ErrorRuntime"];

        var chain = Chain(error).ToList();

        // Pass 1: specific categories (most precise wins, outermost first).
        foreach (var ex in chain)
        {
            if (ClassifySpecific(ex) is { } key)
                return loc[key];
        }

        // Pass 2: generic wrappers (only if no specific category matched anywhere).
        foreach (var ex in chain)
        {
            if (ClassifyGeneric(ex) is { } key)
                return loc[key];
        }

        return loc["ErrorRuntime"];
    }

    private static IEnumerable<Exception> Chain(Exception ex)
    {
        var current = ex;
        for (int guard = 0; current is not null && guard < 10; guard++)
        {
            yield return current;
            current = current.InnerException;
        }
    }

    private static string? ClassifySpecific(Exception ex)
    {
        string m = ex.Message ?? string.Empty;

        if (ex is FileNotFoundException)
        {
            if (Contains(m, "whisper.cpp executable"))
                return "ErrorWhisperExeMissing";
            if (Contains(m, "model not found"))
                return "ErrorWhisperModelMissing";
        }

        if (Contains(m, "Stop listening before changing these settings"))
            return "ErrorStopBeforeSettings";
        if (Contains(m, "disconnected"))
            return "ErrorDeviceLost";
        if (Contains(m, "could not be re-opened"))
            return "ErrorDeviceReopen";
        if (Contains(m, "whisper runtime library"))
            return "ErrorNativeLibMissing";
        if (Contains(m, "whisper runtime version mismatch"))
            return "ErrorWhisperVersionMismatch";
        if (Contains(m, "Failed to initialize the whisper.cpp model"))
            return "ErrorModelPrepare";
        if (Contains(m, "Unknown model"))
            return "ErrorUnknownModel";
        if (Contains(m, "timed out"))
            return "ErrorWhisperTimeout";
        if (Contains(m, "whisper.cpp exited with code") ||
            Contains(m, "Failed to start the whisper.cpp process") ||
            Contains(m, "Could not launch the whisper.cpp executable"))
            return "ErrorWhisperRun";
        if (Contains(m, "Ollama"))
            return "ErrorOllama";
        if (Contains(m, "Failed to start the translation process"))
            return "ErrorRuntime";

        return null;
    }

    private static string? ClassifyGeneric(Exception ex)
    {
        string m = ex.Message ?? string.Empty;

        // The pipeline wraps the capture error ("Failed to start audio capture: No
        // active audio output device..."), so check the precise no-device marker
        // before the generic capture marker.
        if (Contains(m, "No active audio output device"))
            return "ErrorNoOutputDevice";
        if (Contains(m, "Could not start loopback capture") ||
            Contains(m, "Failed to start audio capture"))
            return "ErrorCaptureStart";
        if (Contains(m, "Failed to prepare local models"))
            return "ErrorModelPrepare";

        return null;
    }

    private static bool Contains(string haystack, string needle)
        => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
