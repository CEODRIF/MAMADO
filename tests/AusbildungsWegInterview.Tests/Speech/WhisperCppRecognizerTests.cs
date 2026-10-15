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
