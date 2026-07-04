using Orkeon.Domain.SharedKernel.ValueObjects;
using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class DeepSeekLlmProviderTests
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<DeepSeekLlmProvider> _logger;
    private readonly LlmConfig _config;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;

    public DeepSeekLlmProviderTests()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<DeepSeekLlmProvider>();
        _config = LlmConfig.Create("deepseek-chat", TestApiKey);
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
    }

    [Fact]
    public void ShouldReturnDeepSeek_WhenName()
    {
        using var provider = new DeepSeekLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        Assert.Equal("deepseek", provider.Name);
    }

    [Fact]
    public async Task ShouldReturnContent_WhenGenerateAsyncWithValidResponse()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "DeepSeek response" } }
            },
            usage = new { total_tokens = 80, prompt_tokens = 30, completion_tokens = 50 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", httpClient);

        using var provider = new DeepSeekLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("DeepSeek response", result.Content);
        Assert.Equal(80, result.TokensUsed);
        Assert.Equal("deepseek-chat", result.Model);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithMissingApiKey()
    {
        // Arrange
        var configWithoutKey = LlmConfig.Create("deepseek-chat", null);
        using var provider = new DeepSeekLlmProvider(configWithoutKey, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("DeepSeek API key is required", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithHttpError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Unauthorized, "Unauthorized");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", httpClient);

        using var provider = new DeepSeekLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("DeepSeek API error", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithException()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithException(new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", httpClient);

        using var provider = new DeepSeekLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Connection refused", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldIncludeReasoningContent_WhenGenerateAsyncWithR1Response()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = "The answer is 42.",
                        reasoning_content = "Let me think step by step. First, I need to consider..."
                    }
                }
            },
            usage = new { total_tokens = 200, prompt_tokens = 50, completion_tokens = 150 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", httpClient);

        using var provider = new DeepSeekLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("The answer is 42.", result.Content);
        Assert.Equal(200, result.TokensUsed);
        Assert.Contains("reasoning_content", result.Metadata.Keys);
        Assert.Equal("Let me think step by step. First, I need to consider...", result.Metadata["reasoning_content"].ToString());
    }

    [Fact]
    public async Task ShouldNotIncludeReasoningContent_WhenGenerateAsyncWithV3Response()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Standard response" } }
            },
            usage = new { total_tokens = 100, prompt_tokens = 40, completion_tokens = 60 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", httpClient);

        using var provider = new DeepSeekLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Standard response", result.Content);
        Assert.DoesNotContain("reasoning_content", result.Metadata.Keys);
    }

    [Theory]
    [InlineData("deepseek-chat")]
    [InlineData("deepseek-reasoner")]
    public async Task ShouldWork_WhenGenerateAsyncWithDifferentModels(string model)
    {
        // Arrange
        var configWithModel = LlmConfig.Create(model, TestApiKey);

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = $"Response from {model}" } }
            },
            usage = new { total_tokens = 50 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", httpClient);

        using var provider = new DeepSeekLlmProvider(configWithModel, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"Response from {model}", result.Content);
        Assert.Equal(model, result.Model);
    }

    [Fact]
    public async Task ShouldUseOverrideValues_WhenGenerateAsyncWithOverrideConfig()
    {
        // Arrange
        var overrideConfig = LlmConfig.Create("deepseek-reasoner", "override-key") with
        {
            Temperature = 0.2,
            MaxTokens = 2000
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Override response" } }
            },
            usage = new { total_tokens = 60 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", httpClient);

        using var provider = new DeepSeekLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, overrideConfig, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Override response", result.Content);
        Assert.Equal("deepseek-reasoner", result.Model);
    }

    [Fact]
    public async Task ShouldUseCustomUrl_WhenGenerateAsyncWithCustomBaseUrl()
    {
        // Arrange
        var configWithCustomUrl = LlmConfig.Create("deepseek-chat", TestApiKey) with
        {
            BaseUrl = new Uri("https://custom-deepseek-proxy.example.com")
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Custom URL response" } }
            },
            usage = new { total_tokens = 50 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", httpClient);

        using var provider = new DeepSeekLlmProvider(configWithCustomUrl, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Custom URL response", result.Content);
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithForbiddenError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Forbidden, "Forbidden");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", httpClient);

        using var provider = new DeepSeekLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("DeepSeek API error: Forbidden", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithInvalidJson()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "{ invalid json }");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", httpClient);

        using var provider = new DeepSeekLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("error", result.Metadata.Keys);
    }

    [Fact]
    public async Task ShouldHandleCancellation_WhenGenerateAsyncWithCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "{}");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", httpClient);

        using var provider = new DeepSeekLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, null, cts.Token);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("error", result.Metadata.Keys);
    }

    [Fact]
    public async Task ShouldIncludeUsageMetadata_WhenGenerateAsyncWithTokenBreakdown()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Usage test" } }
            },
            usage = new { total_tokens = 100, prompt_tokens = 40, completion_tokens = 60 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("DeepSeekLlmProvider", httpClient);

        using var provider = new DeepSeekLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Usage test", result.Content);
        Assert.Equal(100, result.TokensUsed);
        Assert.Equal("deepseek", result.Metadata["provider"].ToString());
        Assert.Equal(40, Convert.ToInt32(result.Metadata["prompt_tokens"]));
        Assert.Equal(60, Convert.ToInt32(result.Metadata["completion_tokens"]));
    }
}
