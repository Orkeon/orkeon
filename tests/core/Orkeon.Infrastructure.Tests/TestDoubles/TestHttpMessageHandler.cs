using System.Net;

namespace Orkeon.Infrastructure.Tests.TestDoubles;

public class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage>? _responseFactory;
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>>? _asyncResponseFactory;
    private readonly List<HttpRequestMessage> _capturedRequests = [];

    public IReadOnlyList<HttpRequestMessage> CapturedRequests => _capturedRequests;

    public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        _responseFactory = responseFactory;
    }

    public TestHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> asyncResponseFactory)
    {
        _asyncResponseFactory = asyncResponseFactory;
    }

    public static TestHttpMessageHandler CreateWithResponse(HttpStatusCode statusCode, string content)
    {
        return new TestHttpMessageHandler(request =>
            new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            });
    }

    public static TestHttpMessageHandler CreateWithException(Exception exception)
    {
        return new TestHttpMessageHandler((Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw exception));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Capture a buffered clone *before* the caller disposes the original request/
        // content after send (R10.2). Tests read CapturedRequests[..].Content later,
        // which would otherwise throw ObjectDisposedException on the disposed content.
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var buffered = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
                buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
            clone.Content = buffered;
        }
        _capturedRequests.Add(clone);

        if (_asyncResponseFactory != null)
        {
            return await _asyncResponseFactory(request);
        }

        return _responseFactory!(request);
    }
}
