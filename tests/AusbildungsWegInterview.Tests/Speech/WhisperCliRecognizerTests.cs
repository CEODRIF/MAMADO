using System.Text;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Speech;
using AusbildungsWegInterview.Speech;
using AusbildungsWegInterview.Tests.Fakes;
using Xunit;
using Xunit.Abstractions;

namespace AusbildungsWegInterview.Tests.Speech;

public class WhisperCliRecognizerTests
{
    private readonly ITestOutputHelper _output;
    public WhisperCliRecognizerTests(ITestOutputHelper output) => _output = output;

    // ---------------- error handling (no subprocess needed) ----------------

    [Fact]
    public async Task Prepare_MissingExecutable_ThrowsClearError()
    {
        await using var rec = new WhisperCliRecognizer(
            Path.Combine(Path.GetTempPath(), "definitely-missing-whisper-cli-" + Guid.NewGuid().ToString("N")),
            "model.bin");

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => rec.PrepareAsync("tiny", null));
        Assert.Contains("executable not found", ex.Message);
        Assert.False(rec.IsReady);
    }

    [Fact]
    public async Task Prepare_MissingModel_ThrowsClearError()
    {
        // Use an existing file as a stand-in "executable" so the check reaches the model path.
        string exe = typeof(WhisperCliRecognizerTests).Assembly.Location;
        await using var rec = new WhisperCliRecognizer(exe, Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".bin"));

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => rec.PrepareAsync("tiny", null));
        Assert.Contains("model not found", ex.Message);
        Assert.False(rec.IsReady);
    }

    [Fact]
    public async Task Finalize_EmptyBuffer_ReturnsEmpty_NoSubprocess()
    {
        string exe = typeof(WhisperCliRecognizerTests).Assembly.Location;
        var model = Path.Combine(Path.GetTempPath(), "dummy-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllBytesAsync(model, new byte[] { 1, 2, 3 });
        try
        {
            await using var rec = new WhisperCliRecognizer(exe, model);
            await rec.PrepareAsync("tiny", null);
            Assert.True(rec.IsReady);

            var r = await rec.FinalizeAsync();
            Assert.Equal(TranscriptionResult.Empty.Kind, r.Kind);
            Assert.True(string.IsNullOrWhiteSpace(r.Text));
        }
        finally
        {
            try { File.Delete(model); } catch { }
        }
    }

    [Fact]
    public async Task Process_NonPcm16Mono_Throws()
    {
        string exe = typeof(WhisperCliRecognizerTests).Assembly.Location;
        var model = Path.Combine(Path.GetTempPath(), "dummy-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllBytesAsync(model, new byte[] { 1 });
        try
        {
            await using var rec = new WhisperCliRecognizer(exe, model);
            await rec.PrepareAsync("tiny", null);
            var stereo = new AudioFrame(new byte[6400], 16000, 16, channels: 2);
            await Assert.ThrowsAsync<ArgumentException>(() => rec.ProcessAudioAsync(stereo));
        }
        finally
        {
            try { File.Delete(model); } catch { }
        }
    }

    [Fact]
    public void WavWriter_WritesValidRiffHeader()
    {
        int rate = 16000;
        var pcm = new byte[rate * 2]; // 1 s
        for (int i = 0; i < pcm.Length; i += 2) { pcm[i] = 0; pcm[i + 1] = 0x10; }

        string path = Path.Combine(Path.GetTempPath(), "awg-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WavWriter.Write16BitMono(path, pcm, rate);
            var all = File.ReadAllBytes(path);

            Assert.Equal((byte)'R', all[0]); Assert.Equal((byte)'I', all[1]);
            Assert.Equal((byte)'F', all[2]); Assert.Equal((byte)'F', all[3]);
            Assert.Equal((byte)'W', all[8]); Assert.Equal((byte)'A', all[9]);
            Assert.Equal((byte)'V', all[10]); Assert.Equal((byte)'E', all[11]);
            Assert.Equal((byte)'f', all[12]); Assert.Equal((byte)'m', all[13]);
            Assert.Equal((byte)'t', all[14]); Assert.Equal((byte)' ', all[15]);

            int fmtSize = BitConverter.ToInt32(all, 16);
            int audioFormat = BitConverter.ToInt16(all, 20);
            int channels = BitConverter.ToInt16(all, 22);
            int sampleRate = BitConverter.ToInt32(all, 24);
            int bits = BitConverter.ToInt16(all, 34);
            Assert.Equal(16, fmtSize);
            Assert.Equal(1, audioFormat); // PCM
            Assert.Equal(1, channels);
            Assert.Equal(rate, sampleRate);
            Assert.Equal(16, bits);

            // data chunk: payload must match exactly
            int dataStart = 44;
            int dataLen = BitConverter.ToInt32(all, 40);
            Assert.Equal(pcm.Length, dataLen);
            Assert.Equal(pcm.Length + 44, all.Length);
            for (int i = 0; i < pcm.Length; i++)
                Assert.Equal(pcm[i], all[dataStart + i]);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task RunWhisper_HangingExecutable_TimesOutClearly()
    {
        // A shell "executable" that sleeps longer than the recognizer timeout.
        string dir = Path.Combine(Path.GetTempPath(), "awg-hang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string script = Path.Combine(dir, "hang.sh");
        string model = Path.Combine(dir, "model.bin");
        await File.WriteAllBytesAsync(model, new byte[] { 1 });
        await File.WriteAllTextAsync(script, "#!/bin/sh\nsleep 30\n");
        try
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                try { File.SetUnixFileMode(script, UnixFileMode.UserExecute | UnixFileMode.UserRead | UnixFileMode.UserWrite); }
                catch { }
            }

            await using var rec = new WhisperCliRecognizer(script, model, timeoutMs: 1000, workDir: dir);
            await rec.PrepareAsync("tiny", null);

            // Feed 1 s of (silent) audio so a transcription actually starts; the hang is in the "executable".
            var frame = new AudioFrame(new byte[16000], 16000);
            await rec.ProcessAudioAsync(frame);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => rec.FinalizeAsync());
            Assert.Contains("timed out", ex.Message);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // ---------------- real whisper.cpp integration (gated on a local build) ----------------

    private sealed record WhisperLoc(string Bin, string Dir);
    private sealed record Assets(string Model, string JfkWav);

    /// <summary>
    /// Locates a locally built whisper.cpp: env override, or the repository's
    /// sibling tools directory (built via CMake). Returns null when unavailable
    /// so tests degrade gracefully on machines without the build.
    /// </summary>
    private static WhisperLoc? FindLocalWhisperCpp()
    {
        string? envBin = Environment.GetEnvironmentVariable("AUSBILDUNGSWEG_WHISPER_CLI");
        if (!string.IsNullOrWhiteSpace(envBin) && File.Exists(envBin))
            return new WhisperLoc(envBin, Path.GetDirectoryName(Path.GetFullPath(envBin))!);

        // Walk up from the test bin dir to find the repository root (MAMADO),
        // then look in ../tools/whisper.cpp/build/bin.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (string.Equals(dir.Name, "MAMADO", StringComparison.OrdinalIgnoreCase))
            {
                string bin = Path.GetFullPath(Path.Combine(dir.FullName, "..", "tools", "whisper.cpp", "build", "bin", "whisper-cli"));
                return File.Exists(bin) ? new WhisperLoc(bin, Path.GetDirectoryName(bin)!) : null;
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static Assets? FindLocalAssets(WhisperLoc loc)
    {
        // loc.Dir = .../tools/whisper.cpp/build/bin -> assets under .../tools/whisper.cpp
        string root = Path.GetFullPath(Path.Combine(loc.Dir, "..", ".."));
        string model = Path.Combine(root, "models", "ggml-tiny.bin");
        string jfk = Path.Combine(root, "samples", "jfk.wav");
        return File.Exists(model) && File.Exists(jfk) ? new Assets(model, jfk) : null;
    }

    [Fact]
    public async Task RealWhisper_TranscribesJfk_Speech_Final()
    {
        var bin = FindLocalWhisperCpp();
        if (bin is null)
        {
            _output.WriteLine("SKIPPED: no local whisper.cpp build found (set AUSBILDUNGSWEG_WHISPER_CLI to run).");
            return;
        }
        var assets = FindLocalAssets(bin);
        if (assets is null)
        {
            _output.WriteLine("SKIPPED: ggml-tiny.bin or samples/jfk.wav not found next to the build.");
            return;
        }

        byte[] pcm = ReadPcm16MonoWav(assets.JfkWav, out int rate);
        Assert.Equal(16000, rate);

        await using var rec = new WhisperCliRecognizer(
            bin.Bin, assets.Model, threads: Math.Min(4, Environment.ProcessorCount),
            partialIntervalSeconds: 999.0, // no partial inferences; finalize once
            maxSegmentSeconds: 60.0, timeoutMs: 120000,
            workDir: Path.Combine(Path.GetTempPath(), "awg-real-" + Guid.NewGuid().ToString("N")));
        await rec.PrepareAsync("tiny", null); // null = auto-detect language

        int chunkSamples = 8000; // 0.5 s
        for (int offset = 0; offset < pcm.Length; offset += chunkSamples * 2)
        {
            int len = Math.Min(chunkSamples * 2, pcm.Length - offset);
            var chunk = new byte[len];
            Buffer.BlockCopy(pcm, offset, chunk, 0, len);
            await rec.ProcessAudioAsync(new AudioFrame(chunk, rate));
        }

        var final = await rec.FinalizeAsync();

        Assert.False(string.IsNullOrWhiteSpace(final.Text), "Real whisper.cpp produced no text.");
        Assert.Equal(TranscriptKind.Final, final.Kind);
        Assert.Contains("fellow Americans", final.Text, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine("Detected language: " + final.DetectedLanguage + " | Text: " + final.Text);
    }

    [Fact]
    public async Task RealWhisper_PartialResults_AreProvisional()
    {
        var bin = FindLocalWhisperCpp();
        if (bin is null)
        {
            _output.WriteLine("SKIPPED: no local whisper.cpp build found.");
            return;
        }
        var assets = FindLocalAssets(bin);
        if (assets is null)
        {
            _output.WriteLine("SKIPPED: assets not found.");
            return;
        }

        byte[] pcm = ReadPcm16MonoWav(assets.JfkWav, out int rate);

        await using var rec = new WhisperCliRecognizer(
            bin.Bin, assets.Model, threads: Math.Min(4, Environment.ProcessorCount),
            partialIntervalSeconds: 0.5, // infer every 0.5 s of new audio
            maxSegmentSeconds: 60.0, timeoutMs: 120000,
            workDir: Path.Combine(Path.GetTempPath(), "awg-real-" + Guid.NewGuid().ToString("N")));
        await rec.PrepareAsync("tiny", null);

        int chunkSamples = 8000; // 0.5 s
        TranscriptionResult? lastPartial = null;
        int total = Math.Min(pcm.Length, chunkSamples * 2 * 6);
        for (int offset = 0; offset < total; offset += chunkSamples * 2)
        {
            var chunk = new byte[chunkSamples * 2];
            Buffer.BlockCopy(pcm, offset, chunk, 0, chunkSamples * 2);
            var r = await rec.ProcessAudioAsync(new AudioFrame(chunk, rate));
            if (!string.IsNullOrWhiteSpace(r.Text))
            {
                Assert.Equal(TranscriptKind.Partial, r.Kind);
                lastPartial = r;
            }
        }

        Assert.NotNull(lastPartial); // at least one real partial transcription happened
        _output.WriteLine("Partial: " + lastPartial!.Text);
    }

    [Fact]
    public async Task RealWhisper_TranslateMode_ProducesEnglish()
    {
        var loc = LocalWhisperAssets.Find();
        if (loc is null)
        {
            _output.WriteLine("SKIPPED: no local whisper.cpp build found.");
            return;
        }

        byte[] pcm = LocalWhisperAssets.ReadPcm16MonoWav(loc.JfkWav, out int rate);

        await using var rec = new WhisperCliRecognizer(
            loc.Bin, loc.Model, threads: Math.Min(2, Environment.ProcessorCount),
            partialIntervalSeconds: 999.0, maxSegmentSeconds: 60.0, timeoutMs: 120000,
            workDir: Path.Combine(Path.GetTempPath(), "awg-tr-" + Guid.NewGuid().ToString("N")));
        await rec.PrepareAsync("tiny", null);

        int chunkSamples = 8000;
        for (int offset = 0; offset < pcm.Length; offset += chunkSamples * 2)
        {
            int len = Math.Min(chunkSamples * 2, pcm.Length - offset);
            var chunk = new byte[len];
            Buffer.BlockCopy(pcm, offset, chunk, 0, len);
            await rec.ProcessAudioAsync(new AudioFrame(chunk, rate));
        }

        // JFK audio is English: -tr must (nearly) echo it — proves the -tr flag path + JSON parse.
        string? en = await rec.TranslateSegmentToEnglishAsync();

        Assert.False(string.IsNullOrWhiteSpace(en), "whisper -tr produced no text.");
        Assert.Contains("Americans", en!, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine("-tr output: " + en);
    }

    [Fact]
    public async Task RealWhisper_GarbageModel_FailsClearly()
    {
        var bin = FindLocalWhisperCpp();
        if (bin is null)
        {
            _output.WriteLine("SKIPPED: no local whisper.cpp build found.");
            return;
        }

        string dir = Path.Combine(Path.GetTempPath(), "awg-garbage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string garbage = Path.Combine(dir, "ggml-garbage.bin");
        await File.WriteAllBytesAsync(garbage, new byte[1024]);
        try
        {
            await using var rec = new WhisperCliRecognizer(
                bin.Bin, garbage, timeoutMs: 60000, workDir: Path.Combine(dir, "work"));
            await rec.PrepareAsync("tiny", null);

            // 1 s of silence + broken model -> non-zero exit, clear error.
            var frame = new AudioFrame(new byte[16000], 16000);
            await rec.ProcessAudioAsync(frame);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => rec.FinalizeAsync());
            Assert.Contains("exited with code", ex.Message);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>Reads the PCM payload of a 16-bit mono WAV (robust to extra RIFF chunks).</summary>
    private static byte[] ReadPcm16MonoWav(string path, out int sampleRate)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.Equal((byte)'R', bytes[0]);
        Assert.Equal((byte)'W', bytes[8]);
        sampleRate = 0;
        int dataOffset = -1, dataLen = 0;
        int pos = 12;
        while (pos + 8 <= bytes.Length) // walk RIFF chunks
        {
            string id = Encoding.ASCII.GetString(bytes, pos, 4);
            int size = BitConverter.ToInt32(bytes, pos + 4);
            if (id == "fmt ")
                sampleRate = BitConverter.ToInt32(bytes, pos + 12);
            if (id == "data")
            {
                dataOffset = pos + 8;
                dataLen = size;
                break;
            }
            pos += 8 + size + (size & 1);
        }
        Assert.True(dataOffset > 0, "WAV has no data chunk.");
        var pcm = new byte[dataLen];
        Buffer.BlockCopy(bytes, dataOffset, pcm, 0, dataLen);
        return pcm;
    }
}
