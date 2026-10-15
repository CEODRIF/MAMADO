namespace AusbildungsWegInterview.Core.Logging;

/// <summary>Log severity.</summary>
public enum LogLevel
{
    Info = 0,
    Warning = 1,
    Error = 2
}

/// <summary>
/// Minimal structured logging facade.
///
/// Sensitive-data policy: callers must NOT pass transcript text, CV content,
/// audio, or secrets. Logging is for operational state (state transitions,
/// latency, backpressure, error types). Implementations are expected to redact
/// defensively regardless.
/// </summary>
public interface IAppLogger
{
    /// <summary>Minimum level to emit.</summary>
    LogLevel MinLevel { get; }

    /// <summary>Writes one structured log line (implementation redacts as appropriate).</summary>
    void Log(LogLevel level, string category, string message, Exception? exception = null);

    /// <summary>Returns a logger scoped to a category.</summary>
    IAppLogger For(string category);
}

/// <summary>No-op logger (default; used when no sink is configured).</summary>
public sealed class NullLogger : IAppLogger
{
    public static readonly NullLogger Instance = new();
    public LogLevel MinLevel => LogLevel.Info;
    public void Log(LogLevel level, string category, string message, Exception? exception = null) { }
    public IAppLogger For(string category) => this;
}
