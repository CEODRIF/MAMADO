using System.Net;
using AusbildungsWegInterview.Infrastructure.Ollama;
using AusbildungsWegInterview.Tests.Fakes;
using Xunit;

namespace AusbildungsWegInterview.Tests.Infrastructure;

public class OllamaClientTests
{
    [Fact]
    public async Task IsAvailable_True_WhenServerResponds()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK, "{\"models\":[]}"));
        using var http = new HttpClient(handler);
        var client = new OllamaClient("http://x", http, 500);
        Assert.True(await client.IsAvailableAsync());
    }

    [Fact]
    public async Task IsAvailable_False_OnServerError()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.InternalServerError, "{}"));
        using var http = new HttpClient(handler);
        var client = new OllamaClient("http://x", http);
        Assert.False(await client.IsAvailableAsync());
    }

    [Fact]
    public async Task ListModels_ParsesNames()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK,
            "{\"models\":[{\"name\":\"llama3.1:8b\"},{\"name\":\"phi3\"}]}"));
        using var http = new HttpClient(handler);
        var client = new OllamaClient("http://x", http);

        var names = await client.ListModelsAsync();

        Assert.Contains("llama3.1:8b", names);
        Assert.Contains("phi3", names);
    }

    [Fact]
    public async Task ModelAvailable_MatchesExactAndBaseName()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK,
            "{\"models\":[{\"name\":\"llama3.1:8b-q4_K_M\"}]}"));
        using var http = new HttpClient(handler);
        var client = new OllamaClient("http://x", http);

        Assert.True(await client.ModelAvailableAsync("llama3.1:8b"));   // base name prefix
        Assert.False(await client.ModelAvailableAsync("mistral:7b"));
    }

    [Fact]
    public async Task ModelAvailable_False_WhenUnreachable()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.InternalServerError, "{}"));
        using var http = new HttpClient(handler);
        var client = new OllamaClient("http://x", http);
        Assert.False(await client.ModelAvailableAsync("m"));
    }

    [Fact]
    public async Task Generate_ParsesResponseAndPostsToEndpoint()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK, "{\"response\":\"Hello there\"}"));
        using var http = new HttpClient(handler);
        var client = new OllamaClient("http://x", http);

        var result = await client.GenerateAsync("m", "some prompt", 0.3);

        Assert.Equal("Hello there", result);
        Assert.Single(handler.Requests);
        Assert.Equal("/api/generate", handler.Requests[0].Path);
        Assert.Equal("POST", handler.Requests[0].Method);
        Assert.Contains("\"model\":\"m\"", handler.Requests[0].Body);
    }

    [Fact]
    public async Task Generate_Throws_OnHttpError()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.BadRequest, "{\"error\":\"bad\"}"));
        using var http = new HttpClient(handler);
        var client = new OllamaClient("http://x", http);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GenerateAsync("m", "p"));
    }

    [Fact]
    public async Task Generate_Throws_OnOllamaErrorField()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK,
            "{\"error\":\"model 'm' not found\"}"));
        using var http = new HttpClient(handler);
        var client = new OllamaClient("http://x", http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateAsync("m", "p"));
        Assert.Contains("Ollama error", ex.Message);
    }

    [Fact]
    public async Task Generate_Throws_OnMalformedJson()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK, "this is not json"));
        using var http = new HttpClient(handler);
        var client = new OllamaClient("http://x", http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateAsync("m", "p"));
        Assert.Contains("Malformed", ex.Message);
    }

    [Fact]
    public async Task Generate_TimesOut_WhenServerHangs()
    {
        // Server accepts the request but never answers.
        using var handler = new HangingHandler();
        using var http = new HttpClient(handler);
        var client = new OllamaClient("http://x", http, 500, requestTimeoutMs: 300);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GenerateAsync("m", "p")); // TaskCanceledException is a subclass
        sw.Stop();
        // Bounded by the configured request timeout (~300 ms), not by the 300 s client timeout.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "Generation should be bounded by the request timeout.");
    }
}

/// <summary>Handler that never completes on its own; it only ends when the request token fires.</summary>
internal sealed class HangingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }
}
