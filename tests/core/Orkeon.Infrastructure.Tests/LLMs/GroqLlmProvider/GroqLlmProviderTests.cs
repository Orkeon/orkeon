using Orkeon.Domain.SharedKernel.ValueObjects;
using Polly;
using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class GroqLlmProviderTests
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<GroqLlmProvider> _logger;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;
    private readonly LlmConfig _config;

    public GroqLlmProviderTests()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<GroqLlmProvider>();
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
        _config = LlmConfig.Create("llama-3.3-70b-versatile", TestApiKey);
    }

    [Fact]
    public void ShouldReturnGroq_WhenName()
    {
        using var provider = new GroqLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        Assert.Equal("groq", provider.Name);
    }

    [Fact]
    public async Task ShouldReturnContent_WhenGenerateAsyncWithValidResponse()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Groq response" } }
            },
            usage = new { total_tokens = 80, prompt_tokens = 30, completion_tokens = 50 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("GroqLlmProvider", httpClient);

        using var provider = new GroqLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Groq response", result.Content);
        Assert.Equal(80, result.TokensUsed);
        Assert.Equal("llama-3.3-70b-versatile", result.Model);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithMissingApiKey()
    {
        // Arrange
        var configWithoutKey = LlmConfig.Create("llama-3.3-70b-versatile", null);
        using var provider = new GroqLlmProvider(configWithoutKey, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Groq API key is required", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithHttpError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Unauthorized, "Unauthorized");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("GroqLlmProvider", httpClient);

        using var provider = new GroqLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Groq API error", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithException()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithException(new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("GroqLlmProvider", httpClient);

        using var provider = new GroqLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Connection refused", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldIncludeInRequest_WhenGenerateAsyncWithSystemMessage()
    {
        // Arrange
        var configWithSystem = LlmConfig.Create("llama-3.3-70b-versatile", TestApiKey) with
        {
            CustomParameters = new Dictionary<string, object> { ["system_message"] = "You are a helpful assistant." }
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "System response" } }
            },
            usage = new { total_tokens = 100 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("GroqLlmProvider", httpClient);

        using var provider = new GroqLlmProvider(configWithSystem, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("System response", result.Content);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithRateLimitError()
    {
        // Arrange
        var errorResponse = JsonSerializer.Serialize(new
        {
            error = new { message = "Rate limit exceeded", type = "rate_limit_error" }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.TooManyRequests, errorResponse);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("GroqLlmProvider", httpClient);

        using var provider = new GroqLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("error", result.Metadata.Keys);
    }

    [Theory]
    [InlineData("llama-3.3-70b-versatile")]
    [InlineData("mixtral-8x7b-32768")]
    [InlineData("llama-3.1-8b-instant")]
    [InlineData("gemma2-9b-it")]
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
        _httpClientFactory.RegisterClient("GroqLlmProvider", httpClient);

        using var provider = new GroqLlmProvider(configWithModel, _httpClientFactory, _noOpPolicy, _logger);

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
        var overrideConfig = LlmConfig.Create("mixtral-8x7b-32768", "override-key") with
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
        _httpClientFactory.RegisterClient("GroqLlmProvider", httpClient);

        using var provider = new GroqLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, overrideConfig, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Override response", result.Content);
        Assert.Equal("mixtral-8x7b-32768", result.Model);
    }

    [Fact]
    public async Task ShouldIncludeInResponse_WhenGenerateAsyncWithGroqTimingMetadata()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Timing test" } }
            },
            usage = new
            {
                total_tokens = 100,
                prompt_tokens = 40,
                completion_tokens = 60,
                queue_time = 0.01,
                prompt_time = 0.05,
                completion_time = 0.15,
                total_time = 0.21
            }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("GroqLlmProvider", httpClient);

        using var provider = new GroqLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Timing test", result.Content);
        Assert.Equal(100, result.TokensUsed);
        Assert.Equal("groq", result.Metadata["provider"].ToString());
        Assert.Equal(40, Convert.ToInt32(result.Metadata["prompt_tokens"]));
        Assert.Equal(60, Convert.ToInt32(result.Metadata["completion_tokens"]));
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithForbiddenError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Forbidden, "Forbidden");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("GroqLlmProvider", httpClient);

        using var provider = new GroqLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Groq API error: Forbidden", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithInternalServerError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.InternalServerError, "Internal Server Error");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("GroqLlmProvider", httpClient);

        using var provider = new GroqLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Groq API error: InternalServerError", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithInvalidJson()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "{ invalid json }");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("GroqLlmProvider", httpClient);

        using var provider = new GroqLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

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
        _httpClientFactory.RegisterClient("GroqLlmProvider", httpClient);

        using var provider = new GroqLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, null, cts.Token);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("error", result.Metadata.Keys);
    }

    [Fact]
    public async Task ShouldUseCustomUrl_WhenGenerateAsyncWithCustomBaseUrl()
    {
        // Arrange
        var configWithCustomUrl = LlmConfig.Create("llama-3.3-70b-versatile", TestApiKey) with
        {
            BaseUrl = new Uri("https://custom-groq-proxy.example.com/openai/v1")
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
        _httpClientFactory.RegisterClient("GroqLlmProvider", httpClient);

        using var provider = new GroqLlmProvider(configWithCustomUrl, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Custom URL response", result.Content);
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithServiceUnavailable()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.ServiceUnavailable, "Service Unavailable");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("GroqLlmProvider", httpClient);

        using var provider = new GroqLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Groq API error: ServiceUnavailable", result.Metadata["error"].ToString());
    }
}
