using System.Net;

namespace GitLabPortal.Tests.Infrastructure;

/// <summary>Records outgoing requests and replays a caller-supplied response.</summary>
public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public HttpRequestMessage LastRequest => Requests[^1];

    public static StubHttpMessageHandler Json(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });

    public static StubHttpMessageHandler Status(HttpStatusCode statusCode, string body = "") =>
        new(_ => new HttpResponseMessage(statusCode) { Content = new StringContent(body) });

    public static StubHttpMessageHandler Throws(Exception exception) =>
        new(_ => throw exception);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(responder(request));
    }
}

public sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
