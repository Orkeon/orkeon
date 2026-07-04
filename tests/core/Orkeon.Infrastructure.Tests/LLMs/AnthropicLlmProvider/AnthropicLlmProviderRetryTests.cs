using System.Net;
using System.Text.Json;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Resilience;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Tests verifying that AnthropicLlmProvider uses the Polly resilience policy
/// for retry on transient HTTP errors (5xx, 429). Covers TASK-007 fix.
/// </summary>
public class AnthropicLlmProviderRetryTests
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<AnthropicLlmProvider> _logger;
    private readonly LlmConfig _config;

    private static readonly string SuccessResponse = JsonSerializer.Serialize(new
    {
        content = new[] { new { type = "text", text = "Hello from Claude" } },
        usage = new { input_tokens = 10, output_tokens = 20 }
    });

    public AnthropicLlmProviderRetryTests()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<AnthropicLlmProvider>();
        _config = LlmConfig.Create(ModelClaude3Opus, TestApiKey) with
        {
            BaseUrl = new Uri("https://api.anthropic.com")
        };
    }

    [Fact]
    public async Task ShouldRetryAndSucceed_WhenGenerateAsyncWith500ThenSuccess()
    {
        // Arrange — first call returns 500, second returns 200
        var callCount = 0;
        using var handler = new TestHttpMessageHandler(request =>
        {
            Interlocked.Increment(ref callCount);
            if (callCount == 1)
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{\"error\":{\"type\":\"api_error\",\"message\":\"unknown error\"}}")
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessResponse)
            };
        });
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        var retryPolicy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(10));
        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, retryPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — should have retried once and returned the success response
        Assert.Equal("Hello from Claude", result.Content);
        Assert.Equal(30, result.TokensUsed);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ShouldRetryAndSucceed_WhenChatAsyncWith500ThenSuccess()
    {
        // Arrange
        var callCount = 0;
        using var handler = new TestHttpMessageHandler(request =>
        {
            Interlocked.Increment(ref callCount);
            if (callCount == 1)
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("Server Error")
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessResponse)
            };
        });
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        var retryPolicy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(10));
        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, retryPolicy, _logger);

        var messages = new[]
        {
            LlmMessage.System("You are a helpful assistant."),
            LlmMessage.User("Hello")
        };

        // Act
        var result = await provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Hello from Claude", result.Content);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithPersistent500()
    {
        // Arrange — all calls return 500
        var callCount = 0;
        using var handler = new TestHttpMessageHandler(request =>
        {
            Interlocked.Increment(ref callCount);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":{\"type\":\"api_error\",\"message\":\"persistent error\"}}")
            };
        });
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        var retryPolicy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 2, baseDelay: TimeSpan.FromMilliseconds(10));
        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, retryPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — should have attempted 1 initial + 2 retries = 3 total
        Assert.Empty(result.Content);
        Assert.Contains("Anthropic API error: InternalServerError", result.Metadata["error"].ToString());
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task ShouldRetryOn429_WhenGenerateAsyncWithRateLimitThenSuccess()
    {
        // Arrange — first call returns 429, second returns 200
        var callCount = 0;
        using var handler = new TestHttpMessageHandler(request =>
        {
            Interlocked.Increment(ref callCount);
            if (callCount == 1)
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("{\"error\":{\"type\":\"rate_limit_error\",\"message\":\"Rate limit exceeded\"}}")
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessResponse)
            };
        });
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        var retryPolicy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(10));
        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, retryPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Hello from Claude", result.Content);
        Assert.Equal(2, callCount);
    }
}
