using System.Net;
using System.Text.Json;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Z.AI reports its implicit context cache in the OpenAI-standard shape —
/// <c>usage.prompt_tokens_details.cached_tokens</c> (reads only, no miss counter).
/// The base parser must map it onto the typed <see cref="LlmResponse.CacheHitTokens"/> and
/// derive <see cref="LlmResponse.CacheMissTokens"/> from <c>prompt_tokens</c> so
/// <see cref="LlmResponse.CacheHitRatio"/> stays computable (exp02 GLM run analysis).
/// </summary>
public class ZaiCacheMetricsTests
{
    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<ZaiLlmProvider> _logger = new();
    private readonly LlmConfig _config = LlmConfig.Create("glm-5.2", TestApiKey);
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();

    private static string BuildResponseBody(int promptTokens, int completionTokens, int? cachedTokens)
        => JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "ok" } } },
            usage = new Dictionary<string, object?>
            {
                ["prompt_tokens"] = promptTokens,
                ["completion_tokens"] = completionTokens,
                ["total_tokens"] = promptTokens + completionTokens,
                ["prompt_tokens_details"] = cachedTokens is { } cached
                    ? new Dictionary<string, object> { ["cached_tokens"] = cached }
                    : null,
            }
        });

    [Fact]
    public async Task GenerateAsync_ShouldMapCachedTokensToTypedCacheFields_WhenProviderReportsThem()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.OK,
            BuildResponseBody(promptTokens: 90000, completionTokens: 1200, cachedTokens: 72000));
        _httpClientFactory.RegisterClient("ZaiLlmProvider", new HttpClient(handler));

        using var provider = new ZaiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        var response = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(90000, response.PromptTokens);
        Assert.Equal(72000, response.CacheHitTokens);
        Assert.Equal(18000, response.CacheMissTokens); // derived: prompt - cached
        Assert.NotNull(response.CacheHitRatio);
        Assert.Equal(0.8, response.CacheHitRatio!.Value, precision: 5);
        Assert.Equal(72000, response.Metadata["cached_tokens"]);
    }

    [Fact]
    public async Task GenerateAsync_ShouldReportZeroHitFullMiss_WhenCachedTokensIsZero()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.OK,
            BuildResponseBody(promptTokens: 5000, completionTokens: 300, cachedTokens: 0));
        _httpClientFactory.RegisterClient("ZaiLlmProvider", new HttpClient(handler));

        using var provider = new ZaiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        var response = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, response.CacheHitTokens);
        Assert.Equal(5000, response.CacheMissTokens);
        Assert.Equal(0.0, response.CacheHitRatio);
    }

    [Fact]
    public async Task GenerateAsync_ShouldLeaveCacheFieldsNull_WhenProviderOmitsDetails()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.OK,
            BuildResponseBody(promptTokens: 100, completionTokens: 50, cachedTokens: null));
        _httpClientFactory.RegisterClient("ZaiLlmProvider", new HttpClient(handler));

        using var provider = new ZaiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        var response = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(response.CacheHitTokens);
        Assert.Null(response.CacheMissTokens);
        Assert.Null(response.CacheHitRatio);
    }
}
