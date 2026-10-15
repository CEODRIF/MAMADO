using System.Text;
using System.Text.RegularExpressions;
using AusbildungsWegInterview.Core.Logging;

namespace AusbildungsWegInterview.Infrastructure.Logging;

/// <summary>
/// A local, structured log writer that defensively REDACTS anything that looks
/// like a secret, even if a caller mistakenly passes one. Lines go to an in-memory
/// sink and/or a local file. It never contains audio; transcript/CV should never
/// be passed by callers, and long opaque strings are truncated as defense-in-depth.
/// </summary>
public sealed class RedactingAppLogger : IAppLogger
{
    private static readonly Regex GithubPat = new("github_pat_[A-Za-z0-9_\\-]{16,}", RegexOptions.Compiled);
    private static readonly Regex Bearer = new("(?i)bearer\\s+[A-Za-z0-9._\\-]{16,}", RegexOptions.Compiled);
    private static readonly Regex LongOpaque = new(@"\S{48,}", RegexOptions.Compiled);

    private readonly Action<string> _sink;
    private readonly string? _filePath;
    private readonly object _gate = new();

    public LogLevel MinLevel { get; set; } = LogLevel.Info;
    public string? Category { get; }

    public RedactingAppLogger(Action<string> sink, string? filePath = null, string? category = null)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _filePath = filePath;
        Category = category;
    }

    public void Log(LogLevel level, string category, string message, Exception? exception = null)
    {
        if (level < MinLevel)
            return;

        var redactedMessage = Redact(message);
        var redactedEx = exception is null ? null : Redact(exception.GetType().Name) + ": " + Redact(exception.Message);

        // A scope set via For() wins over the per-call category.
        string effectiveCategory = string.IsNullOrEmpty(Category) ? category : Category;

        var sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("o")).Append(' ')
          .Append(level.ToString().ToUpperInvariant()).Append(' ')
          .Append(effectiveCategory).Append(": ").Append(redactedMessage);
        if (redactedEx is not null)
            sb.Append(" [").Append(redactedEx).Append(']');

        Emit(sb.ToString());
    }

    public IAppLogger For(string category) => new RedactingAppLogger(_sink, _filePath, category);

    private void Emit(string line)
    {
        _sink(line);
        if (!string.IsNullOrEmpty(_filePath))
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(_filePath));
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                lock (_gate)
                    File.AppendAllText(_filePath, line + Environment.NewLine);
            }
            catch
            {
                // logging must never crash the app
            }
        }
    }

    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;
        var s = GithubPat.Replace(input, "[REDACTED_GITHUB_PAT]");
        s = Bearer.Replace(s, "[REDACTED_BEARER]");
        s = LongOpaque.Replace(s, "[REDACTED_LONG_STRING]");
        return s;
    }
}

/// <summary>Convenience factory for a file-backed redacting logger under the user AppData dir.</summary>
public static class AppLogging
{
    public static string DefaultLogFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AusbildungsWegInterview", "logs", "app.log");

    public static RedactingAppLogger CreateFileLogger() => new(_ => { }, DefaultLogFile);
}
