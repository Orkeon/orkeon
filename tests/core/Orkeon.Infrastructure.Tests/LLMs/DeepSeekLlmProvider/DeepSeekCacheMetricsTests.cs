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
/// Regression coverage for Experiment 07 friction #5: the OpenAI-compatible base parser
/// must surface DeepSeek's <c>prompt_cache_hit_tokens</c> and <c>prompt_cache_miss_tokens</c>
/// as typed fields on <see cref="LlmResponse"/> so they propagate through the orchestrator
/// to AUTO_SUMMARY.
/// </summary>
public class DeepSeekCacheMetricsTests
{
    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<DeepSeekLlmProvider> _logger = new();
    private readonly LlmConfig _config = LlmConfig.Create("deepseek-v4-flash", TestApiKey);
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();

    private static string BuildResponseBody(int promptTokens, int completionTokens, int cacheHit, int cacheMiss)
        => JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "ok" } } },
            usage = new
            {
                prompt_tokens = promptTokens,
                completion_tokens = completionTokens,
                total_tokens = promptTokens + completionTokens,
                prompt_cache_hit_tokens = cacheHit,
                prompt_cache_miss_tokens = cacheMiss,
            }
        });

    [Fact]
    public async Task GenerateAsync_ShouldPopulateCacheHitAndMissOnResponse_WhenProviderReportsThem()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.OK,
            BuildResponseBody(promptTokens: 12340, completionTokens: 800, cacheHit: 9800, cacheMiss: 2540));
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", new HttpClient(handler));

        using var provider = new DeepSeekLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        var response = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(12340, response.PromptTokens);
        Assert.Equal(800, response.CompletionTokens);
        Assert.Equal(9800, response.CacheHitTokens);
        Assert.Equal(2540, response.CacheMissTokens);
    }

    [Fact]
    public async Task GenerateAsync_ShouldLeaveCacheFieldsNull_WhenProviderOmitsThem()
    {
        var body = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "ok" } } },
            usage = new { prompt_tokens = 100, completion_tokens = 50, total_tokens = 150 }
        });
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, body);
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", new HttpClient(handler));

        using var provider = new DeepSeekLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        var response = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(100, response.PromptTokens);
        Assert.Equal(50, response.CompletionTokens);
        Assert.Null(response.CacheHitTokens);
        Assert.Null(response.CacheMissTokens);
    }
}
