using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Resilience;
using System.Net;
using System.Text.Json;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Base;

/// <summary>
/// Tests verifying that OpenAICompatibleProviderBase uses the Polly resilience policy
/// for retry on transient HTTP errors (5xx, 429). Covers TASK-007 fix.
/// All derived providers (OpenAI, Grok, DeepSeek, Qwen, Kimi, HuggingFace, TogetherAi)
/// inherit this behavior.
/// </summary>
public class OpenAICompatibleProviderBaseRetryTests
{
    private readonly TestDoubles.TestHttpClientFactory _httpClientFactory;
    private readonly TestDoubles.TestLogger<TestableOpenAICompatibleProvider> _logger;
    private readonly LlmConfig _config;

    private static readonly string SuccessResponse = JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = "Test response" } } },
        usage = new { total_tokens = 100 }
    });

    public OpenAICompatibleProviderBaseRetryTests()
    {
        _httpClientFactory = new TestDoubles.TestHttpClientFactory();
        _logger = new TestDoubles.TestLogger<TestableOpenAICompatibleProvider>();
        _config = LlmConfig.Create(TestModelName, TestApiKey);
    }

    [Fact]
    public async Task ShouldRetryAndSucceed_WhenGenerateAsyncWith500ThenSuccess()
    {
        // Arrange
        var callCount = 0;
        using var handler = new TestDoubles.TestHttpMessageHandler(request =>
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
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        var retryPolicy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(10));
        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, retryPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Test response", result.Content);
        Assert.Equal(100, result.TokensUsed);
        Assert.Equal(2, callCount);
    }

    /// <summary>A handler that never answers but honours the token — what a stalled endpoint looks like to HttpClient.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    [Fact]
    public async Task ShouldNameTheTimeoutSettingAndRetryOnce_WhenTheHttpTimeoutElapses()
    {
        // LLM-11 — the run of 2026-09-20: Kimi took longer than Llm:TimeoutSeconds, HttpClient
        // threw its timeout shape, the policy never retried it (its token reads as cancelled)
        // and the loop reported "the model answered empty, raise MaxTokens". The provider now
        // says which setting elapsed and the ways out, after exactly one more attempt.
        using var handler = new HangingHandler();
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);
        var config = _config with { TimeoutSeconds = 1 };
        var retryPolicy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 10, baseDelay: TimeSpan.FromMilliseconds(10));
        using var provider = new TestableOpenAICompatibleProvider(config, _httpClientFactory, retryPolicy, _logger);

        var result = await provider.ChatAsync([LlmMessage.User("score the contacts")], config, TestContext.Current.CancellationToken);

        Assert.Equal("", result.Content);
        Assert.NotNull(result.Error);
        Assert.StartsWith("Test did not answer within Llm:TimeoutSeconds = 1 s", result.Error, StringComparison.Ordinal);
        Assert.Contains("not an empty answer", result.Error, StringComparison.Ordinal);
        Assert.Contains("raise Llm:TimeoutSeconds", result.Error, StringComparison.Ordinal);
        Assert.Contains("thinking", result.Error, StringComparison.Ordinal);
        Assert.Equal(nameof(TaskCanceledException), result.ErrorType);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ShouldRetryAndSucceed_WhenChatAsyncWith500ThenSuccess()
    {
        // Arrange
        var callCount = 0;
        using var handler = new TestDoubles.TestHttpMessageHandler(request =>
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
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        var retryPolicy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(10));
        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, retryPolicy, _logger);

        // ChatAsync with tool metadata triggers the multi-turn path through SendChatRequestAsync
        var messages = new[]
        {
            LlmMessage.User("Hello"),
            new LlmMessage { Role = "assistant", Content = "Hi", RawToolCalls = "[{\"id\":\"call_1\",\"function\":{\"name\":\"test\",\"arguments\":\"{}\"}}]" },
            new LlmMessage { Role = "tool", Content = "result", ToolCallId = "call_1" }
        };

        // Act
        var result = await provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Test response", result.Content);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithPersistent500()
    {
        // Arrange
        var callCount = 0;
        using var handler = new TestDoubles.TestHttpMessageHandler(request =>
        {
            Interlocked.Increment(ref callCount);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Server Error")
            };
        });
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        var retryPolicy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 2, baseDelay: TimeSpan.FromMilliseconds(10));
        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, retryPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — 1 initial + 2 retries = 3 total
        Assert.Empty(result.Content);
        Assert.Contains("Test API error: InternalServerError", result.Metadata["error"].ToString());
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task ShouldRetryOn429_WhenGenerateAsyncWithRateLimitThenSuccess()
    {
        // Arrange
        var callCount = 0;
        using var handler = new TestDoubles.TestHttpMessageHandler(request =>
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
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        var retryPolicy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(10));
        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, retryPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Test response", result.Content);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ShouldNotRetryOn400_WhenGenerateAsyncWithBadRequest()
    {
        // Arrange — 400 is not a transient error, should not be retried
        var callCount = 0;
        using var handler = new TestDoubles.TestHttpMessageHandler(request =>
        {
            Interlocked.Increment(ref callCount);
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Bad Request")
            };
        });
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        var retryPolicy = ResiliencePolicies.GetLlmApiPolicy(
            _logger, maxRetryAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(10));
        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, retryPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — no retry, only 1 call
        Assert.Empty(result.Content);
        Assert.Equal(1, callCount);
    }
}
