using System.Net;

namespace Orkeon.Studio.Core.Tests.Doubles;

/// <summary>What the handler was asked to send, captured before the request is disposed.</summary>
/// <param name="Method">HTTP verb.</param>
/// <param name="Uri">Absolute request URI.</param>
/// <param name="Authorization">Value of the <c>Authorization</c> header, or null.</param>
/// <param name="ApiKeyHeader">Value of Anthropic's <c>x-api-key</c> header, or null.</param>
public sealed record RecordedHttpRequest(string Method, Uri? Uri, string? Authorization, string? ApiKeyHeader);

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers from a script instead of the network, so
/// the endpoint probe can be driven — success, rejection, transport failure, silence — without
/// a single packet leaving the test.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    /// <summary>Every request the handler was given, in call order.</summary>
    public List<RecordedHttpRequest> Requests { get; } = [];

    /// <summary>Status of the scripted answer.</summary>
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    /// <summary>Body of the scripted answer.</summary>
    public string Body { get; set; } = "{}";

    /// <summary>When set, thrown instead of answering — a transport failure.</summary>
    public Exception? FailWith { get; set; }

    /// <summary>How long the handler waits before answering, to exercise the probe's deadline.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>The single request, when exactly one was sent.</summary>
    public RecordedHttpRequest LastRequest => Requests[^1];

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Requests.Add(new RecordedHttpRequest(
            request.Method.Method,
            request.RequestUri,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("x-api-key", out var apiKey) ? string.Join(",", apiKey) : null));

        if (Delay > TimeSpan.Zero)
            await Task.Delay(Delay, cancellationToken);

        if (FailWith is not null)
            throw FailWith;

        return new HttpResponseMessage(StatusCode) { Content = new StringContent(Body) };
    }
}
