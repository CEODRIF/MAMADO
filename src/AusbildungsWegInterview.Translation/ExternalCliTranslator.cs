using System.Diagnostics;
using System.Text;
using AusbildungsWegInterview.Core.Translation;

namespace AusbildungsWegInterview.Translation;

/// <summary>
/// Translation via a standalone, user-provided CLI (e.g. opus-mt / mariannet,
/// or an Argos wrapper). Contract:
///  * Text is written to the process STDIN (UTF-8, no trailing newline).
///  * The FIRST non-empty line of STDOUT is the translation.
///  * Arguments come from a template with {source} and {target} placeholders.
///
/// All failures (missing executable, unsupported pair, timeout, non-zero exit,
/// empty output) are reported as honest <see cref="TranslationResult.StatusMessage"/>
/// — never a faked translation. Models are user-managed (explicit download);
/// this class never fetches anything.
/// </summary>
public sealed class ExternalCliTranslator : ITranslator
{
    private readonly string _executable;
    private readonly string? _argsTemplate;
    private readonly HashSet<string> _supportedPairs;
    private readonly int _timeoutMs;

    /// <param name="executable">Path to the translation CLI.</param>
    /// <param name="argsTemplate">Argument template, e.g.
    /// "mariannet -m {model} -s {source} -t {target} -i - -o -".</param>
    /// <param name="supportedPairs">Pairs the provided model(s) cover, e.g. "de-en,en-de,de-fr".</param>
    /// <param name="timeoutSeconds">Per-translation timeout.</param>
    public ExternalCliTranslator(string executable, string? argsTemplate, string supportedPairs, int timeoutSeconds = 30)
    {
        _executable = executable ?? throw new ArgumentException("executable required", nameof(executable));
        _argsTemplate = argsTemplate;
        _supportedPairs = supportedPairs
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _timeoutMs = Math.Max(1, timeoutSeconds) * 1000;
    }

    public string ProviderName => "external CLI (opus-mt class)";

    public Task PrepareAsync(string? sourceLanguage, string targetLanguage, CancellationToken ct = default)
    {
        // Fail fast with a clear, actionable error when the binary is missing.
        if (!System.IO.File.Exists(_executable))
            throw new FileNotFoundException(
                "Translation CLI not found at '" + _executable +
                "'. Install the engine (e.g. mariannet/opus-mt) or switch the translation engine in Settings.");
        return Task.CompletedTask;
    }

    public Task<TranslationResult> TranslateAsync(
        string text, string? sourceLanguage, string targetLanguage, CancellationToken ct = default)
    {
        var src = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(new TranslationResult(text ?? string.Empty, string.Empty, src, targetLanguage, false, ProviderName));

        // No auto-detection in a standalone MT model: it needs a concrete source.
        if (src == "auto")
            return Task.FromResult(new TranslationResult(text, string.Empty, src, targetLanguage, false, ProviderName,
                "External CLI needs a pinned source language (set the recognition language) - original shown (not translated)",
                TranslationStatus.NeedsPinnedSource));

        string tgt = targetLanguage.Trim().ToLowerInvariant();
        if (_supportedPairs.Count > 0 && !_supportedPairs.Contains(src + "-" + tgt))
            return Task.FromResult(new TranslationResult(text, string.Empty, src, tgt, false, ProviderName,
                $"Pair {src}→{tgt} is not covered by the configured model(s) [{string.Join(", ", _supportedPairs)}] - download the model for this pair or use Ollama",
                TranslationStatus.PairNotSupported));

        return TranslateViaCliAsync(text, src, tgt, ct);
    }

    /// <summary>
    /// The exact launch configuration for the translation subprocess. Extracted as
    /// a test seam so the launch flags and argument line are covered by regression
    /// tests without spawning a process. (A hard-exit child-kill guarantee is
    /// provided by App.OnExit's bounded teardown + the cancellation kill path.)
    /// </summary>
    internal ProcessStartInfo BuildProcessStartInfo(string src, string tgt)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (!string.IsNullOrWhiteSpace(_argsTemplate))
        {
            string args = _argsTemplate
                .Replace("{source}", src, StringComparison.OrdinalIgnoreCase)
                .Replace("{target}", tgt, StringComparison.OrdinalIgnoreCase);
            foreach (var a in SplitArgs(args))
                psi.ArgumentList.Add(a);
        }
        return psi;
    }

    private async Task<TranslationResult> TranslateViaCliAsync(string text, string src, string tgt, CancellationToken ct)
    {
        var psi = BuildProcessStartInfo(src, tgt);

        Process proc;
        try
        {
            proc = Process.Start(psi)
                  ?? throw new InvalidOperationException("Failed to start the translation process.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new TranslationResult(text, string.Empty, src, tgt, false, ProviderName,
                "Could not launch the translation CLI (incompatible binary?): " + ex.Message,
                TranslationStatus.CliLaunchFailed);
        }

        using (proc)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_timeoutMs);
            try
            {
                await proc.StandardInput.WriteAsync(text).ConfigureAwait(false);
                proc.StandardInput.Close();

                var stderrTask = proc.StandardError.ReadToEndAsync();
                string? line;
                try
                {
                    line = await proc.StandardOutput.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    TryKill(proc);
                    return new TranslationResult(text, string.Empty, src, tgt, false, ProviderName,
                        $"Translation timed out after {_timeoutMs / 1000}s - original shown (not translated)",
                        TranslationStatus.CliTimeout);
                }

                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                string stderr = await stderrTask.ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(line))
                    return proc.ExitCode == 0
                        ? new TranslationResult(text, string.Empty, src, tgt, false, ProviderName,
                            "Translation produced no output - original shown (not translated)",
                            TranslationStatus.NoOutput)
                        : new TranslationResult(text, string.Empty, src, tgt, false, ProviderName,
                            $"Translation CLI exited with code {proc.ExitCode}. {Truncate(stderr.Trim(), 300)}",
                            TranslationStatus.CliError);

                line = line.Trim();
                if (string.Equals(line, text.Trim(), StringComparison.Ordinal))
                    return new TranslationResult(text, line, src, tgt, false, ProviderName,
                        "Text returned unchanged by the engine (not a translation)",
                        TranslationStatus.Echo);

                return new TranslationResult(text, line, src, tgt, false, ProviderName);
            }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                throw;
            }
            catch (Exception ex)
            {
                return new TranslationResult(text, string.Empty, src, tgt, false, ProviderName,
                    "Translation failed - original shown (not translated): " + ex.Message,
                    TranslationStatus.CliError);
            }
        }
    }

    private static void TryKill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
    }

    private static IEnumerable<string> SplitArgs(string template)
    {
        // Simple quoted-aware split (models paths may contain spaces).
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        foreach (char c in template)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (c == ' ' && !inQuotes)
            {
                if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
