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

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        Task.Yield();
        return Task.FromResult(Available);
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<string> result = Available ? Models : Array.Empty<string>();
        return Task.FromResult(result);
    }

    public Task<string> GenerateAsync(string model, string prompt, double? temperature = null, CancellationToken ct = default)
    {
        lock (Prompts) Prompts.Add(prompt);
        return Task.FromResult(Available ? GenerateResponse : string.Empty);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
