using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs.Embeddings;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Embeddings.Providers;

public class GeminiEmbeddingProviderTests
{
    [Fact]
    public async Task ShouldPostToTheOpenAiCompatibleEndpointWithBearerKey_WhenEmbedding()
    {
        using var handler = EmbeddingHttpFixtures.Handler(1, 3072);
        using var http = new HttpClient(handler);
        var provider = new GeminiEmbeddingProvider(http, EmbeddingHttpFixtures.Options(o =>
        {
            o.Provider = "gemini";
            o.Model = "gemini-embedding-001";
            o.Dimension = 3072;
            o.ApiKey = "g-key";
        }));

        var vector = await provider.GetEmbeddingAsync("hello", TestContext.Current.CancellationToken);

        Assert.Equal(3072, vector.Length);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/openai/embeddings",
            handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer g-key", handler.LastRequest.Headers.Authorization!.ToString());
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("gemini-embedding-001", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task ShouldHonourACustomEndpoint_WhenOneIsConfigured()
    {
        using var handler = EmbeddingHttpFixtures.Handler(1, 3072);
        using var http = new HttpClient(handler);
        var provider = new GeminiEmbeddingProvider(http, EmbeddingHttpFixtures.Options(o =>
        {
            o.ApiKey = "k";
            o.Endpoint = "http://127.0.0.1:9/proxy/";
        }));

        await provider.GetEmbeddingAsync("hello", TestContext.Current.CancellationToken);

        Assert.Equal("http://127.0.0.1:9/proxy/embeddings", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ShouldUseGeminiModelAndDimension_WhenOptionsKeepTheGlobalDefaults()
    {
        using var handler = EmbeddingHttpFixtures.Handler(1, 3072);
        using var http = new HttpClient(handler);
        var provider = new GeminiEmbeddingProvider(http, EmbeddingHttpFixtures.Options(o => o.ApiKey = "k"));

        await provider.GetEmbeddingAsync("hello", TestContext.Current.CancellationToken);

        Assert.Equal("gemini-embedding-001", provider.Model);
        Assert.Equal(3072, provider.Dimensions);
    }

    [Fact]
    public async Task ShouldNameTheStatusAndTheBody_WhenTheApiAnswers400()
    {
        using var handler = new MockHttpMessageHandler();
        handler.SetResponse(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"API key not valid\"}}", "application/json");
        using var http = new HttpClient(handler);
        var provider = new GeminiEmbeddingProvider(http, EmbeddingHttpFixtures.Options(o => o.ApiKey = "g-secret"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GetEmbeddingAsync("hello", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("Gemini", ex.Message, StringComparison.Ordinal);
        Assert.Contains("API key not valid", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("g-secret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldReturnVectorsInInputOrder_WhenTheApiListsThemOutOfOrder()
    {
        using var handler = EmbeddingHttpFixtures.Handler(3, 3072);
        using var http = new HttpClient(handler);
        var provider = new GeminiEmbeddingProvider(http, EmbeddingHttpFixtures.Options(o => o.ApiKey = "k"));

        var vectors = await provider.GetEmbeddingsAsync(["a", "b", "c"], TestContext.Current.CancellationToken);

        Assert.Equal([0f, 1f, 2f], vectors.Select(v => v[0]));
    }

    [Fact]
    public void ShouldRefuseAtConstruction_WhenDimensionIsNotTheFixedOneOfGeminiEmbedding()
    {
        using var handler = new MockHttpMessageHandler();
        using var http = new HttpClient(handler);

        var ex = Assert.Throws<InvalidOperationException>(() => new GeminiEmbeddingProvider(
            http, EmbeddingHttpFixtures.Options(o => { o.Dimension = 768; o.ApiKey = "k"; })));

        Assert.Contains("3072", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Embeddings:Dimension", ex.Message, StringComparison.Ordinal);
    }
}
