namespace AusbildungsWegInterview.Infrastructure.Models;

/// <summary>Metadata for a downloadable whisper.cpp model (third-party, local-only).</summary>
public sealed record WhisperModelInfo(string Name, string Url, long SizeBytes, string Format);

/// <summary>
/// Manages whisper.cpp model files in a local directory.
///
/// Privacy/safety rules implemented here:
///  * Models are downloaded EXPLICITLY (via <see cref="DownloadAsync"/>) and NEVER
///    automatically at application startup.
///  * Model files are stored locally (gitignored) and are never committed to Git.
///  * Downloaded files are data only; they are never executed as code.
/// </summary>
public sealed class WhisperModelManager
{
    private readonly IReadOnlyDictionary<string, WhisperModelInfo> _catalog;
    private readonly HttpClient _http;

    public string ModelsDirectory { get; }

    public WhisperModelManager(string modelsDirectory, IEnumerable<WhisperModelInfo>? catalog = null, HttpClient? http = null)
    {
        ModelsDirectory = modelsDirectory ?? throw new ArgumentException("Directory required.", nameof(modelsDirectory));
        _catalog = (catalog ?? DefaultCatalog()).ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
        _http = http ?? new HttpClient();
    }

    public IReadOnlyCollection<string> AvailableModels => _catalog.Keys.ToArray();

    public WhisperModelInfo? GetInfo(string name) => _catalog.TryGetValue(name, out var i) ? i : null;

    public string ResolvePath(string name)
    {
        if (GetInfo(name) is null)
            throw new ArgumentException($"Unknown model '{name}'.", nameof(name));
        return Path.Combine(ModelsDirectory, name + "." + (GetInfo(name)!.Format));
    }

    public bool IsPresent(string name)
    {
        if (GetInfo(name) is null)
            return false;
        var p = ResolvePath(name);
        return File.Exists(p) && new FileInfo(p).Length > 0;
    }

    /// <summary>
    /// Explicitly downloads a model. Caller must decide to invoke this (e.g. a
    /// "Download model" button). Never called automatically on startup.
    /// </summary>
    public async Task<string> DownloadAsync(string name, bool overwrite = false, CancellationToken ct = default)
    {
        var info = GetInfo(name) ?? throw new ArgumentException($"Unknown model '{name}'.", nameof(name));
        Directory.CreateDirectory(ModelsDirectory);
        var dest = ResolvePath(name);
        if (IsPresent(name) && !overwrite)
            return dest;

        var tmp = dest + ".part";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using var resp = await _http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
        }
        File.Move(tmp, dest, overwrite: true);
        return dest;
    }

    public static IReadOnlyList<WhisperModelInfo> DefaultCatalog() => new List<WhisperModelInfo>
    {
        // Official whisper.cpp model binaries (ggerganov). Sizes approximate.
        new("tiny",   "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin",   75_000_000,  "bin"),
        new("base",   "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",   142_000_000, "bin"),
        new("small",  "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",  466_000_000, "bin"),
        new("medium", "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin", 1_545_000_000, "bin"),
    };

    /// <summary>Default local models directory (gitignored, per-user).</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AusbildungsWegInterview", "models");
}
