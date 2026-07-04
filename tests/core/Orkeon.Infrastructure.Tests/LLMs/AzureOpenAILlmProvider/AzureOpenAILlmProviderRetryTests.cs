using Orkeon.Domain.SharedKernel.ValueObjects;
using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Resilience;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Tests verifying that AzureOpenAILlmProvider uses the Polly resilience policy
/// for retry on transient HTTP errors (5xx, 429). Covers TASK-007 fix.
/// </summary>
public class AzureOpenAILlmProviderRetryTests
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<AzureOpenAILlmProvider> _logger;
    private readonly LlmConfig _config;

    private static readonly string SuccessResponse = JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = "Azure response" } } },
        usage = new { total_tokens = 150 }
    });

    public AzureOpenAILlmProviderRetryTests()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<AzureOpenAILlmProvider>();
        _config = LlmConfig.Create(ModelGpt4, TestApiKey) with
        {
            BaseUrl = new Uri("https://myinstance.openai.azure.com")
        };
    }

    [Fact]
    public async Task ShouldRetryAndSucceed_WhenGenerateAsyncWith500ThenSuccess()
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
        _httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        var retryPolicy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(10));
        using var provider = new AzureOpenAILlmProvider(_config, _httpClientFactory, retryPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Azure response", result.Content);
        Assert.Equal(150, result.TokensUsed);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithPersistent500()
    {
        // Arrange
        var callCount = 0;
        using var handler = new TestHttpMessageHandler(request =>
        {
            Interlocked.Increment(ref callCount);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Server Error")
            };
        });
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        var retryPolicy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 2, baseDelay: TimeSpan.FromMilliseconds(10));
        using var provider = new AzureOpenAILlmProvider(_config, _httpClientFactory, retryPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — 1 initial + 2 retries = 3 total
        Assert.Empty(result.Content);
        Assert.Contains("Azure OpenAI API error: InternalServerError", result.Metadata["error"].ToString());
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task ShouldRetryOn429_WhenGenerateAsyncWithRateLimitThenSuccess()
    {
        // Arrange
        var callCount = 0;
        using var handler = new TestHttpMessageHandler(request =>
        {
            Interlocked.Increment(ref callCount);
            if (callCount == 1)
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("Rate limited")
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessResponse)
            };
        });
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        var retryPolicy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(10));
        using var provider = new AzureOpenAILlmProvider(_config, _httpClientFactory, retryPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Azure response", result.Content);
        Assert.Equal(2, callCount);
    }
}
