using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Speech;
using AusbildungsWegInterview.Tests.Fakes;
using Xunit;
using Xunit.Abstractions;

namespace AusbildungsWegInterview.Tests.Speech;

/// <summary>
/// REAL native P/Invoke transcription against a locally built whisper shared
/// library (soft-skip when the build is unavailable, same pattern as the CLI
/// tests). This is the end-to-end proof that the pinned ABI mirror is correct.
/// </summary>
public class WhisperNativeSmokeTests
{
    private readonly ITestOutputHelper _output;
    public WhisperNativeSmokeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task NativeTranscription_Jfk_ProducesText()
    {
        var loc = LocalWhisperAssets.FindNative();
        if (loc is null)
        {
            _output.WriteLine("SKIPPED: no local libwhisper build found (set AUSBILDUNGSWEG_WHISPER_LIB to run).");
            return;
        }

        WhisperNative.LibraryOverridePath = loc.Lib;
        try
        {
            await using var rec = new WhisperCppRecognizer(loc.Model, threads: 2, minAudioMs: 1500);
            await rec.PrepareAsync("tiny", null);
            Assert.True(rec.IsReady, "native prepare failed");

            // Pinned-version evidence: the loaded runtime reports the pinned major.minor.
            string version = WhisperNative.Version();
            _output.WriteLine("NATIVE RUNTIME VERSION: " + version);
            Assert.StartsWith(WhisperNative.PinnedMajorMinor + ".", version);

            byte[] pcm = LocalWhisperAssets.ReadPcm16MonoWav(loc.JfkWav, out int rate);
            Assert.Equal(16000, rate);

            // Feed the whole 11 s JFK clip as one frame: the recognizer's
            // internal segment loop then transcribes the full clip (the first
            // pass auto-detects the language, the following passes transcribe).
            string collected = string.Empty;
            var frame = new AudioFrame(pcm, 16000);
            var r = await rec.ProcessAudioAsync(frame);
            if (!r.IsProvisional && r.Text.Length > 0)
                collected += r.Text + " ";
            var fin = await rec.FinalizeAsync();
            if (fin.Text.Length > 0)
                collected += fin.Text;

            _output.WriteLine("NATIVE SMOKE LANGUAGE: " + (rec.Language ?? "(none)"));
            Assert.False(string.IsNullOrEmpty(rec.Language), "language was not detected");
            _output.WriteLine("NATIVE SMOKE RESULT: " + collected.Trim());
            Assert.True(collected.Trim().Length > 0, "native transcription produced no text");
        }
        finally { WhisperNative.LibraryOverridePath = null; }
    }
}
