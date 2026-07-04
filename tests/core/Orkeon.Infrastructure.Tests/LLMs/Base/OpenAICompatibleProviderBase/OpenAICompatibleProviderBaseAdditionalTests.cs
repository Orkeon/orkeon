using Orkeon.Domain.SharedKernel.ValueObjects;
using Polly;
using System.Net;
using System.Text.Json;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Base;

/// <summary>
/// Additional tests for OpenAICompatibleProviderBase covering Phase 1 gaps:
/// Streaming, cancellation, request payload building with various parameters,
/// error handling edge cases, and response parsing.
/// </summary>
public class OpenAICompatibleProviderBaseAdditionalTests
{
    private readonly TestDoubles.TestHttpClientFactory _httpClientFactory;
    private readonly TestDoubles.TestLogger<TestableOpenAICompatibleProvider> _logger;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;
    private readonly LlmConfig _config;

    public OpenAICompatibleProviderBaseAdditionalTests()
    {
        _httpClientFactory = new TestDoubles.TestHttpClientFactory();
        _logger = new TestDoubles.TestLogger<TestableOpenAICompatibleProvider>();
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
        _config = LlmConfig.Create(TestModelName, TestApiKey);
    }

    #region Streaming Tests

    [Fact]
    public async Task ShouldYieldNoTokens_WhenStreamingWithMissingApiKey()
    {
        // Arrange
        var configWithoutKey = LlmConfig.Create(TestModelName, null);
        using var provider = new TestableOpenAICompatibleProvider(configWithoutKey, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var tokens = new List<string>();
        await foreach (var token in provider.GenerateStreamingAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken))
        {
            tokens.Add(token);
        }

        // Assert
        Assert.Empty(tokens);
    }

    [Fact]
    public async Task ShouldYieldNoTokens_WhenStreamingReturnsHttpError()
    {
        // Arrange
        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.InternalServerError, "Server Error");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var tokens = new List<string>();
        await foreach (var token in provider.GenerateStreamingAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken))
        {
            tokens.Add(token);
        }

        // Assert
        Assert.Empty(tokens);
    }

    #endregion

    #region Request Payload Tests

    [Fact]
    public async Task ShouldIncludeTemperatureAndMaxTokens_WhenGenerateAsyncWithConfig()
    {
        // Arrange
        var configWithParams = LlmConfig.Create(TestModelName, TestApiKey) with
        {
            Temperature = 0.9f,
            MaxTokens = 2000
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "Response" } } },
            usage = new { total_tokens = 50 }
        });

        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(configWithParams, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Response", result.Content);
        // Verify the request was sent
        Assert.Single(handler.CapturedRequests);
        var request = handler.CapturedRequests[0];
        var body = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"temperature\"", body);
        Assert.Contains("\"max_tokens\"", body);
    }

    [Fact]
    public async Task ShouldIncludeTopP_WhenTopPIsNotDefault()
    {
        // Arrange
        var configWithTopP = LlmConfig.Create(TestModelName, TestApiKey) with
        {
            TopP = 0.5
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "TopP response" } } },
            usage = new { total_tokens = 30 }
        });

        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(configWithTopP, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("TopP response", result.Content);
        var request = handler.CapturedRequests[0];
        var body = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"top_p\"", body);
    }

    [Fact]
    public async Task ShouldNotIncludeTopP_WhenTopPIsDefault()
    {
        // Arrange - default TopP is 1.0
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "Default TopP" } } },
            usage = new { total_tokens = 20 }
        });

        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Default TopP", result.Content);
        var request = handler.CapturedRequests[0];
        var body = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("\"top_p\"", body);
    }

    [Fact]
    public async Task ShouldIncludeStopSequences_WhenProvided()
    {
        // Arrange
        var configWithStop = LlmConfig.Create(TestModelName, TestApiKey) with
        {
            StopSequences = ["STOP", "END"]
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "Stopped" } } },
            usage = new { total_tokens = 10 }
        });

        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(configWithStop, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Stopped", result.Content);
        var request = handler.CapturedRequests[0];
        var body = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"stop\"", body);
        Assert.Contains("STOP", body);
        Assert.Contains("END", body);
    }

    [Fact]
    public async Task ShouldNotIncludeStopSequences_WhenEmpty()
    {
        // Arrange - no stop sequences
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "No stop" } } },
            usage = new { total_tokens = 10 }
        });

        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("No stop", result.Content);
        var request = handler.CapturedRequests[0];
        var body = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("\"stop\"", body);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task ShouldReturnError_WhenCancelledDuringGeneration()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // Cancel immediately

        using var handler = new TestDoubles.TestHttpMessageHandler(async (request) =>
        {
            await Task.Delay(5000); // Long delay that should be cancelled
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: cts.Token);

        // Assert - Should get an error response (TaskCanceledException caught by error handler)
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
    }

    #endregion

    #region Error Response Parsing Tests

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ShouldReturnApiError_WhenDifferentHttpStatusCodes(HttpStatusCode statusCode)
    {
        // Arrange
        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(statusCode, $"Error: {statusCode}");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("error", result.Metadata.Keys);
        Assert.Contains("Test API error", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldHandleNetworkException_Gracefully()
    {
        // Arrange
        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithException(
            new HttpRequestException("DNS resolution failed"));
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("API call failed", result.Metadata!["error"].ToString());
    }

    [Fact]
    public async Task ShouldHandleTimeoutException_Gracefully()
    {
        // Arrange
        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithException(
            new TaskCanceledException("The operation was cancelled"));
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("error_type", result.Metadata.Keys);
    }

    #endregion

    #region Endpoint Building Tests

    [Fact]
    public async Task ShouldUseCustomBaseUrl_WhenConfigured()
    {
        // Arrange
        var configWithUrl = LlmConfig.Create(TestModelName, TestApiKey) with
        {
            BaseUrl = new Uri("https://custom.api.com/v2/")
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "Custom endpoint" } } },
            usage = new { total_tokens = 10 }
        });

        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(configWithUrl, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync("Test", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(handler.CapturedRequests);
        var requestUri = handler.CapturedRequests[0].RequestUri!.ToString();
        // Trailing slash should be trimmed and path appended
        Assert.Equal("https://custom.api.com/v2/chat/completions", requestUri);
    }

    #endregion

    #region Override Config Tests

    [Fact]
    public async Task ShouldUseOverrideConfig_WhenPassedToGenerateAsync()
    {
        // Arrange
        var overrideConfig = LlmConfig.Create("override-model", "override-key");

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "Override response" } } },
            usage = new { total_tokens = 25 }
        });

        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, overrideConfig, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Override response", result.Content);
        Assert.Equal("override-model", result.Model);
        // Verify the request body uses the override model
        var request = handler.CapturedRequests[0];
        var body = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("override-model", body);
    }

    [Fact]
    public async Task ShouldFallbackToDefaultConfig_WhenOverrideIsNull()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "Default config" } } },
            usage = new { total_tokens = 15 }
        });

        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, config: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Default config", result.Content);
        Assert.Equal(TestModelName, result.Model);
    }

    #endregion

    #region Metadata Tests

    [Fact]
    public async Task ShouldIncludeErrorTypeInMetadata_WhenExceptionOccurs()
    {
        // Arrange
        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithException(
            new HttpRequestException("Connection reset"));
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync("Test", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("error_type", result.Metadata!.Keys);
    }

    [Fact]
    public async Task ShouldIncludeTokenCount_WhenResponseIsValid()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "Token test" } } },
            usage = new { total_tokens = 42 }
        });

        using var handler = TestDoubles.TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TestableOpenAICompatibleProvider", httpClient);

        using var provider = new TestableOpenAICompatibleProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync("Test", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(42, result.TokensUsed);
    }

    #endregion
}
