using Orkeon.Application.Configuration;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs.Embeddings;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Embeddings;

public class OllamaEmbeddingProviderTests
{
    private readonly EmbeddingOptions _options;

    public OllamaEmbeddingProviderTests()
    {
        _options = new EmbeddingOptions
        {
            Provider = ProviderOllama,
            Model = "nomic-embed-text",
            Dimension = 768
        };
    }

    [Fact]
    public async Task ShouldSendCorrectHttpRequest_WhenGetEmbeddingAsync()
    {
        // Arrange
        var expectedEmbedding = new float[] { 0.1f, 0.2f, 0.3f };
        var responseJson = JsonSerializer.Serialize(new
        {
            embeddings = new[] { expectedEmbedding }
        });
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseJson);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(EndpointOllamaDefault) };
        var provider = new OllamaEmbeddingProvider(httpClient, Options.Create(_options));

        // Act
        var result = await provider.GetEmbeddingAsync("test text", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.Length);
        Assert.Equal(0.1f, result[0], precision: 5);
        Assert.Equal(0.2f, result[1], precision: 5);
        Assert.Single(handler.CapturedRequests);

        var request = handler.CapturedRequests[0];
        Assert.Equal("/api/embed", request.RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, request.Method);
    }

    [Fact]
    public async Task ShouldSendBatchRequest_WhenGetEmbeddingsAsync()
    {
        // Arrange
        var vec1 = new float[] { 0.1f, 0.2f };
        var vec2 = new float[] { 0.3f, 0.4f };
        var responseJson = JsonSerializer.Serialize(new
        {
            embeddings = new[] { vec1, vec2 }
        });
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseJson);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(EndpointOllamaDefault) };
        var provider = new OllamaEmbeddingProvider(httpClient, Options.Create(_options));

        // Act
        var results = await provider.GetEmbeddingsAsync(["text1", "text2"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal(vec1[0], results[0][0], precision: 5);
        Assert.Equal(vec2[0], results[1][0], precision: 5);
    }

    [Fact]
    public async Task ShouldReturnEmptyArray_WhenGetEmbeddingAsyncEmptyResponse()
    {
        // Arrange
        var responseJson = JsonSerializer.Serialize(new
        {
            embeddings = Array.Empty<float[]>()
        });
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseJson);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(EndpointOllamaDefault) };
        var provider = new OllamaEmbeddingProvider(httpClient, Options.Create(_options));

        // Act
        var result = await provider.GetEmbeddingAsync("test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ShouldReturnOllama_WhenName()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "{}");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(EndpointOllamaDefault) };
        var provider = new OllamaEmbeddingProvider(httpClient, Options.Create(_options));

        Assert.Equal("Ollama", provider.Name);
    }

    [Fact]
    public void ShouldReturnConfiguredModel_WhenModel()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "{}");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(EndpointOllamaDefault) };
        var provider = new OllamaEmbeddingProvider(httpClient, Options.Create(_options));

        Assert.Equal("nomic-embed-text", provider.Model);
    }
}
