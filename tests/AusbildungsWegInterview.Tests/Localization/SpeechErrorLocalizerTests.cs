using AusbildungsWegInterview.Localization;
using AusbildungsWegInterview.Speech;
using Xunit;

namespace AusbildungsWegInterview.Tests.Localization;

/// <summary>
/// Category mapping for runtime pipeline errors: the exact exceptions the
/// production providers throw must map to the intended localized messages in
/// ALL four UI languages, and raw text (local paths) must never leak.
/// </summary>
public class SpeechErrorLocalizerTests
{
    private static LocalizationManager Loc(string culture) => new(culture);

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("ar")]
    public void EveryCategory_IsLocalised_InAllCultures(string culture)
    {
        var loc = Loc(culture);
        var cases = new (Exception Ex, string Key)[]
        {
            // Missing device / capture failures
            (new InvalidOperationException("No active audio output device available to capture."), "ErrorNoOutputDevice"),
            (new InvalidOperationException("Could not start loopback capture. Is an audio output device available?"), "ErrorCaptureStart"),
            (new InvalidOperationException("Failed to start audio capture: Could not start loopback capture. Is an audio output device available?"), "ErrorCaptureStart"),
            (new InvalidOperationException("Audio output device was disconnected: capture stopped."), "ErrorDeviceLost"),
            (new InvalidOperationException("Default audio device changed but capture could not be re-opened."), "ErrorDeviceReopen"),
            // Missing whisper executable / model / native library
            (new FileNotFoundException("whisper.cpp executable not found at 'C:\\tools\\whisper-cli.exe'. Build whisper.cpp (or install it) and set the executable path in settings.", "C:\\tools\\whisper-cli.exe"), "ErrorWhisperExeMissing"),
            (new FileNotFoundException("whisper.cpp model not found at 'C:\\models\\ggml-base.bin'. Use the app's model downloader or set a model path.", "C:\\models\\ggml-base.bin"), "ErrorWhisperModelMissing"),
            (new FileNotFoundException("Whisper model not found: 'C:\\models\\ggml-base.bin'. Use the app's model downloader to fetch it explicitly.", "C:\\models\\ggml-base.bin"), "ErrorWhisperModelMissing"),
            (new WhisperRuntimeMissingException(), "ErrorNativeLibMissing"),
            (new WhisperVersionMismatchException("1.9", "1.5.0"), "ErrorWhisperVersionMismatch"),
            (new InvalidOperationException("Failed to initialize the whisper.cpp model (returned null context)."), "ErrorModelPrepare"),
            (new ArgumentException("Unknown model 'huge-v3'.", "name"), "ErrorUnknownModel"),
            // Runtime errors
            (new InvalidOperationException("whisper.cpp exited with code 139. some stderr"), "ErrorWhisperRun"),
            (new InvalidOperationException("Failed to start the whisper.cpp process."), "ErrorWhisperRun"),
            (new InvalidOperationException("Could not launch the whisper.cpp executable (incompatible binary or missing runtime?): 0x80070004"), "ErrorWhisperRun"),
            (new InvalidOperationException("whisper.cpp transcription timed out."), "ErrorWhisperTimeout"),
            (new InvalidOperationException("Ollama error: model not found"), "ErrorOllama"),
            (new InvalidOperationException("Stop listening before changing these settings."), "ErrorStopBeforeSettings"),
            (new InvalidOperationException("something entirely unexpected"), "ErrorRuntime"),
        };

        foreach (var (ex, key) in cases)
        {
            string expected = loc[key];
            string actual = SpeechErrorLocalizer.Localize(loc, ex);
            Assert.True(!string.IsNullOrWhiteSpace(actual), $"localization is empty for {key} in {culture}");
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void WrappedErrors_MapToTheInnerCategory_NotTheGenericWrapper()
    {
        // The pipeline wraps provider errors: "Failed to prepare local models: <inner>".
        // The specific inner category must win over the generic wrapper.
        var loc = Loc("en");

        var modelMissing = new FileNotFoundException(
            "whisper.cpp model not found at 'C:\\models\\ggml-base.bin'. Use the app's model downloader or set a model path.",
            "C:\\models\\ggml-base.bin");
        var wrappedPrepare = new InvalidOperationException("Failed to prepare local models: " + modelMissing.Message, modelMissing);
        Assert.Equal(loc["ErrorWhisperModelMissing"], SpeechErrorLocalizer.Localize(loc, wrappedPrepare));

        var noDevice = new InvalidOperationException("No active audio output device available to capture.");
        var wrappedCapture = new InvalidOperationException("Failed to start audio capture: " + noDevice.Message, noDevice);
        Assert.Equal(loc["ErrorNoOutputDevice"], SpeechErrorLocalizer.Localize(loc, wrappedCapture));
    }

    [Fact]
    public void LocalPaths_NeverLeakIntoTheLocalizedMessage()
    {
        var secret = "C:\\Users\\alice\\SecretFolder\\ggml-base.bin";
        var ex = new FileNotFoundException(
            $"whisper.cpp model not found at '{secret}'. Use the app's model downloader or set a model path.", secret);

        foreach (var culture in new[] { "en", "de", "fr", "ar" })
        {
            var loc = Loc(culture);
            string msg = SpeechErrorLocalizer.Localize(loc, ex);
            Assert.DoesNotContain(secret, msg);
            Assert.DoesNotContain("alice", msg);
            Assert.DoesNotContain("SecretFolder", msg);
        }
    }

    [Fact]
    public void NullAndEmpty_AreSafe()
    {
        var loc = Loc("en");
        Assert.Equal(loc["ErrorRuntime"], SpeechErrorLocalizer.Localize(loc, null));
    }

    [Fact]
    public void UnknownException_DoesNotEchoRawText()
    {
        var loc = Loc("en");
        string raw = "some very specific internal detail C:\\x\\y";
        var msg = SpeechErrorLocalizer.Localize(loc, new System.IO.IOException(raw));
        Assert.Equal(loc["ErrorRuntime"], msg);
        Assert.DoesNotContain(raw, msg);
    }
}
