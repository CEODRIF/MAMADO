using AusbildungsWegInterview.Core.Speech;
using AusbildungsWegInterview.Core.Translation;
using AusbildungsWegInterview.Translation;
using Xunit;

namespace AusbildungsWegInterview.Tests.Translation;

public class WhisperTranslateAdapterTests
{
    private sealed class FakeSegment : ISegmentTranslator
    {
        public string? Result { get; set; }
        public Task<string?> TranslateSegmentToEnglishAsync(CancellationToken ct = default)
            => Task.FromResult(Result);
    }

    [Fact]
    public async Task NonEnglishTarget_ReportedNotFaked()
    {
        var adapter = new WhisperTranslateAdapter(new FakeSegment { Result = "x" });
        var r = await adapter.TranslateAsync("Warum?", "de", "fr");

        Assert.False(r.IsTranslated);
        Assert.Contains("English only", r.StatusMessage);
    }

    [Fact]
    public async Task UnrecognizedEngine_Reported()
    {
        var adapter = new WhisperTranslateAdapter(null);
        var r = await adapter.TranslateAsync("Warum?", "de", "en");

        Assert.False(r.IsTranslated);
        Assert.Contains("does not support", r.StatusMessage);
    }

    [Fact]
    public async Task RealTranslation_IsLabeledTranslated()
    {
        var adapter = new WhisperTranslateAdapter(new FakeSegment { Result = "Why do you want to join us?" });
        var r = await adapter.TranslateAsync("Warum möchten Sie zu uns?", "de", "en");

        Assert.True(r.IsTranslated);
        Assert.Null(r.StatusMessage);
        Assert.Equal("Why do you want to join us?", r.TargetText);
    }

    [Fact]
    public async Task EchoText_NotLabeledTranslated()
    {
        var adapter = new WhisperTranslateAdapter(new FakeSegment { Result = "And so, my fellow Americans." });
        var r = await adapter.TranslateAsync("And so, my fellow Americans.", "en", "en");

        Assert.False(r.IsTranslated);
        Assert.Contains("unchanged", r.StatusMessage);
    }

    [Fact]
    public async Task EmptySegment_ReportedNotCrash()
    {
        var adapter = new WhisperTranslateAdapter(new FakeSegment { Result = null });
        var r = await adapter.TranslateAsync("Warum?", "de", "en");

        Assert.False(r.IsTranslated);
        Assert.Contains("No translation produced", r.StatusMessage);
    }
}

public class ExternalCliTranslatorTests
{
    /// <summary>Creates a tiny "translation" CLI: prefixes every input line with TR:</summary>
    private static string CreateFakeCli(string dir)
    {
        if (!OperatingSystem.IsWindows())
        {
            string p = Path.Combine(dir, "fakecli.sh");
            File.WriteAllText(p, "#!/bin/sh\necho \"TR:$(cat)\"\n");
            try { File.SetUnixFileMode(p, UnixFileMode.UserExecute | UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
            return p;
        }
        string bat = Path.Combine(dir, "fakecli.bat");
        File.WriteAllText(bat, "@echo off\r\nfor /f \"usebackq delims=\" %%a in (`cat`) do @echo TR:%%a\r\n");
        return bat;
    }

    [Fact]
    public async Task Prepare_MissingExecutable_ThrowsClearly()
    {
        var t = new ExternalCliTranslator(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".exe"), "a b", "de-en");
        await Assert.ThrowsAsync<FileNotFoundException>(() => t.PrepareAsync("de", "en"));
    }

    [Fact]
    public async Task UnsupportedPair_ReportedNotFaked()
    {
        var t = new ExternalCliTranslator("/bin/true", "a", "de-en,en-de");
        var r = await t.TranslateAsync("Hallo", "de", "fr");

        Assert.False(r.IsTranslated);
        Assert.Contains("not covered", r.StatusMessage);
        Assert.Contains("de→fr", r.StatusMessage);
    }

    [Fact]
    public async Task AutoSource_ReportedBecauseCliNeedsPinnedLanguage()
    {
        var t = new ExternalCliTranslator("/bin/true", "a", "de-en");
        var r = await t.TranslateAsync("Hallo", null, "en");

        Assert.False(r.IsTranslated);
        Assert.Contains("pinned source language", r.StatusMessage);
    }

    [Fact]
    public async Task RealCli_TranslatesViaStdinStdout()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows .bat behavior is verified manually per docs; keep the suite fast/clean.
            return;
        }
        string dir = Path.Combine(Path.GetTempPath(), "awg-clitr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string cli = CreateFakeCli(dir);
            var t = new ExternalCliTranslator(cli, "x y", "de-en");
            await t.PrepareAsync("de", "en");

            var r = await t.TranslateAsync("Warum möchten Sie?", "de", "en");

            Assert.True(r.IsTranslated, r.StatusMessage);
            Assert.Equal("TR:Warum möchten Sie?", r.TargetText);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task CliEchoingInput_IsNotLabeledTranslated()
    {
        if (OperatingSystem.IsWindows()) return;
        string dir = Path.Combine(Path.GetTempPath(), "awg-cliecho-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string cli = Path.Combine(dir, "echo.sh");
            File.WriteAllText(cli, "#!/bin/sh\ncat\n");
            try { File.SetUnixFileMode(cli, UnixFileMode.UserExecute | UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }

            var t = new ExternalCliTranslator(cli, "x", "de-en");
            var r = await t.TranslateAsync("Hallo Welt", "de", "en");

            Assert.False(r.IsTranslated);
            Assert.Contains("unchanged", r.StatusMessage);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task CliTimeout_Reported()
    {
        if (OperatingSystem.IsWindows()) return;
        string dir = Path.Combine(Path.GetTempPath(), "awg-clitimeout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string cli = Path.Combine(dir, "hang.sh");
            File.WriteAllText(cli, "#!/bin/sh\nsleep 30\n");
            try { File.SetUnixFileMode(cli, UnixFileMode.UserExecute | UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }

            var t = new ExternalCliTranslator(cli, "x", "de-en", timeoutSeconds: 1);
            var r = await t.TranslateAsync("Hallo", "de", "en");

            Assert.False(r.IsTranslated);
            Assert.Contains("timed out", r.StatusMessage);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
