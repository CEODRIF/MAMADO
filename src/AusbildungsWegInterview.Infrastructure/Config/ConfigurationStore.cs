using System.Text.Json;
using AusbildungsWegInterview.Core.Config;

namespace AusbildungsWegInterview.Infrastructure.Config;

/// <summary>
/// Persists <see cref="AppConfiguration"/> as local JSON. The configuration holds
/// no secrets or credentials, so writing it locally is safe. Load is resilient:
/// a missing or corrupt file falls back to sensible defaults.
/// </summary>
public sealed class ConfigurationStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string _path;
    public string FilePath => _path;

    public ConfigurationStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path required.", nameof(path));
        _path = path;
    }

    public AppConfiguration Load()
    {
        if (!File.Exists(_path))
            return AppConfiguration.Default();
        try
        {
            var json = File.ReadAllText(_path);
            return string.IsNullOrWhiteSpace(json)
                ? AppConfiguration.Default()
                : (JsonSerializer.Deserialize<AppConfiguration>(json, Options) ?? AppConfiguration.Default());
        }
        catch
        {
            return AppConfiguration.Default();
        }
    }

    public void Save(AppConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var full = Path.GetFullPath(_path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(full, JsonSerializer.Serialize(config, Options));
    }

    /// <summary>Default local path under the per-user AppData directory.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AusbildungsWegInterview", "config.json");
}
