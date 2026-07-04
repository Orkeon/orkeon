using Microsoft.Extensions.Logging;
using Polly;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Base;
using System.Net;
using System.Text.Json;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Base;

/// <summary>
/// Test-only concrete implementation of <see cref="OpenAICompatibleProviderBase"/>.
/// </summary>
public partial class TestableOpenAICompatibleProvider : OpenAICompatibleProviderBase
{
    public override string Name => "TestProvider";
    protected override Uri DefaultBaseUrl => new("https://api.test.com/v1");
    protected override string DefaultModel => TestModelName;
    protected override string ProviderDisplayName => "Test";

    public TestableOpenAICompatibleProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<TestableOpenAICompatibleProvider>? logger = null)
        : base(config, httpClientFactory, logger)
    {
    }

    public TestableOpenAICompatibleProvider(
        LlmConfig config,
        IHttpClientFactory httpClientFactory,
        IAsyncPolicy<HttpResponseMessage>? resiliencePolicy,
        ILogger<TestableOpenAICompatibleProvider>? logger = null)
        : base(config, httpClientFactory, resiliencePolicy, logger)
    {
    }
}

public class OpenAICompatibleProviderBaseTests
{
    private readonly TestDoubles.TestHttpClientFactory _httpClientFactory;
    private readonly TestDoubles.TestLogger<TestableOpenAICompatibleProvider> _logger;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;
    private readonly LlmConfig _config;

    public OpenAICompatibleProviderBaseTests()
    {
        _httpClientFactory = new TestDoubles.TestHttpClientFactory();
        _logger = new TestDoubles.TestLogger<TestableOpenAICompatibleProvider>();
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
        _config = LlmConfig.Create(TestModelName, TestApiKey);
    }

    [Fact]
    public void ShouldReturnTestProvider_WhenName()
    {
        // Arrange
        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act & Assert
        Assert.Equal("TestProvider", provider.Name);
    }

    [Fact]
    public async Task ShouldReturnContent_WhenGenerateAsyncWithValidResponse()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Test response" } }
            },
            usage = new { total_tokens = 100 }
        });

        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Test response", result.Content);
        Assert.Equal(100, result.TokensUsed);
        Assert.Equal(TestModelName, result.Model);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithMissingApiKey()
    {
        // Arrange
        var configWithoutKey = LlmConfig.Create(TestModelName, null);
        using var provider = new TestableOpenAICompatibleProvider(configWithoutKey, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("Test API key is required", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithHttpError()
    {
        // Arrange
        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Forbidden, "Forbidden");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("Test API error: Forbidden", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithException()
    {
        // Arrange
        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithException(new HttpRequestException("Network error"));
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        // Harmonized with OllamaLlmProvider: the response surfaces the sanitized message
        // (no secret to redact here) while the real exception type is preserved.
        Assert.Contains("Test API call failed: Network error", result.Metadata["error"].ToString());
        Assert.Equal("HttpRequestException", result.Metadata["error_type"]);
    }

    [Fact]
    public async Task ShouldIncludeSystemMessage_WhenGenerateAsyncWithCustomParameters()
    {
        // Arrange
        var configWithSystem = LlmConfig.Create(TestModelName, TestApiKey) with
        {
            CustomParameters = new Dictionary<string, object>
            {
                ["system_message"] = "You are a helpful assistant."
            }
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Response with system message" } }
            },
            usage = new { total_tokens = 150 }
        });

        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(configWithSystem, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Response with system message", result.Content);
        Assert.Equal(150, result.TokensUsed);
    }

    [Fact]
    public async Task ShouldIncludeSystemMessage_WhenGenerateAsyncWithConfigSystemMessage()
    {
        // Arrange
        var configWithSystem = LlmConfig.Create(TestModelName, TestApiKey) with
        {
            SystemMessage = "You are an expert."
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Expert response" } }
            },
            usage = new { total_tokens = 120 }
        });

        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(configWithSystem, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Expert response", result.Content);
        Assert.Equal(120, result.TokensUsed);
    }

    [Fact]
    public async Task ShouldUseCustomBaseUrl_WhenGenerateAsyncWithBaseUrlInConfig()
    {
        // Arrange
        var configWithCustomUrl = LlmConfig.Create(TestModelName, TestApiKey) with
        {
            BaseUrl = new Uri("https://custom-proxy.example.com/v1")
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Custom URL response" } }
            },
            usage = new { total_tokens = 50 }
        });

        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(configWithCustomUrl, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Custom URL response", result.Content);
    }

    [Fact]
    public async Task ShouldUseDefaultModel_WhenGenerateAsyncWithNullModelInConfig()
    {
        // Arrange
#pragma warning disable CS0618
        var effectiveConfig = LlmConfig.Default() with { ApiKey = TestApiKey };
#pragma warning restore CS0618

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Default model response" } }
            },
            usage = new { total_tokens = 60 }
        });

        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(effectiveConfig, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Default model response", result.Content);
        Assert.Equal(60, result.TokensUsed);
    }

    [Fact]
    public async Task ShouldUseDefaultBaseUrl_WhenBuildEndpointWithNoBaseUrlInConfig()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Response" } }
            },
            usage = new { total_tokens = 10 }
        });

        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert -- verify the request was made to the default base URL
        Assert.Single(handler.CapturedRequests);
        var requestUri = handler.CapturedRequests[0].RequestUri!.ToString();
        Assert.Equal("https://api.test.com/v1/chat/completions", requestUri);
    }

    [Fact]
    public async Task ShouldIncludeProviderInMetadata_WhenGenerateAsyncWithValidResponse()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Metadata test" } }
            },
            usage = new { total_tokens = 30 }
        });

        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("TestProvider", result.Metadata["provider"].ToString());
    }
}
