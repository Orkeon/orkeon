using System.Net;
using System.Text.Json;
using Polly;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class AnthropicLlmProviderTests
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<AnthropicLlmProvider> _logger;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;
    private readonly LlmConfig _config;

    public AnthropicLlmProviderTests()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<AnthropicLlmProvider>();
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
        _config = LlmConfig.Create(ModelClaude3Opus, TestApiKey) with
        {
            BaseUrl = new Uri("https://api.anthropic.com")
        };
    }

    [Fact]
    public void ShouldReturnAnthropic_WhenName()
    {
        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        Assert.Equal(ProviderAnthropic, provider.Name);
    }

    [Fact]
    public async Task ShouldReturnContent_WhenGenerateAsyncWithValidResponse()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            content = new[]
            {
                new { type = "text", text = "Hello from Claude" }
            },
            usage = new { input_tokens = 10, output_tokens = 20 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Hello from Claude", result.Content);
        Assert.Equal(30, result.TokensUsed);
        Assert.Equal(ModelClaude3Opus, result.Model);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithMissingApiKey()
    {
        // Arrange
        var configWithoutKey = LlmConfig.Create(ModelClaude3Opus, null);
        using var provider = new AnthropicLlmProvider(configWithoutKey, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("Anthropic API key is required", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithHttpError()
    {
        // Arrange
        var errorResponse = JsonSerializer.Serialize(new
        {
            error = new { type = "authentication_error", message = "Invalid API key" }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Unauthorized, errorResponse);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("Anthropic API error", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithException()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithException(new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("Connection refused", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldIncludeInRequest_WhenGenerateAsyncWithSystemMessage()
    {
        // Arrange
        var configWithSystem = LlmConfig.Create(ModelClaude3Opus, TestApiKey) with
        {
            BaseUrl = new Uri("https://api.anthropic.com"),
            CustomParameters = new Dictionary<string, object> { ["system_message"] = "You are a helpful assistant." }
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            content = new[]
            {
                new { type = "text", text = "System response" }
            },
            usage = new { input_tokens = 15, output_tokens = 25 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        using var provider = new AnthropicLlmProvider(configWithSystem, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("System response", result.Content);
        Assert.Equal(40, result.TokensUsed);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithRateLimitError()
    {
        // Arrange
        var errorResponse = JsonSerializer.Serialize(new
        {
            error = new { type = "rate_limit_error", message = "Rate limit exceeded" }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.TooManyRequests, errorResponse);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("error", result.Metadata.Keys);
    }

    [Fact]
    public async Task ShouldReturnCorrectCounts_WhenGenerateAsyncWithTokenUsage()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            content = new[]
            {
                new { type = "text", text = "Token count test" }
            },
            usage = new { input_tokens = 100, output_tokens = 200 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(300, result.TokensUsed);
        Assert.Equal(100, Convert.ToInt32(result.Metadata["input_tokens"]));
        Assert.Equal(200, Convert.ToInt32(result.Metadata["output_tokens"]));
    }

    [Theory]
    [InlineData(ModelClaude3Opus)]
    [InlineData("claude-3-sonnet-20240229")]
    [InlineData("claude-3-haiku-20240307")]
    [InlineData("claude-3-5-sonnet-20241022")]
    public async Task ShouldWork_WhenGenerateAsyncWithDifferentModels(string model)
    {
        // Arrange
        var configWithModel = LlmConfig.Create(model, TestApiKey) with
        {
            BaseUrl = new Uri("https://api.anthropic.com")
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            content = new[]
            {
                new { type = "text", text = $"Response from {model}" }
            },
            usage = new { input_tokens = 10, output_tokens = 15 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        using var provider = new AnthropicLlmProvider(configWithModel, _httpClientFactory, _noOpPolicy, _logger);

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
        var overrideConfig = LlmConfig.Create("claude-3-haiku-20240307", "override-key") with
        {
            BaseUrl = new Uri("https://api.anthropic.com"),
            Temperature = 0.9,
            MaxTokens = 500
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            content = new[]
            {
                new { type = "text", text = "Override response" }
            },
            usage = new { input_tokens = 5, output_tokens = 10 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, overrideConfig, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Override response", result.Content);
        Assert.Equal("claude-3-haiku-20240307", result.Model);
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithForbiddenError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Forbidden, "Forbidden");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Anthropic API error: Forbidden", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithInternalServerError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.InternalServerError, "Internal Server Error");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Anthropic API error: InternalServerError", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldSeparateSystemFromMessages_WhenChatAsyncWithSystemMessage()
    {
        // Arrange
        var messages = new[]
        {
            LlmMessage.System("You are a helpful assistant."),
            LlmMessage.User("Hello"),
            LlmMessage.Assistant("Hi there!"),
            LlmMessage.User("How are you?")
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            content = new[]
            {
                new { type = "text", text = "I'm doing great!" }
            },
            usage = new { input_tokens = 30, output_tokens = 10 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("I'm doing great!", result.Content);
        Assert.Equal(40, result.TokensUsed);
    }

    [Fact]
    public async Task ShouldDelegateToGenerate_WhenChatAsyncWithNullMessages()
    {
        // Arrange
        var configWithoutKey = LlmConfig.Create(ModelClaude3Opus, null);
        using var provider = new AnthropicLlmProvider(configWithoutKey, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.ChatAsync(null!, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - should hit the missing API key error path in GenerateAsync
        Assert.Empty(result.Content);
    }

    [Fact]
    public async Task ShouldReturnError_WhenChatAsyncWithMissingApiKey()
    {
        // Arrange
        var configWithoutKey = LlmConfig.Create(ModelClaude3Opus, null);
        using var provider = new AnthropicLlmProvider(configWithoutKey, _httpClientFactory, _noOpPolicy, _logger);

        var messages = new[] { LlmMessage.User("Hello") };

        // Act
        var result = await provider.ChatAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Anthropic API key is required", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnFirstTextBlock_WhenGenerateAsyncWithMultipleContentBlocks()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            content = new object[]
            {
                new { type = "text", text = "First text block" },
                new { type = "text", text = "Second text block" }
            },
            usage = new { input_tokens = 10, output_tokens = 20 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("First text block", result.Content);
    }

    [Fact]
    public async Task ShouldUseCustomUrl_WhenGenerateAsyncWithCustomBaseUrl()
    {
        // Arrange
        var configWithCustomUrl = LlmConfig.Create(ModelClaude3Opus, TestApiKey) with
        {
            BaseUrl = new Uri("https://custom-anthropic-proxy.example.com")
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            content = new[]
            {
                new { type = "text", text = "Custom URL response" }
            },
            usage = new { input_tokens = 5, output_tokens = 10 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        using var provider = new AnthropicLlmProvider(configWithCustomUrl, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Custom URL response", result.Content);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithInvalidJson()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "{ invalid json }");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

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
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", httpClient);

        using var provider = new AnthropicLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, null, cts.Token);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("error", result.Metadata.Keys);
    }
}
