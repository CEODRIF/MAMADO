using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Speech;
using AusbildungsWegInterview.Speech;
using Xunit;

namespace AusbildungsWegInterview.Tests.Speech;

/// <summary>
/// These tests exercise the graceful-degradation path of the whisper adapter
/// WITHOUT the native library or a model present (works on any OS). Live
/// transcription against a real whisper.cpp build is verified in Step 5.
/// </summary>
public class WhisperCppRecognizerTests
{
    [Fact]
    public void StartsNotReady()
    {
        var rec = new WhisperCppRecognizer("/nonexistent/base.bin");
        Assert.False(rec.IsReady);
        Assert.Equal("base", rec.ModelName);
    }

    [Fact]
    public void Prepare_MissingModel_ThrowsFileNotFound()
    {
        var rec = new WhisperCppRecognizer("/nonexistent/base.bin");
        var ex = Assert.Throws<FileNotFoundException>(() =>
            rec.PrepareAsync("base", null).GetAwaiter().GetResult());
        Assert.False(rec.IsReady);
        Assert.NotNull(ex);
    }

    [Fact]
    public void Prepare_ModelPresentButNoRuntime_ThrowsActionableError()
    {
        // Deterministic "missing runtime" path: a model file exists, but no
        // libwhisper/whisper.dll is resolvable in the test environment. The
        // recognizer must fail LOUDLY with an actionable, localized-category
        // error — never silently return empty transcripts.
        string? previous = WhisperNative.LibraryOverridePath;
        string tmp = Path.Combine(Path.GetTempPath(), $"aw-model-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(tmp, new byte[] { 1, 2, 3, 4 });
        try
        {
            WhisperNative.LibraryOverridePath = Path.Combine(Path.GetTempPath(), "aw-nonexistent-override.dll");
            var rec = new WhisperCppRecognizer(tmp);
            var ex = Assert.Throws<WhisperRuntimeMissingException>(() =>
                rec.PrepareAsync("tiny", null).GetAwaiter().GetResult());
            Assert.False(rec.IsReady);
            Assert.Contains("whisper runtime library", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("build-whisper", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            WhisperNative.LibraryOverridePath = previous;
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task Process_NotReady_ReturnsEmpty()
    {
        var rec = new WhisperCppRecognizer("/nonexistent/base.bin");
        var frame = MakeFrame(4000);
        var result = await rec.ProcessAudioAsync(frame);
        Assert.Equal(TranscriptionResult.Empty, result);
    }

    [Fact]
    public async Task Dispose_DoesNotThrow_WhenNeverPrepared()
    {
        var rec = new WhisperCppRecognizer("/nonexistent/base.bin");
        await rec.DisposeAsync();
    }

    private static AudioFrame MakeFrame(int bytes)
    {
        var b = new byte[bytes];
        for (int i = 0; i < b.Length; i += 2) { b[i] = 0x00; b[i + 1] = 0x20; }
        return new AudioFrame(b, 16000);
    }
}
