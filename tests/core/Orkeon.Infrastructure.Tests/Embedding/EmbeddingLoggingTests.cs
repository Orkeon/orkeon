using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Analysis.Vectors;
using Orkeon.Application.Interfaces.Logging;
using Orkeon.Infrastructure.Logging;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.Embedding;

/// <summary>
/// Validates that embedding HTTP calls are captured by LlmLoggingDelegatingHandler
/// when the provider is constructed with an HttpMessageHandler overload.
/// No real HTTP calls are made — a mock inner handler returns pre-built JSON responses.
/// </summary>
public sealed class EmbeddingLoggingTests : IDisposable
{
    private static readonly float[] SampleEmbeddingVector = [0.1f, 0.2f, 0.3f];

    // Minimal valid OpenAI embeddings response for a single text input.
    private static readonly string ValidEmbeddingResponse = JsonSerializer.Serialize(new
    {
        data = new[]
        {
            new { index = 0, embedding = SampleEmbeddingVector }
        }
    });

    private readonly StubExchangeLogger _exchangeLogger;
    private readonly LlmLoggingDelegatingHandler _loggingHandler;
    private readonly FakeInnerHandler _innerHandler;

    public EmbeddingLoggingTests()
    {
        _exchangeLogger = new StubExchangeLogger();
        _innerHandler = new FakeInnerHandler(ValidEmbeddingResponse);

        _loggingHandler = new LlmLoggingDelegatingHandler(
            _exchangeLogger,
            NullLogger<LlmLoggingDelegatingHandler>.Instance)
        {
            InnerHandler = _innerHandler,
        };
    }

    [Fact]
    public async Task OpenAIEmbeddingProvider_WhenConstructedWithHandler_LogsExchangeWithEmbeddingUrl()
    {
        // Arrange
        var options = new OpenAIEmbeddingOptions
        {
            ApiKey = "sk-test-fake-key",
            BaseUrl = new Uri("https://api.openai.com/"),
            EmbeddingsPath = "v1/embeddings",
            Model = "text-embedding-3-small",
            Dimensions = 1536,
        };

        using var provider = new OpenAIEmbeddingProvider(options, _loggingHandler);

        // Act
        var vectors = await provider.EmbedBatchAsync(["hello world"], CancellationToken.None);

        // Allow the fire-and-forget logging call to complete (deterministic wait).
        await Polling.WaitUntilAsync(() => _exchangeLogger.Records.Count > 0);

        // Assert — provider returned a vector.
        Assert.Single(vectors);

        // Assert — logger was called exactly once.
        Assert.Single(_exchangeLogger.Records);

        // Assert — the captured URL contains /v1/embeddings.
        var capturedRecord = _exchangeLogger.Records[0];
        Assert.Contains("/v1/embeddings", capturedRecord.RequestUrl!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OllamaEmbeddingProvider_WhenConstructedWithHandler_LogsExchangeWithEmbeddingUrl()
    {
        // Arrange — Ollama returns a different JSON shape.
        var ollamaResponse = JsonSerializer.Serialize(new
        {
            embedding = SampleEmbeddingVector
        });
        var ollamaInner = new FakeInnerHandler(ollamaResponse);
        var ollamaLogger = new StubExchangeLogger();
        using var ollamaLoggingHandler = new LlmLoggingDelegatingHandler(
            ollamaLogger,
            NullLogger<LlmLoggingDelegatingHandler>.Instance)
        {
            InnerHandler = ollamaInner,
        };

        var options = new OllamaEmbeddingOptions
        {
            BaseUrl = new Uri("http://localhost:11434/"),
            EmbeddingsPath = "api/embeddings",
            Model = "nomic-embed-text",
            Dimensions = 768,
        };

        using var provider = new OllamaEmbeddingProvider(options, ollamaLoggingHandler);

        // Act
        var vectors = await provider.EmbedBatchAsync(["hello world"], CancellationToken.None);

        // Allow the fire-and-forget logging call to complete (deterministic wait).
        await Polling.WaitUntilAsync(() => ollamaLogger.Records.Count > 0);

        // Assert — provider returned a vector.
        Assert.Single(vectors);

        // Assert — the captured URL contains the Ollama embeddings path.
        Assert.Single(ollamaLogger.Records);
        Assert.Contains("embeddings", ollamaLogger.Records[0].RequestUrl!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _loggingHandler.Dispose();
        _innerHandler.Dispose();
    }

    /// <summary>Hand-rolled <see cref="ILlmExchangeLogger"/> that captures every call.</summary>
    private sealed class StubExchangeLogger : ILlmExchangeLogger
    {
        public List<LlmExchangeRecord> Records { get; } = new();

        public System.Threading.Tasks.Task LogExchangeAsync(LlmExchangeRecord exchange, CancellationToken cancellationToken = default)
        {
            Records.Add(exchange);
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    /// <summary>
    /// Minimal <see cref="HttpMessageHandler"/> that always returns a fixed JSON response.
    /// No network calls are made.
    /// </summary>
    private sealed class FakeInnerHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public FakeInnerHandler(string responseJson) => _responseJson = responseJson;

        protected override System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json"),
            };
            return System.Threading.Tasks.Task.FromResult(response);
        }
    }
}
