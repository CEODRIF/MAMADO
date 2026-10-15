using AusbildungsWegInterview.Infrastructure.Ollama;

namespace AusbildungsWegInterview.Tests.Fakes;

/// <summary>In-memory Ollama client for tests.</summary>
public sealed class FakeOllamaClient : IOllamaClient
{
    public string BaseUrl => "http://fake";
    public bool Available { get; set; } = true;
    public string GenerateResponse { get; set; } = "FAKE";
    public IReadOnlyList<string> Models { get; set; } = new[] { "llama3.1:8b" };
    public List<string> Prompts { get; } = new();

    /// <summary>When set, GenerateAsync throws this (simulates Ollama error / timeout).</summary>
    public Exception? GenerateException { get; set; }

    // Probe counters (availability / model checks) — let tests verify how often
    // a consumer re-probes the server (staleness/recheck behavior).
    private readonly object _probeGate = new();
    public int AvailableProbeCount { get { lock (_probeGate) return _availableProbeCount; } }
    public int ModelProbeCount { get { lock (_probeGate) return _modelProbeCount; } }
    private int _availableProbeCount;
    private int _modelProbeCount;

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        Task.Yield();
        lock (_probeGate) _availableProbeCount++;
        return Task.FromResult(Available);
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<string> result = Available ? Models : Array.Empty<string>();
        return Task.FromResult(result);
    }

    public Task<bool> ModelAvailableAsync(string model, CancellationToken ct = default)
    {
        lock (_probeGate) _modelProbeCount++;
        bool ok = Available && (Models.Contains(model, StringComparer.OrdinalIgnoreCase)
            || Models.Any(m => m.StartsWith(model, StringComparison.OrdinalIgnoreCase) && m.Length > model.Length));
        return Task.FromResult(ok);
    }

    public Task<string> GenerateAsync(string model, string prompt, double? temperature = null, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<string>(ct);
        if (GenerateException is not null)
            throw GenerateException; // simulated failure: nothing was sent
        lock (Prompts) Prompts.Add(prompt);
        return Task.FromResult(Available ? GenerateResponse : string.Empty);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
