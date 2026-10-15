using System.Net;
using System.Text;

namespace AusbildungsWegInterview.Tests.Fakes;

/// <summary>HTTP handler that returns canned JSON responses and records requests.</summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<string, (HttpStatusCode Code, string Body)> _responder;
    public List<(string Method, string Path, string? Body)> Requests { get; } = new();

    public StubHttpMessageHandler(Func<string, (HttpStatusCode, string)> responder)
        => _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var path = request.RequestUri!.PathAndQuery;
        Requests.Add((request.Method.Method, path, body));

        var (code, respBody) = _responder(path);
        return new HttpResponseMessage(code)
        {
            Content = new StringContent(respBody, Encoding.UTF8, "application/json")
        };
    }
}
