using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs.Embeddings;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Embeddings.Providers;

public class AzureOpenAIEmbeddingProviderTests
{
    [Fact]
    public async Task ShouldPostToTheDeploymentWithApiKeyHeader_WhenEmbedding()
    {
        using var handler = EmbeddingHttpFixtures.Handler(1, 1536);
        using var http = new HttpClient(handler);
        var provider = new AzureOpenAIEmbeddingProvider(http, EmbeddingHttpFixtures.Options(o =>
        {
            o.Provider = "azure-openai";
            o.Model = "my-embeddings";
            o.Endpoint = "https://contoso.openai.azure.com/";
            o.ApiVersion = "2024-10-21";
            o.ApiKey = "az-key";
        }));

        var vector = await provider.GetEmbeddingAsync("hello", TestContext.Current.CancellationToken);

        Assert.Equal(1536, vector.Length);
        Assert.Equal(
            "https://contoso.openai.azure.com/openai/deployments/my-embeddings/embeddings?api-version=2024-10-21",
            handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("az-key", Assert.Single(handler.LastRequest.Headers.GetValues("api-key")));
        Assert.Null(handler.LastRequest.Headers.Authorization);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(1, body.RootElement.GetProperty("input").GetArrayLength());
    }

    [Fact]
    public async Task ShouldEscapeTheDeploymentName_WhenItHoldsReservedCharacters()
    {
        using var handler = EmbeddingHttpFixtures.Handler(1, 1536);
        using var http = new HttpClient(handler);
        var provider = new AzureOpenAIEmbeddingProvider(http, EmbeddingHttpFixtures.Options(o =>
        {
            o.Model = "a b/c";
            o.Endpoint = "https://contoso.openai.azure.com";
            o.ApiKey = "k";
        }));

        await provider.GetEmbeddingAsync("hello", TestContext.Current.CancellationToken);

        Assert.Contains("/openai/deployments/a%20b%2Fc/embeddings", handler.LastRequest!.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldNameTheEndpointSetting_WhenNoEndpointIsConfigured()
    {
        using var handler = EmbeddingHttpFixtures.Handler(1, 1536);
        using var http = new HttpClient(handler);
        var provider = new AzureOpenAIEmbeddingProvider(http, EmbeddingHttpFixtures.Options(o => o.ApiKey = "k"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetEmbeddingAsync("hello", TestContext.Current.CancellationToken));

        Assert.Contains("Orkeon:Embeddings:Endpoint", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.SendCallCount);
    }

    [Fact]
    public async Task ShouldNameTheStatus_WhenTheDeploymentIsUnknown404()
    {
        using var handler = new MockHttpMessageHandler();
        handler.SetResponse(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"DeploymentNotFound\"}}", "application/json");
        using var http = new HttpClient(handler);
        var provider = new AzureOpenAIEmbeddingProvider(http, EmbeddingHttpFixtures.Options(o =>
        {
            o.Endpoint = "https://contoso.openai.azure.com";
            o.ApiKey = "k";
        }));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GetEmbeddingAsync("hello", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Contains("Azure", ex.Message, StringComparison.Ordinal);
        Assert.Contains("DeploymentNotFound", ex.Message, StringComparison.Ordinal);
    }
}
