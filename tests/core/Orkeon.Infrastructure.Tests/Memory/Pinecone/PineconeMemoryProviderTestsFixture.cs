using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Memory.Pinecone;
using Orkeon.Infrastructure.Tests.Memory.ChromaDb;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.Tests.Memory.Pinecone;

public class PineconeMemoryProviderTestsFixture
{
    private readonly TestLogger<PineconeMemoryProvider> _logger;

    public PineconeMemoryProviderTestsFixture()
    {
        _logger = new TestLogger<PineconeMemoryProvider>();
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The HttpClient is captured by the returned provider and must outlive this factory; it lives for the duration of the test.")]
    public PineconeMemoryProvider CreateProvider(FakeHttpMessageHandler handler, PineconeOptions? options = null)
    {
        options ??= new PineconeOptions
        {
            ApiKey = TestApiKey,
            Environment = "us-east1-gcp",
            IndexName = "test-index",
            Namespace = "test-namespace"
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://test-index.svc.test.pinecone.io")
        };

        var optionsWrapper = Options.Create(options);
        return new PineconeMemoryProvider(httpClient, optionsWrapper, _logger);
    }

    public TestLogger<PineconeMemoryProvider> GetLogger() => _logger;

    /// <summary>
    /// Creates a handler with a pre-configured response.
    /// </summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Responses are handed to the returned FakeHttpMessageHandler and disposed by the HTTP pipeline when consumed.")]
    public static FakeHttpMessageHandler CreateHandler(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        object? responseBody = null)
    {
        var handler = new FakeHttpMessageHandler();

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
