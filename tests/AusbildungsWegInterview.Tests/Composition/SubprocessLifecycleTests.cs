using System.Diagnostics;
using AusbildungsWegInterview.App;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Common;
using AusbildungsWegInterview.Core.Config;
using AusbildungsWegInterview.Core.Pipeline;
using AusbildungsWegInterview.Infrastructure.Config;
using AusbildungsWegInterview.Infrastructure.Models;
using AusbildungsWegInterview.Speech;
using AusbildungsWegInterview.Tests.Fakes;
using AusbildungsWegInterview.Translation;
using Xunit;

namespace AusbildungsWegInterview.Tests.Composition;

/// <summary>
/// Subprocess lifecycle regression guard.
///
/// The app spawns local helper processes (whisper-cli, external translation
/// CLI). On WPF exit, App.OnExit performs a BOUNDED wait on
/// AppServices.DisposeAsync so the pipeline's cancel→kill path has up to 2 s
/// to stop any in-flight subprocess before the process exits (a plain
/// fire-and-forget dispose could otherwise leave orphaned children).
///
/// These tests:
///   1. lock the launch configurations in place (internal test seams),
///   2. prove DisposeAsync COMPLETES while a real whisper-cli subprocess is
///      in flight — and leaves no orphan (Linux: /proc scan).
/// The real kill-on-exit behavior on Windows is additionally covered by
/// acceptance check E12-3.
/// </summary>
public class SubprocessLifecycleTests : IDisposable
{
    private readonly string _dir;

    public SubprocessLifecycleTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "awg-subproc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    // Any existing file is fine: the seams only configure, they never spawn.
    private static string ExistingFile => typeof(SubprocessLifecycleTests).Assembly.Location;

    [Fact]
    public async Task WhisperCliLaunchConfig_CapturesOutput_NoWindow_ExactArgs()
    {
        await using var rec = new WhisperCliRecognizer(ExistingFile, "model.bin");
        var psi = rec.BuildProcessStartInfo("segment.wav", "segment", translate: false);

        Assert.False(psi.UseShellExecute);
        Assert.True(psi.RedirectStandardOutput);
        Assert.True(psi.RedirectStandardError);
        Assert.True(psi.CreateNoWindow);
        // Default: no language flag (auto-detect).
        Assert.Equal(new[] { "-m", "model.bin", "-f", "segment.wav", "-oj", "-of", "segment", "-t", "2", "-np" },
            psi.ArgumentList.ToArray());
    }

    [Fact]
    public async Task WhisperCliLaunchConfig_TranslateMode_InsertsTranslateFlag()
    {
        await using var rec = new WhisperCliRecognizer(ExistingFile, "model.bin");
        var psi = rec.BuildProcessStartInfo("segment.wav", "segment-tr", translate: true);

        Assert.Equal(new[] { "-m", "model.bin", "-f", "segment.wav", "-tr", "-oj", "-of", "segment-tr", "-t", "2", "-np" },
            psi.ArgumentList.ToArray());
    }

    [Fact]
    public async Task ExternalCliLaunchConfig_CapturesIO_NoWindow_SubstitutedArgs()
    {
        await using var tr = new ExternalCliTranslator(ExistingFile, "-m {model} -s {source} -t {target}", "de-en");
        var psi = tr.BuildProcessStartInfo("de", "en");

        Assert.False(psi.UseShellExecute);
        Assert.True(psi.RedirectStandardInput);
        Assert.True(psi.RedirectStandardOutput);
        Assert.True(psi.RedirectStandardError);
        Assert.True(psi.CreateNoWindow);
        Assert.Equal(new[] { "-m", "{model}", "-s", "de", "-t", "en" }, psi.ArgumentList.ToArray());
    }

    [Fact]
    public async Task Dispose_CompletesWithInflightWhisperSubprocess_AndLeavesNoOrphans()
    {
        var loc = LocalWhisperAssets.Find();
        if (loc is null) return; // no local whisper.cpp build — self-skip (consistent with the other integration tests)

        var config = AppConfiguration.Default();
        config.WhisperExecutablePath = loc.Bin;
        config.WhisperModelPath = loc.Model;
        config.TranslationEnabled = false; // answers run against the fake Ollama client

        await using var services = new AppServices(
            config,
            new ConfigurationStore(Path.Combine(_dir, "settings.json")),
            new WhisperModelManager(Path.Combine(_dir, "models")),
            new NoopAudioCapture(),
            new FakeOllamaClient());

        await services.Pipeline.StartAsync();

        // Feed enough non-silence that transcription is in flight at dispose
        // time (1.2 s chunks; the tiny model needs >1 s per chunk on CI-class
        // hardware, and 20 chunks keep the queue busy well past that).
        for (int i = 0; i < 20; i++)
            services.Pipeline.EnqueueAudio(NonSilent(16000));

        // Observe MY child specifically: the pipeline stays in the Listening
        // state while chunks are processed, so "subprocess running" is the
        // in-flight signal. Other test classes may run their own whisper-cli
        // concurrently (same binary path, parallel xunit collections), so the
        // orphan check tracks the PIDs captured here, not the binary name.
        int[] inFlight = Array.Empty<int>();
        Assert.True(await WaitUntilAsync(() => { inFlight = SnapshotPids(loc.Bin); return inFlight.Length > 0; }, 10000),
            "expected a whisper-cli subprocess to be running before disposal");

        // App.OnExit allows 2 s for exactly this; allow generous slack here.
        var sw = Stopwatch.StartNew();
        await services.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(12), $"disposal hung: {sw.Elapsed}");

        // A killed child may take a few ms to be reaped — that is dying, not
        // orphaned. Give the captured PIDs up to 5 s; anything still alive
        // then is an orphan.
        Assert.True(await WaitUntilAsync(() => !SnapshotPids(loc.Bin).Any(p => inFlight.Contains(p)), 5000),
            $"whisper-cli subprocess(es) [{string.Join(",", inFlight)}] survived disposal (orphan)");
    }

    // ------------------------------------------------------------------
    // helpers (pattern shared with the other pipeline tests)
    // ------------------------------------------------------------------

    private static AudioFrame NonSilent(int bytes, int rate = 16000)
    {
        var b = new byte[bytes];
        for (int i = 0; i < b.Length; i += 2) { b[i] = 0x00; b[i + 1] = 0x20; } // 8192
        return new AudioFrame(b, rate);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> cond, int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cond()) return true;
            await Task.Delay(50);
        }
        return cond();
    }

    /// <summary>PIDs of currently running processes for the given binary (cross-platform).</summary>
    private static int[] SnapshotPids(string binPath)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var pids = new List<int>();
            try
            {
                foreach (var entry in Directory.GetDirectories("/proc"))
                {
                    string name = Path.GetFileName(entry);
                    if (!name.All(char.IsDigit)) continue;
                    try
                    {
                        string cmdline = File.ReadAllText(Path.Combine(entry, "cmdline")).Replace("\0", " ");
                        if (cmdline.Contains(binPath, StringComparison.Ordinal))
                            pids.Add(int.Parse(name));
                    }
                    catch { /* process vanished mid-scan */ }
                }
            }
            catch { /* /proc unavailable */ }
            return pids.ToArray();
        }

        string wanted = Path.GetFileNameWithoutExtension(binPath); // "whisper-cli" (Windows: whisper-cli.exe)
        var win = new List<int>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (string.Equals(p.ProcessName, wanted, StringComparison.OrdinalIgnoreCase))
                    win.Add(p.Id);
            }
            finally { p.Dispose(); }
        }
        return win.ToArray();
    }
}
