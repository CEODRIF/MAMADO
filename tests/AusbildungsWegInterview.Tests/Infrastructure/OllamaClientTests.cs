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
}
