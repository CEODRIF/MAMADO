using System.Net;
using System.Text;
using System.Text.Json;

namespace AusbildungsWegInterview.Infrastructure.Ollama;

/// <summary>
/// Default <see cref="IOllamaClient"/> talking to a local Ollama server over HTTP.
/// Availability probes degrade to "unavailable" instead of throwing; generation
/// surfaces clear errors for HTTP failures, an "error" field, or malformed JSON.
/// A per-request timeout bounds slow/hung generations.
/// </summary>
public sealed class OllamaClient : IOllamaClient
{
    private readonly HttpClient _http;
    private readonly int _availabilityTimeoutMs;
    private readonly int _requestTimeoutMs;

    public string BaseUrl { get; }

    public OllamaClient(string baseUrl, HttpClient? http = null, int availabilityTimeoutMs = 1500, int requestTimeoutMs = 60000)
    {
        BaseUrl = (string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:11434" : baseUrl).TrimEnd('/');
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
        _availabilityTimeoutMs = availabilityTimeoutMs;
        _requestTimeoutMs = requestTimeoutMs;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_availabilityTimeoutMs);
            using var resp = await _http.GetAsync(BaseUrl + "/api/tags", cts.Token).ConfigureAwait(false);
            return resp.StatusCode == HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        var names = new List<string>();
        try
        {
            var body = await _http.GetByteArrayAsync(BaseUrl + "/api/tags", ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in models.EnumerateArray())
                {
                    if (m.TryGetProperty("name", out var name))
                        names.Add(name.GetString() ?? string.Empty);
                }
            }
        }
        catch
        {
            // server unreachable -> empty list
        }
        return names;
    }

    public async Task<bool> ModelAvailableAsync(string model, CancellationToken ct = default)
    {
        try
        {
            var models = await ListModelsAsync(ct).ConfigureAwait(false);
            // Allow matching a base name against a specific quantization, e.g. "llama3.1:8b" vs "llama3.1:8b-q4_K_M".
            return models.Contains(model, StringComparer.OrdinalIgnoreCase)
                || models.Any(m => m.StartsWith(model, StringComparison.OrdinalIgnoreCase) && m.Length > model.Length);
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> GenerateAsync(string model, string prompt, double? temperature = null, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = false
        };
        if (temperature is not null)
            payload["temperature"] = temperature.Value;

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_requestTimeoutMs);

        var resp = await _http.PostAsync(BaseUrl + "/api/generate", content, timeoutCts.Token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                throw new InvalidOperationException("Ollama error: " + (err.GetString() ?? "unknown"));
            return doc.RootElement.TryGetProperty("response", out var r) ? (r.GetString() ?? string.Empty) : string.Empty;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Malformed response from Ollama.", ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose(); // HttpClient implements IDisposable only
        return ValueTask.CompletedTask;
    }
}
