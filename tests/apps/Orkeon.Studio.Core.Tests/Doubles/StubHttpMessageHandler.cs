using System.Net;

namespace Orkeon.Studio.Core.Tests.Doubles;

/// <summary>What the handler was asked to send, captured before the request is disposed.</summary>
/// <param name="Method">HTTP verb.</param>
/// <param name="Uri">Absolute request URI.</param>
/// <param name="Authorization">Value of the <c>Authorization</c> header, or null.</param>
/// <param name="ApiKeyHeader">Value of Anthropic's <c>x-api-key</c> header, or null.</param>
/// <param name="Body">The request body, or null when the request carried none.</param>
public sealed record RecordedHttpRequest(
    string Method, Uri? Uri, string? Authorization, string? ApiKeyHeader, string? Body = null);

/// <summary>One scripted answer: a status and a body.</summary>
/// <param name="StatusCode">Status of the answer.</param>
/// <param name="Body">Body of the answer.</param>
public sealed record StubHttpAnswer(HttpStatusCode StatusCode, string Body);

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

    /// <summary>
    /// When set, answers each request in place of <see cref="StatusCode"/>/<see cref="Body"/> —
    /// so the catalogue and the test completion of one probe can be answered differently.
    /// </summary>
    public Func<RecordedHttpRequest, StubHttpAnswer>? Answer { get; set; }

    /// <summary>When set, thrown instead of answering — a transport failure.</summary>
    public Exception? FailWith { get; set; }

    /// <summary>How long the handler waits before answering, to exercise the probe's deadline.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>
    /// Runs before the answer, with the request and the probe's token — the hook through which a
    /// test moves a manual clock, or waits on the probe's deadline.
    /// </summary>
    public Func<RecordedHttpRequest, CancellationToken, Task>? OnSend { get; set; }

    /// <summary>The single request, when exactly one was sent.</summary>
    public RecordedHttpRequest LastRequest => Requests[^1];

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedHttpRequest(
            request.Method.Method,
            request.RequestUri,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("x-api-key", out var apiKey) ? string.Join(",", apiKey) : null,
            body);
        Requests.Add(recorded);

        if (OnSend is not null)
            await OnSend(recorded, cancellationToken);

        if (Delay > TimeSpan.Zero)
            await Task.Delay(Delay, cancellationToken);

        if (FailWith is not null)
            throw FailWith;

        var answer = Answer?.Invoke(recorded) ?? new StubHttpAnswer(StatusCode, Body);
        return new HttpResponseMessage(answer.StatusCode) { Content = new StringContent(answer.Body) };
    }
}
