using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs.Embeddings;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Embeddings.Providers;

public class MistralEmbeddingProviderTests
{
    [Fact]
    public async Task ShouldPostToMistralEmbeddingsWithBearerKey_WhenEmbeddingABatch()
    {
        using var handler = EmbeddingHttpFixtures.Handler(2, 1024);
        using var http = new HttpClient(handler);
        var provider = new MistralEmbeddingProvider(http, EmbeddingHttpFixtures.Options(o =>
        {
            o.Provider = "mistral";
            o.Model = "mistral-embed";
            o.Dimension = 1024;
            o.ApiKey = "sk-test";
        }));

        var vectors = await provider.GetEmbeddingsAsync(["a", "b"], TestContext.Current.CancellationToken);

        Assert.Equal(2, vectors.Count);
        Assert.Equal(1024, vectors[0].Length);
        Assert.Equal(0f, vectors[0][0]);
        Assert.Equal(1f, vectors[1][0]);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://api.mistral.ai/v1/embeddings", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", handler.LastRequest.Headers.Authorization!.ToString());
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("mistral-embed", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(2, body.RootElement.GetProperty("input").GetArrayLength());
    }

    [Fact]
    public async Task ShouldUseMistralModelAndDimension_WhenOptionsKeepTheGlobalDefaults()
    {
        using var handler = EmbeddingHttpFixtures.Handler(1, 1024);
        using var http = new HttpClient(handler);
        var provider = new MistralEmbeddingProvider(http, EmbeddingHttpFixtures.Options(o => o.ApiKey = "k"));

        var vector = await provider.GetEmbeddingAsync("a", TestContext.Current.CancellationToken);

        Assert.Equal("mistral-embed", provider.Model);
        Assert.Equal(1024, provider.Dimensions);
        Assert.Equal(1024, vector.Length);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("mistral-embed", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task ShouldNameTheStatusAndNeverTheKey_WhenTheApiAnswers401()
    {
        using var handler = new MockHttpMessageHandler();
        handler.SetResponse(HttpStatusCode.Unauthorized, "{\"message\":\"Unauthorized\"}", "application/json");
        using var http = new HttpClient(handler);
        var provider = new MistralEmbeddingProvider(http, EmbeddingHttpFixtures.Options(o => o.ApiKey = "sk-secret"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GetEmbeddingAsync("a", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Contains("Mistral", ex.Message, StringComparison.Ordinal);
        Assert.Contains("401", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-secret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldRefuseTheAnswer_WhenVectorsDoNotMatchTheDeclaredDimension()
    {
        using var handler = EmbeddingHttpFixtures.Handler(1, 768);
        using var http = new HttpClient(handler);
        var provider = new MistralEmbeddingProvider(http, EmbeddingHttpFixtures.Options(o => o.ApiKey = "k"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetEmbeddingAsync("a", TestContext.Current.CancellationToken));

        Assert.Contains("768", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1024", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldSplitTheBatchBySize_WhenMoreTextsThanBatchSize()
    {
        using var handler = new MockHttpMessageHandler();
        handler.SetResponseFactory(request =>
        {
            var count = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())
                .RootElement.GetProperty("input").GetArrayLength();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(EmbeddingHttpFixtures.Answer(count, 1024), System.Text.Encoding.UTF8, "application/json"),
            };
        });
        using var http = new HttpClient(handler);
        var provider = new MistralEmbeddingProvider(http, EmbeddingHttpFixtures.Options(o => { o.ApiKey = "k"; o.BatchSize = 2; }));

        var vectors = await provider.GetEmbeddingsAsync(["a", "b", "c"], TestContext.Current.CancellationToken);

        Assert.Equal(3, vectors.Count);
        Assert.Equal(2, handler.SendCallCount);
    }

    [Fact]
    public async Task ShouldReadTheKeyFromTheNamedVariable_WhenNoKeyIsWritten()
    {
        var variable = "ORKEON_TEST_MISTRAL_KEY_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, "from-env");
        try
        {
            using var handler = EmbeddingHttpFixtures.Handler(1, 1024);
            using var http = new HttpClient(handler);
            var provider = new MistralEmbeddingProvider(http, EmbeddingHttpFixtures.Options(o => o.ApiKeyEnvVar = variable));

            await provider.GetEmbeddingAsync("a", TestContext.Current.CancellationToken);

            Assert.Equal("Bearer from-env", handler.LastRequest!.Headers.Authorization!.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task ShouldSayWhereToPutTheKey_WhenNoneIsConfigured()
    {
        using var handler = EmbeddingHttpFixtures.Handler(1, 1024);
        using var http = new HttpClient(handler);
        var provider = new MistralEmbeddingProvider(http, EmbeddingHttpFixtures.Options(_ => { }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetEmbeddingAsync("a", TestContext.Current.CancellationToken));

        Assert.Contains("Orkeon:Embeddings:ApiKey", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.SendCallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "429")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "503")]
    public async Task ShouldRaiseTheStatusWithoutRetrying_WhenTheApiAnswers429Or5xx(HttpStatusCode status, string code)
    {
        using var handler = new MockHttpMessageHandler();
        handler.SetResponse(status, "{\"message\":\"busy\"}", "application/json");
        using var http = new HttpClient(handler);
        var provider = new MistralEmbeddingProvider(http, EmbeddingHttpFixtures.Options(o => o.ApiKey = "k"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GetEmbeddingAsync("a", TestContext.Current.CancellationToken));

        Assert.Equal(status, ex.StatusCode);
        Assert.Contains(code, ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.SendCallCount);
    }

    [Theory]
    [InlineData("mistral-embed", 768)]
    [InlineData("text-embedding-3-small", 768)]
    public void ShouldRefuseAtConstruction_WhenDimensionIsNotTheFixedOneOfMistralEmbed(string model, int dimension)
    {
        using var handler = new MockHttpMessageHandler();
        using var http = new HttpClient(handler);

        var ex = Assert.Throws<InvalidOperationException>(() => new MistralEmbeddingProvider(
            http, EmbeddingHttpFixtures.Options(o => { o.Model = model; o.Dimension = dimension; o.ApiKey = "k"; })));

        Assert.Contains("1024", ex.Message, StringComparison.Ordinal);
        Assert.Contains("768", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Embeddings:Dimension", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.SendCallCount);
    }
}
