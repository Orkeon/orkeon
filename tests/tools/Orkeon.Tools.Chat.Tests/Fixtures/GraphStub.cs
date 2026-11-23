using System.Net;

namespace Orkeon.Tools.Chat.Tests.Fixtures;

/// <summary>
/// Microsoft Graph and its identity provider, replayed: answers by method and exact path. The
/// answers of one route are given in order, the last one for good. What no route answers is a 404.
/// </summary>
internal sealed class GraphStub
{
    private readonly Dictionary<string, Queue<Func<RecordedRequest, FakeHttpResponse>>> _routes = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>Answers everything <paramref name="handler"/> receives.</summary>
    public GraphStub(FakeHttpMessageHandler handler)
    {
        Handler = handler;
        handler.Map(HttpMethod.Get, "https://", Answer);
        handler.Map(HttpMethod.Post, "https://", Answer);
    }

    /// <summary>What received the requests.</summary>
    public FakeHttpMessageHandler Handler { get; }

    /// <summary>The Graph requests that write, in order.</summary>
    public IReadOnlyList<RecordedRequest> Writes =>
        Handler.Requests.Where(request => request.Method != HttpMethod.Get && request.Uri.AbsolutePath.StartsWith("/v1.0/", StringComparison.Ordinal)).ToList();

    /// <summary>Answers a GET of Graph with JSON.</summary>
    public GraphStub Get(string path, string json) => On(HttpMethod.Get, path, _ => FakeHttpResponse.Json(json));

    /// <summary>Answers a POST to Graph with JSON, as a 201.</summary>
    public GraphStub Post(string path, string json) => On(HttpMethod.Post, path, _ => new FakeHttpResponse(HttpStatusCode.Created, json));

    /// <summary>Answers a Graph request with a computed answer, or an exception.</summary>
    public GraphStub On(HttpMethod method, string path, Func<RecordedRequest, FakeHttpResponse> respond)
    {
        lock (_gate)
        {
            var key = $"{method} /v1.0/{path}";
            if (!_routes.TryGetValue(key, out var answers))
                _routes[key] = answers = new Queue<Func<RecordedRequest, FakeHttpResponse>>();
            answers.Enqueue(respond);
        }

        return this;
    }

    /// <summary>Answers a GET of Graph with a 403, whatever answered it before.</summary>
    public GraphStub Refuse(string path)
    {
        lock (_gate)
            _routes.Remove($"GET /v1.0/{path}");
        return On(HttpMethod.Get, path, _ => Error(HttpStatusCode.Forbidden, "Forbidden"));
    }

    /// <summary>Answers an endpoint of the identity provider (<c>token</c>, <c>devicecode</c>).</summary>
    public GraphStub Identity(string endpoint, string json, Action? meanwhile = null)
    {
        lock (_gate)
            _routes[$"POST /{TestChatAccounts.Tenant}/oauth2/v2.0/{endpoint}"] = new Queue<Func<RecordedRequest, FakeHttpResponse>>(
            [
                _ =>
                {
                    meanwhile?.Invoke();
                    return FakeHttpResponse.Json(json);
                },
            ]);
        return this;
    }

    /// <summary>The requests of <paramref name="method"/> to <paramref name="path"/> of Graph.</summary>
    public IReadOnlyList<RecordedRequest> To(HttpMethod method, string path) =>
        Handler.Requests.Where(request => request.Method == method && Uri.UnescapeDataString(request.Uri.AbsolutePath) == $"/v1.0/{path}").ToList();

    /// <summary>The requests the identity provider's token endpoint received.</summary>
    public IReadOnlyList<RecordedRequest> TokenRequests =>
        Handler.Requests.Where(request => request.Uri.AbsolutePath.EndsWith("/oauth2/v2.0/token", StringComparison.Ordinal)).ToList();

    /// <summary>A Graph error.</summary>
    public static FakeHttpResponse Error(HttpStatusCode status, string code, string message = "refused", TimeSpan? retryAfter = null) =>
        new(status, $$$"""{"error":{"code":"{{{code}}}","message":"{{{message}}}"}}""", RetryAfter: retryAfter);

    private FakeHttpResponse Answer(RecordedRequest request)
    {
        Func<RecordedRequest, FakeHttpResponse>? respond = null;
        lock (_gate)
        {
            if (_routes.TryGetValue($"{request.Method} {Uri.UnescapeDataString(request.Uri.AbsolutePath)}", out var answers))
                respond = answers.Count > 1 ? answers.Dequeue() : answers.Peek();
        }

        return respond?.Invoke(request) ?? Error(HttpStatusCode.NotFound, "NotFound", "not mapped");
    }
}
