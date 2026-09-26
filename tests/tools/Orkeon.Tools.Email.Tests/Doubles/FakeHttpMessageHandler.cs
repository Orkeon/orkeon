using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Orkeon.Tools.Email.Tests.Doubles;

/// <summary>A request the <see cref="FakeHttpMessageHandler"/> received, body read at send time.</summary>
/// <param name="Method">HTTP method.</param>
/// <param name="Uri">Absolute request address.</param>
/// <param name="Authorization">The <c>Authorization</c> header, or null.</param>
/// <param name="Prefer">The <c>Prefer</c> header, or null.</param>
/// <param name="ContentType">The body's media type, or null.</param>
/// <param name="Body">The body as text, or null.</param>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Prefer, string? ContentType, string? Body)
{
    /// <summary>The body read as an <c>application/x-www-form-urlencoded</c> form.</summary>
    public IReadOnlyDictionary<string, string> Form
    {
        get
        {
            var form = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in (Body ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var cut = pair.IndexOf('=', StringComparison.Ordinal);
                var key = WebUtility.UrlDecode(cut < 0 ? pair : pair[..cut]);
                form[key] = cut < 0 ? string.Empty : WebUtility.UrlDecode(pair[(cut + 1)..]);
            }

            return form;
        }
    }

    /// <summary>The request address with its query decoded, for readable assertions.</summary>
    public string DecodedUri => Uri.UnescapeDataString(Uri.AbsoluteUri);
}

/// <summary>What a <see cref="FakeHttpMessageHandler"/> answers.</summary>
/// <param name="Status">Status code.</param>
/// <param name="Body">Body text.</param>
/// <param name="MediaType">Body media type.</param>
/// <param name="RetryAfter">Value of a <c>Retry-After</c> header, when set.</param>
public sealed record FakeHttpResponse(HttpStatusCode Status, string Body, string MediaType = "application/json", TimeSpan? RetryAfter = null)
{
    /// <summary>A 200 JSON answer.</summary>
    public static FakeHttpResponse Json(string body) => new(HttpStatusCode.OK, body);
}

/// <summary>
/// Hand-written HTTP double: answers each request from the first route whose method and address
/// prefix match, else from a FIFO queue, else with 404; records every request with its body read
/// when it was sent (the pipeline disposes the content afterwards).
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Lock _gate = new();
    private readonly List<(HttpMethod Method, string Prefix, Func<RecordedRequest, FakeHttpResponse> Respond)> _routes = [];
    private readonly Queue<Func<RecordedRequest, FakeHttpResponse>> _queue = new();
    private readonly List<RecordedRequest> _requests = [];

    /// <summary>Every request received, in order.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
                return [.. _requests];
        }
    }

    /// <summary>Queues the answer of the next unrouted request.</summary>
    public void Enqueue(FakeHttpResponse response) => Enqueue(_ => response);

    /// <summary>Queues a 200 JSON answer.</summary>
    public void EnqueueJson(string json) => Enqueue(FakeHttpResponse.Json(json));

    /// <summary>Queues a computed answer (or a thrown exception) for the next unrouted request.</summary>
    public void Enqueue(Func<RecordedRequest, FakeHttpResponse> respond)
    {
        lock (_gate)
            _queue.Enqueue(respond);
    }

    /// <summary>Answers every <paramref name="method"/> request whose address starts with <paramref name="prefix"/>.</summary>
    public void Map(HttpMethod method, string prefix, FakeHttpResponse response) => Map(method, prefix, _ => response);

    /// <summary>Answers every <paramref name="method"/> request whose address starts with <paramref name="prefix"/>.</summary>
    public void Map(HttpMethod method, string prefix, Func<RecordedRequest, FakeHttpResponse> respond)
    {
        lock (_gate)
            _routes.Add((method, prefix, respond));
    }

    /// <summary>The requests whose decoded address starts with <paramref name="prefix"/>.</summary>
    public IReadOnlyList<RecordedRequest> RequestsTo(HttpMethod method, string prefix) =>
        Requests.Where(r => r.Method == method && r.DecodedUri.StartsWith(prefix, StringComparison.Ordinal)).ToList();

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri ?? throw new InvalidOperationException("A request without an address."),
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("Prefer", out var prefer) ? string.Join(", ", prefer) : null,
            request.Content?.Headers.ContentType?.MediaType,
            body);

        Func<RecordedRequest, FakeHttpResponse>? respond;
        lock (_gate)
        {
            _requests.Add(recorded);
            respond = _routes
                .Where(route => route.Method == recorded.Method && recorded.DecodedUri.StartsWith(route.Prefix, StringComparison.Ordinal))
                .Select(route => route.Respond)
                .FirstOrDefault();
            if (respond is null && _queue.Count > 0)
                respond = _queue.Dequeue();
        }

        var answer = respond?.Invoke(recorded) ?? new FakeHttpResponse(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"ErrorItemNotFound\",\"message\":\"not mapped\"}}");
        var response = new HttpResponseMessage(answer.Status)
        {
            Content = new StringContent(answer.Body, Encoding.UTF8, answer.MediaType),
            RequestMessage = request,
        };
        if (answer.RetryAfter is { } delay)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(delay);
        return response;
    }
}
