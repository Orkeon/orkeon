using System.Net;

namespace Orkeon.Tools.Web.Tests.Doubles;

/// <summary>
/// Manual mock for HttpMessageHandler with call tracking and configurable responses.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private HttpResponseMessage _response = new(HttpStatusCode.OK) { Content = new StringContent("") };
    private Func<HttpRequestMessage, HttpResponseMessage>? _responseFactory;

    // --- Tracking ---
    public int SendCallCount { get; private set; }
    public HttpRequestMessage? LastRequest { get; private set; }
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
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        SendCallCount++;
        LastRequest = request;
        AllRequests.Add(request);

        var response = _responseFactory != null ? _responseFactory(request) : _response;
        return Task.FromResult(response);
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
