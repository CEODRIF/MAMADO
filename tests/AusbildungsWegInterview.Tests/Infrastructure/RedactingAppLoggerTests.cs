using System.Text;
using AusbildungsWegInterview.Core.Logging;
using AusbildungsWegInterview.Infrastructure;
using AusbildungsWegInterview.Infrastructure.Logging;
using Xunit;

namespace AusbildungsWegInterview.Tests.Infrastructure;

public class RedactingAppLoggerTests
{
    private static string TempLog() => Path.Combine(Path.GetTempPath(), "awg-log-" + Guid.NewGuid().ToString("N") + ".log");

    [Fact]
    public void Redacts_GithubPat()
    {
        string s = RedactingAppLogger.Redact("token=github_pat_11BDGRTYY0YDKITbcHz046_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
        Assert.DoesNotContain("github_pat_", s);
        Assert.Contains("[REDACTED_GITHUB_PAT]", s);
    }

    [Fact]
    public void Redacts_BearerTokens()
    {
        string s = RedactingAppLogger.Redact("Authorization: Bearer abcdef0123456789abcdef0123456789");
        Assert.DoesNotContain("abcdef0123456789abcdef0123456789", s);
        Assert.Contains("[REDACTED_BEARER]", s);
    }

    [Fact]
    public void Redacts_LongOpaqueStrings()
    {
        string opaque = new('x', 80);
        string s = RedactingAppLogger.Redact("payload=" + opaque + " end");
        Assert.DoesNotContain(opaque, s);
        Assert.Contains("[REDACTED_LONG_STRING]", s);
    }

    [Fact]
    public void NormalMessages_PassThrough()
    {
        Assert.Equal("state -> Listening", RedactingAppLogger.Redact("state -> Listening"));
    }

    [Fact]
    public void Log_WritesRedactedLine_WithCategory()
    {
        var lines = new List<string>();
        var logger = new RedactingAppLogger(lines.Add);
        logger.Log(LogLevel.Info, "pipeline", "hello world");

        var line = Assert.Single(lines);
        Assert.Contains("INFO", line);
        Assert.Contains("pipeline: ", line);
        Assert.Contains("hello world", line);
    }

    [Fact]
    public void Log_FiltersBelowMinLevel()
    {
        var lines = new List<string>();
        var logger = new RedactingAppLogger(lines.Add) { MinLevel = LogLevel.Error };
        logger.Log(LogLevel.Info, "c", "should not appear");
        logger.Log(LogLevel.Error, "c", "boom");

        Assert.Single(lines);
        Assert.Contains("boom", lines[0]);
    }

    [Fact]
    public void Log_RedactsExceptionDetails()
    {
        var lines = new List<string>();
        var logger = new RedactingAppLogger(lines.Add);
        logger.Log(LogLevel.Error, "c", "failed",
            new InvalidOperationException("token github_pat_11BDGRTYY0YDKITbcHz046_ZZZZZZZZZZZZZZZZZZZZZZ0123456789"));

        var line = Assert.Single(lines);
        Assert.Contains("InvalidOperationException", line);
        Assert.DoesNotContain("github_pat_", line);
    }

    [Fact]
    public void Log_WritesToFile_UnderAppData()
    {
        string path = TempLog();
        try
        {
            var logger = new RedactingAppLogger(_ => { }, path);
            logger.Log(LogLevel.Info, "unit", "file test line");

            string content = File.ReadAllText(path);
            Assert.Contains("file test line", content);
            Assert.Contains("unit: ", content);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void For_Category_ScopesLogger()
    {
        var lines = new List<string>();
        var root = new RedactingAppLogger(lines.Add);
        var scoped = root.For("speech");
        scoped.Log(LogLevel.Info, "ignored", "scoped message");

        var line = Assert.Single(lines);
        Assert.Contains("speech: ", line);
    }
}

public class SystemInfoTests
{
    [Fact]
    public void RecommendOllamaModel_ReturnsKnownLocalModel()
    {
        string m = SystemInfo.RecommendOllamaModel();
        Assert.Contains(m, new[] { "llama3.1:8b", "llama3.2:3b", "phi3:mini" });
    }

    [Fact]
    public void RecommendWhisperModel_ReturnsKnownModel()
    {
        string m = SystemInfo.RecommendWhisperModel();
        Assert.Contains(m, new[] { "tiny", "base", "small" });
    }

    [Fact]
    public void ProcessorCount_IsPositive()
        => Assert.True(SystemInfo.ProcessorCount >= 1);
}
