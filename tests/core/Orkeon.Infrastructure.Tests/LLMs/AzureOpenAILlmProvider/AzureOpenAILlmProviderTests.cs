using Orkeon.Domain.SharedKernel.ValueObjects;
using Polly;
using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class AzureOpenAILlmProviderTests
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<AzureOpenAILlmProvider> _logger;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;
    private readonly LlmConfig _config;

    public AzureOpenAILlmProviderTests()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<AzureOpenAILlmProvider>();
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
        _config = LlmConfig.Create(ModelGpt4, TestApiKey) with
        {
            BaseUrl = new Uri("https://myinstance.openai.azure.com")
        };
    }

    [Fact]
    public void ShouldReturnAzureOpenAI_WhenName()
    {
        using var provider = new AzureOpenAILlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        Assert.Equal("azure-openai", provider.Name);
    }

    [Fact]
    public async Task ShouldReturnContent_WhenGenerateAsyncWithValidResponse()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Azure response" } }
            },
            usage = new { total_tokens = 150 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        using var provider = new AzureOpenAILlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Azure response", result.Content);
        Assert.Equal(150, result.TokensUsed);
        Assert.Equal(ModelGpt4, result.Model);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithMissingApiKey()
    {
        // Arrange
        var configWithoutKey = LlmConfig.Create(ModelGpt4, null) with
        {
            BaseUrl = new Uri("https://myinstance.openai.azure.com")
        };
        using var provider = new AzureOpenAILlmProvider(configWithoutKey, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Azure OpenAI API key is required", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithMissingBaseUrl()
    {
        // Arrange
        var configWithoutUrl = LlmConfig.Create(ModelGpt4, TestApiKey);
        using var provider = new AzureOpenAILlmProvider(configWithoutUrl, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Azure OpenAI endpoint (BaseUrl) is required", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithHttpError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Unauthorized, "Unauthorized");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        using var provider = new AzureOpenAILlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Azure OpenAI API error", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithException()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithException(new HttpRequestException("Network error"));
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        using var provider = new AzureOpenAILlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Network error", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldUseCustomVersion_WhenGenerateAsyncWithCustomApiVersion()
    {
        // Arrange
        var configWithVersion = LlmConfig.Create(ModelGpt4, TestApiKey) with
        {
            BaseUrl = new Uri("https://myinstance.openai.azure.com"),
            CustomParameters = new Dictionary<string, object> { ["api_version"] = "2024-06-01" }
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Custom version response" } }
            },
            usage = new { total_tokens = 100 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        using var provider = new AzureOpenAILlmProvider(configWithVersion, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Custom version response", result.Content);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithRateLimitError()
    {
        // Arrange
        var errorResponse = JsonSerializer.Serialize(new
        {
            error = new { message = "Rate limit exceeded", code = "429" }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.TooManyRequests, errorResponse);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        using var provider = new AzureOpenAILlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("error", result.Metadata.Keys);
    }

    [Fact]
    public async Task ShouldUseOverrideValues_WhenGenerateAsyncWithOverrideConfig()
    {
        // Arrange
        var overrideConfig = LlmConfig.Create("gpt-35-turbo", "override-key") with
        {
            BaseUrl = new Uri("https://other-instance.openai.azure.com"),
            Temperature = 0.5,
            MaxTokens = 1000
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Override response" } }
            },
            usage = new { total_tokens = 75 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        using var provider = new AzureOpenAILlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, overrideConfig, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Override response", result.Content);
        Assert.Equal("gpt-35-turbo", result.Model);
    }

    [Theory]
    [InlineData(ModelGpt4)]
    [InlineData("gpt-35-turbo")]
    [InlineData("gpt-4-32k")]
    [InlineData("text-embedding-ada-002")]
    public async Task ShouldWork_WhenGenerateAsyncWithDifferentDeployments(string deployment)
    {
        // Arrange
        var configWithDeployment = LlmConfig.Create(deployment, TestApiKey) with
        {
            BaseUrl = new Uri("https://myinstance.openai.azure.com")
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = $"Response from {deployment}" } }
            },
            usage = new { total_tokens = 100 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        using var provider = new AzureOpenAILlmProvider(configWithDeployment, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"Response from {deployment}", result.Content);
        Assert.Equal(deployment, result.Model);
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithInternalServerError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.InternalServerError, "Internal Server Error");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        using var provider = new AzureOpenAILlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Azure OpenAI API error: InternalServerError", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithForbiddenError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Forbidden, "Forbidden");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        using var provider = new AzureOpenAILlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Azure OpenAI API error: Forbidden", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithInvalidJson()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "{ invalid json }");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        using var provider = new AzureOpenAILlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

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
        _httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        using var provider = new AzureOpenAILlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, null, cts.Token);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("error", result.Metadata.Keys);
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithServiceUnavailable()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.ServiceUnavailable, "Service Unavailable");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("AzureOpenAILlmProvider", httpClient);

        using var provider = new AzureOpenAILlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Azure OpenAI API error: ServiceUnavailable", result.Metadata["error"].ToString());
    }
}
