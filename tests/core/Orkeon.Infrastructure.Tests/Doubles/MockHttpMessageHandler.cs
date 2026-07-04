using System.Net;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for HttpMessageHandler with call tracking and configurable responses.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private HttpResponseMessage _response = new(HttpStatusCode.OK) { Content = new StringContent("") };
    private Func<HttpRequestMessage, HttpResponseMessage>? _responseFactory;

    // --- Tracking ---
    public int SendCallCount { get; private set; }

    /// <summary>
    /// A buffered clone of the last request. Cloned so its body remains readable
    /// after the caller disposes the original request/content post-send (R10.2).
    /// </summary>
    public HttpRequestMessage? LastRequest { get; private set; }

    /// <summary>The last request body, captured as a string before disposal.</summary>
    public string? LastRequestBody { get; private set; }

    public List<HttpRequestMessage> AllRequests { get; } = [];

    // --- Configuration ---
    public void SetResponse(HttpResponseMessage response) => _response = response;

    public void SetResponse(HttpStatusCode status, string content) =>
        _response = new HttpResponseMessage(status) { Content = new StringContent(content) };

    public void SetResponse(HttpStatusCode status, string content, string mediaType) =>
        _response = new HttpResponseMessage(status)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, mediaType)
        };

    /// <summary>
    /// Sets a factory function that generates a response per request.
    /// Useful when different requests should return different responses.
    /// </summary>
    public void SetResponseFactory(Func<HttpRequestMessage, HttpResponseMessage> factory) =>
        _responseFactory = factory;

    // --- HttpMessageHandler ---
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        SendCallCount++;

        // Buffer the request (and its body) into an independent clone *before* the
        // caller disposes the original message/content after send (R10.2). Reading
        // LastRequest.Content later — a common assertion pattern in these tests —
        // would otherwise throw ObjectDisposedException on the disposed content.
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            LastRequestBody = System.Text.Encoding.UTF8.GetString(bytes);
            var buffered = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
                buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
            clone.Content = buffered;
        }
        else
        {
            LastRequestBody = null;
        }

        LastRequest = clone;
        AllRequests.Add(clone);

        return _responseFactory != null ? _responseFactory(request) : _response;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _response.Dispose();
        }

        base.Dispose(disposing);
    }
}
