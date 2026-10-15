using System.Net;
using System.Text;
using System.Text.Json;

namespace AusbildungsWegInterview.Infrastructure.Ollama;

/// <summary>
/// Default <see cref="IOllamaClient"/> implementation talking to a local Ollama
/// server over HTTP. All failures degrade to "unavailable" instead of throwing
/// (except hard argument errors), so the app stays usable when Ollama is absent.
/// </summary>
public sealed class OllamaClient : IOllamaClient
{
    private readonly HttpClient _http;
    private readonly int _availabilityTimeoutMs;

    public string BaseUrl { get; }

    public OllamaClient(string baseUrl, HttpClient? http = null, int availabilityTimeoutMs = 1200)
    {
        BaseUrl = (string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:11434" : baseUrl).TrimEnd('/');
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _availabilityTimeoutMs = availabilityTimeoutMs;
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

        var resp = await _http.PostAsync(BaseUrl + "/api/generate", content, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("response", out var r) ? (r.GetString() ?? string.Empty) : string.Empty;
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose(); // HttpClient implements IDisposable only
        return ValueTask.CompletedTask;
    }
}
