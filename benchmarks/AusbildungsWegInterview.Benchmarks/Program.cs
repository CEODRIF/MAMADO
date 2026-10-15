using System.Diagnostics;
using AusbildungsWegInterview.Answers;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Config;
using AusbildungsWegInterview.Core.Pipeline;
using AusbildungsWegInterview.Speech;
using AusbildungsWegInterview.Tests.Fakes;
using AusbildungsWegInterview.Translation;

// ---------------------------------------------------------------------------
// Reproducible local benchmarks (CPU-only host measurements).
// Usage:
//   dotnet run --project benchmarks/AusbildungsWegInterview.Benchmarks -- \
//     --whisper-cli <path> --model <path> --wav <path>
// Without --whisper-cli/--model/--wav the whisper items are skipped and the
// pipeline/sustained/detection items (mocked LLM) still run.
// ---------------------------------------------------------------------------

string[] cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
string? Get(string name)
{
    for (int i = 0; i < cliArgs.Length - 1; i++)
        if (cliArgs[i] == name) return cliArgs[i + 1];
    return null;
}
string? whisperCli = Get("--whisper-cli");
string? model = Get("--model");
string? wav = Get("--wav");

string CpuInfo()
{
    string cpu = "unknown";
    try
    {
        using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "wmic" : "sh",
            Arguments = OperatingSystem.IsWindows() ? "cpu get Name" : "-c \"grep -m1 'model name' /proc/cpuinfo | cut -d: -f2\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        string outp = proc.StandardOutput.ReadToEnd().Trim();
        proc.WaitForExit(3000);
        cpu = outp.Split('\n')[0].Trim();
    }
    catch { /* keep "unknown" */ }
    return $"CPU: {cpu} | cores: {Environment.ProcessorCount} | RAM: {RamGb():0.#} GB | OS: {Environment.OSVersion.Platform}";
}

double RamGb()
{
    try
    {
        if (OperatingSystem.IsWindows())
        {
            using var p = Process.Start(new ProcessStartInfo("wmic", "OS get TotalPhysicalMemory")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
            string s = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000);
            var line = s.Split('\n').FirstOrDefault(l => long.TryParse(l.Trim(), out _)) ?? "";
            return long.TryParse(line.Trim(), out var b) ? b / 1024.0 / 1024.0 / 1024.0 : 0;
        }
        foreach (var line in File.ReadLines("/proc/meminfo"))
            if (line.StartsWith("MemTotal:"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return uint.Parse(parts[1]) / 1024.0 / 1024.0; // kB -> GB
            }
    }
    catch { /* unknown */ }
    return 0;
}

Console.WriteLine("=== AusbildungsWeg Interview — local benchmarks ===");
Console.WriteLine(CpuInfo());
Console.WriteLine("NOTE: these are MEASUREMENTS on the current host, not promises for other hardware.");
Console.WriteLine();

byte[] Pcm; int Rate;
bool HaveAssets = !string.IsNullOrWhiteSpace(whisperCli) && !string.IsNullOrWhiteSpace(model) && !string.IsNullOrWhiteSpace(wav);
if (HaveAssets)
{
    Pcm = ReadWav(wav!, out Rate);
    Console.WriteLine($"Audio sample: {wav} ({Pcm.Length / 2 / (double)Rate:0.0} s @ {Rate} Hz)");
    Console.WriteLine();
}
else
{
    Pcm = Array.Empty<byte>();
    Rate = 16000;
    Console.WriteLine("No --whisper-cli/--model/--wav supplied: whisper benchmarks skipped.");
    Console.WriteLine();
}

// 1) whisper transcription (real) -------------------------------------------
if (HaveAssets)
{
    var sw = Stopwatch.StartNew();
    await using (var rec = new WhisperCliRecognizer(whisperCli!, model!, threads: Math.Min(4, Environment.ProcessorCount),
                     partialIntervalSeconds: 999.0, maxSegmentSeconds: 60.0, timeoutMs: 300000,
                     workDir: Path.Combine(Path.GetTempPath(), "awg-bench-" + Guid.NewGuid().ToString("N"))))
    {
        await rec.PrepareAsync("tiny", null);
        int step = 8000 * 2;
        for (int o = 0; o < Pcm.Length; o += step)
        {
            int len = Math.Min(step, Pcm.Length - o);
            var c = new byte[len]; Buffer.BlockCopy(Pcm, o, c, 0, len);
            await rec.ProcessAudioAsync(new AudioFrame(c, Rate));
        }
        var r = await rec.FinalizeAsync();
        Console.WriteLine($"[1] whisper transcription (final): {sw.ElapsedMilliseconds} ms | RTF {(sw.Elapsed.TotalSeconds / (Pcm.Length / 2.0 / Rate)):0.00}x | text: \"{Trunc(r.Text, 80)}\"");
    }
}

// 2) whisper -tr translation (real) ------------------------------------------
if (HaveAssets)
{
    var sw = Stopwatch.StartNew();
    await using (var rec = new WhisperCliRecognizer(whisperCli!, model!, threads: Math.Min(4, Environment.ProcessorCount),
                     partialIntervalSeconds: 999.0, maxSegmentSeconds: 60.0, timeoutMs: 300000,
                     workDir: Path.Combine(Path.GetTempPath(), "awg-bench-" + Guid.NewGuid().ToString("N"))))
    {
        await rec.PrepareAsync("tiny", null);
        int step = 8000 * 2;
        for (int o = 0; o < Pcm.Length; o += step)
        {
            int len = Math.Min(step, Pcm.Length - o);
            var c = new byte[len]; Buffer.BlockCopy(Pcm, o, c, 0, len);
            await rec.ProcessAudioAsync(new AudioFrame(c, Rate));
        }
        var r = await rec.TranslateSegmentToEnglishAsync();
        Console.WriteLine($"[2] whisper -tr translation: {sw.ElapsedMilliseconds} ms | output: \"{Trunc(r ?? "(none)", 80)}\"");
    }
}

// 3) question detection latency (mocked, deterministic) ----------------------
{
    var detector = new QuestionDetector();
    string[] samples =
    {
        "Warum möchten Sie eine Ausbildung bei uns machen?",
        "What is your greatest strength?",
        "Pourquoi postulez-vous à ce poste ?",
        "لماذا تريد هذا التدريب؟"
    };
    // warmup
    for (int i = 0; i < 1000; i++) detector.IsSufficient(samples[i % samples.Length], 8, true);
    const int N = 20_000;
    double[] ns = new double[5];
    for (int rep = 0; rep < 5; rep++)
    {
        long sink = 0;
        var swd = Stopwatch.StartNew();
        for (int i = 0; i < N; i++) sink += (detector.IsSufficient(samples[i % samples.Length], 8, true) ? 1 : 0);
        swd.Stop();
        ns[rep] = swd.Elapsed.TotalMilliseconds / N * 1_000_000.0; // ns/op
        Console.WriteLine($"      (sink={sink} to keep the work observable)");
    }
    Array.Sort(ns);
    Console.WriteLine($"[3] question detection: median {ns[2]:0.#} ns/op (5 reps x {N}, multilingual DE/EN/FR/AR)");
}

// 4) end-to-end pipeline with real ASR (mocked LLM stages) -------------------
if (HaveAssets)
{
    var speech = new WhisperCliRecognizer(whisperCli!, model!, threads: Math.Min(4, Environment.ProcessorCount),
        partialIntervalSeconds: 5.0, maxSegmentSeconds: 60.0, timeoutMs: 300000,
        workDir: Path.Combine(Path.GetTempPath(), "awg-bench-" + Guid.NewGuid().ToString("N")));
    var answers = new MockAnswerGenerator();
    var config = new AppConfiguration { ChunkSeconds = 0.5, TargetSampleRate = 16000, SilenceRmsThreshold = 0.01, SilenceFinalizeSeconds = 0.8, MinQuestionChars = 8 };
    await using var pipeline = new InterviewPipeline(new NoopAudioCapture(), speech, new MockTranslator(), answers, new QuestionDetector(), config);
    TaskCompletionSource<bool> answerSeen = new();
    TaskCompletionSource<bool> finalSeen = new();
    Stopwatch sw = Stopwatch.StartNew();
    long finalMs = -1, firstAnswerMs = -1, lastAnswerMs = -1;
    int answerCount = 0;
    pipeline.AnswerUpdated += (_, _) =>
    {
        var now = sw.ElapsedMilliseconds;
        if (Interlocked.CompareExchange(ref firstAnswerMs, now, -1) == -1) { }
        Interlocked.Exchange(ref lastAnswerMs, now);
        Interlocked.Increment(ref answerCount);
        answerSeen.TrySetResult(true);
    };
    pipeline.TranscriptUpdated += (_, e) => { if (e.Result.IsFinal && !string.IsNullOrWhiteSpace(e.Result.Text) && Interlocked.CompareExchange(ref finalMs, sw.ElapsedMilliseconds, -1) == -1) finalSeen.TrySetResult(true); };
    await pipeline.StartAsync();
    int step = 8000 * 2;
    for (int o = 0; o < Pcm.Length; o += step)
    {
        int len = Math.Min(step, Pcm.Length - o);
        var c = new byte[len]; Buffer.BlockCopy(Pcm, o, c, 0, len);
        pipeline.EnqueueAudio(new AudioFrame(c, Rate));
    }
    for (int i = 0; i < 4; i++)
        pipeline.EnqueueAudio(new AudioFrame(new byte[Rate], Rate)); // 2 s trailing silence
    while (sw.ElapsedMilliseconds < 300_000)
    {
        if (finalSeen.Task.IsCompleted && Volatile.Read(ref answerCount) >= 2) break;
        await Task.Delay(50);
    }
    await pipeline.StopAsync();
    Console.WriteLine($"[4] pipeline E2E (real ASR, mocked LLM): provisional answer at {firstAnswerMs} ms | final transcript at {finalMs} ms | final answer at {lastAnswerMs} ms (audio fed as fast as possible)");
}

// 5) sustained audio: queue growth + peak working set -------------------------
{
    var speech = new ScriptedSpeechRecognizer(); // no-op (silence-gated)
    var answers = new MockAnswerGenerator();
    var config = new AppConfiguration { ChunkSeconds = 0.5, TargetSampleRate = 16000, SilenceRmsThreshold = 0.01 };
    await using var pipeline = new InterviewPipeline(new NoopAudioCapture(), speech, new MockTranslator(), answers, new QuestionDetector(), config);
    await pipeline.StartAsync();

    var proc = Process.GetCurrentProcess();
    long peakKb = 0;
    int maxDepth = 0;
    // 10 minutes of simulated silent audio = 1200 frames x 0.5 s
    for (int i = 0; i < 1200; i++)
    {
        pipeline.EnqueueAudio(new AudioFrame(new byte[Rate], Rate));
        if (i % 25 == 0)
        {
            maxDepth = Math.Max(maxDepth, pipeline.QueueDepth);
            peakKb = Math.Max(peakKb, proc.WorkingSet64 / 1024);
        }
    }
    await Task.Delay(500);
    for (int i = 0; i < 40; i++) // drain
    {
        maxDepth = Math.Max(maxDepth, pipeline.QueueDepth);
        peakKb = Math.Max(peakKb, proc.WorkingSet64 / 1024);
        if (pipeline.QueueDepth == 0) break;
        await Task.Delay(50);
    }
    await pipeline.StopAsync();
    Console.WriteLine($"[5] sustained 10-min simulated audio: max queue depth {maxDepth} (cap 128) | peak process RSS {peakKb / 1024.0:0.#} MB");
}

Console.WriteLine();
Console.WriteLine("Done. Ollama answer/translation latency was NOT measured (no Ollama on this host).");

static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";

static byte[] ReadWav(string path, out int sampleRate)
{
    var bytes = File.ReadAllBytes(path);
    sampleRate = 0;
    int dataOffset = -1, dataLen = 0, pos = 12;
    while (pos + 8 <= bytes.Length)
    {
        string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
        int size = BitConverter.ToInt32(bytes, pos + 4);
        if (id == "fmt ") sampleRate = BitConverter.ToInt32(bytes, pos + 12);
        if (id == "data") { dataOffset = pos + 8; dataLen = size; break; }
        pos += 8 + size + (size & 1);
    }
    if (dataOffset <= 0) throw new InvalidDataException("no data chunk");
    var pcm = new byte[dataLen];
    Buffer.BlockCopy(bytes, dataOffset, pcm, 0, dataLen);
    return pcm;
}
