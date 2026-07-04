using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Memory.ChromaDb;
using Orkeon.Infrastructure.Tests.TestDoubles;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.Tests.Memory.ChromaDb;

public class ChromaDbMemoryProviderTestsFixture
{
    private readonly TestLogger<ChromaDbMemoryProvider> _logger;

    public ChromaDbMemoryProviderTestsFixture()
    {
        _logger = new TestLogger<ChromaDbMemoryProvider>();
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The HttpClient is captured by the returned provider and must outlive this factory; it lives for the duration of the test.")]
    public ChromaDbMemoryProvider CreateProvider(FakeHttpMessageHandler handler, ChromaDbOptions? options = null)
    {
        options ??= new ChromaDbOptions
        {
            BaseUrl = new Uri("http://localhost:8000"),
            CollectionName = "test_collection",
            DefaultTopK = 10
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = options.BaseUrl
        };

        var optionsWrapper = Options.Create(options);
        return new ChromaDbMemoryProvider(httpClient, optionsWrapper, _logger);
    }

    public TestLogger<ChromaDbMemoryProvider> GetLogger() => _logger;

    /// <summary>
    /// Creates a handler pre-configured with a collection creation response
    /// and an optional additional response.
    /// </summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Responses are handed to the returned FakeHttpMessageHandler and disposed by the HTTP pipeline when consumed.")]
    public static FakeHttpMessageHandler CreateHandlerWithCollection(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        object? responseBody = null)
    {
        var handler = new FakeHttpMessageHandler();

        // Always set up collection creation response
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { id = "test-collection-id", name = "test_collection" }),
                System.Text.Encoding.UTF8,
                "application/json")
        });

        // Then the actual operation response
        if (responseBody != null)
        {
            handler.EnqueueResponse(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(responseBody),
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        }
        else
        {
            handler.EnqueueResponse(new HttpResponseMessage(statusCode));
        }

        return handler;
    }
}

/// <summary>
/// A fake HTTP message handler that throws the configured exception on every request,
/// simulating an unreachable server.
/// </summary>
public class ThrowingHttpMessageHandler : FakeHttpMessageHandler
{
    private readonly Exception _exception;

    public ThrowingHttpMessageHandler(Exception exception)
    {
        _exception = exception;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return Task.FromException<HttpResponseMessage>(_exception);
    }
}

/// <summary>
/// A fake HTTP message handler that returns pre-configured responses in order.
/// </summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    private readonly List<HttpRequestMessage> _requests = [];

    /// <summary>Gets the list of captured requests.</summary>
    public IReadOnlyList<HttpRequestMessage> CapturedRequests => _requests;

    /// <summary>Enqueues a response to be returned on the next request.</summary>
    public void EnqueueResponse(HttpResponseMessage response)
    {
        _responses.Enqueue(response);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _requests.Add(request);

        if (_responses.Count == 0)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("No more responses configured in FakeHttpMessageHandler")
            });
        }

        return Task.FromResult(_responses.Dequeue());
    }
}
