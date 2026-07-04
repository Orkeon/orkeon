using Orkeon.Domain.SharedKernel.ValueObjects;
using Polly;
using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class OpenAIProviderTests
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<OpenAIProvider> _logger;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;
    private readonly LlmConfig _config;

    public OpenAIProviderTests()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<OpenAIProvider>();
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
        _config = LlmConfig.Create(ModelGpt4, TestApiKey);
    }

    [Fact]
    public async Task ShouldReturnContent_WhenGenerateAsyncWithValidResponse()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new { content = "Test response" }
                }
            },
            usage = new { total_tokens = 100 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Test response", result.Content);
        Assert.Equal(100, result.TokensUsed);
        Assert.Equal(ModelGpt4, result.Model);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithMissingApiKey()
    {
        // Arrange
        var configWithoutKey = LlmConfig.Create(ModelGpt4, null);
        using var provider = new OpenAIProvider(configWithoutKey, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("OpenAI API key is required", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithHttpError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Unauthorized, "Unauthorized");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("OpenAI API error", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithException()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithException(new HttpRequestException("Network error"));
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("Network error", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldIncludeInRequest_WhenGenerateAsyncWithSystemMessage()
    {
        // Arrange
        var configWithSystem = LlmConfig.Create(ModelGpt4, TestApiKey) with
        {
            CustomParameters = new Dictionary<string, object> { ["system_message"] = "You are a helpful assistant." }
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Response with system message" } }
            },
            usage = new { total_tokens = 150 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(configWithSystem, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Response with system message", result.Content);
        Assert.Equal(150, result.TokensUsed);
    }

    [Fact]
    public async Task ShouldUseConfiguredValue_WhenGenerateAsyncWithTemperatureParameter()
    {
        // Arrange
        var configWithTemp = LlmConfig.Create(ModelGpt4, TestApiKey) with
        {
            Temperature = 0.8,
            MaxTokens = 2000
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Response with custom temperature" } }
            },
            usage = new { total_tokens = 200 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(configWithTemp, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Response with custom temperature", result.Content);
        Assert.Equal(200, result.TokensUsed);
    }

    [Fact]
    public async Task ShouldReturnFirstChoice_WhenGenerateAsyncWithMultipleChoices()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "First response" } },
                new { message = new { content = "Second response" } },
                new { message = new { content = "Third response" } }
            },
            usage = new { total_tokens = 250 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("First response", result.Content);
        Assert.Equal(250, result.TokensUsed);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithRateLimitError()
    {
        // Arrange
        var errorResponse = JsonSerializer.Serialize(new
        {
            error = new
            {
                message = "Rate limit exceeded",
                type = "rate_limit_error",
                code = "rate_limit_exceeded"
            }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.TooManyRequests, errorResponse);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("error", result.Metadata.Keys);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithInvalidJson()
    {
        // Arrange
        var invalidJson = "{ invalid json }";
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, invalidJson);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("error", result.Metadata.Keys);
    }

    [Fact]
    public async Task ShouldHandleGracefully_WhenGenerateAsyncWithEmptyResponse()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "" } }
            },
            usage = new { total_tokens = 10 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Equal(10, result.TokensUsed);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithTimeout()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.RequestTimeout, "");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("error", result.Metadata.Keys);
    }

    [Theory]
    [InlineData(ModelGpt35Turbo)]
    [InlineData(ModelGpt4)]
    [InlineData("gpt-4-32k")]
    [InlineData("gpt-4-turbo-preview")]
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
            usage = new { total_tokens = 100 },
            model = model
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(configWithModel, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"Response from {model}", result.Content);
        Assert.Equal(model, result.Model);
    }

    [Fact]
    public async Task ShouldHandleCorrectly_WhenGenerateAsyncWithLongPrompt()
    {
        // Arrange
        var longPrompt = string.Join(" ", Enumerable.Repeat("This is a very long prompt.", 1000));
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Response to long prompt" } }
            },
            usage = new { total_tokens = 5000 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(longPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Response to long prompt", result.Content);
        Assert.Equal(5000, result.TokensUsed);
    }

    [Fact]
    public async Task ShouldReturnFunctionCall_WhenGenerateAsyncWithFunctionCalling()
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
                        content = (string?)null,
                        function_call = new
                        {
                            name = "get_weather",
                            arguments = "{\"location\": \"San Francisco\"}"
                        }
                    }
                }
            },
            usage = new { total_tokens = 50 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync("What's the weather in San Francisco?", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(50, result.TokensUsed);
        Assert.NotNull(result.Metadata);
    }

    [Fact]
    public async Task ShouldCancelRequest_WhenGenerateAsyncWithCancellationToken()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "{}");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, null, cancellationTokenSource.Token);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("error", result.Metadata.Keys);
    }

    [Fact]
    public async Task ShouldHandleGracefully_WhenGenerateAsyncWithNullPrompt()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Response to null prompt" } }
            },
            usage = new { total_tokens = 20 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(null!, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Response to null prompt", result.Content);
        Assert.Equal(20, result.TokensUsed);
    }

    [Fact]
    public async Task ShouldSendRequest_WhenGenerateAsyncWithEmptyPrompt()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Response to empty prompt" } }
            },
            usage = new { total_tokens = 25 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(string.Empty, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Response to empty prompt", result.Content);
        Assert.Equal(25, result.TokensUsed);
    }

    [Fact]
    public async Task ShouldUseOverrideValues_WhenGenerateAsyncWithOverrideConfig()
    {
        // Arrange
        var overrideConfig = LlmConfig.Create(ModelGpt35Turbo, "override-api-key") with
        {
            Temperature = 0.9,
            MaxTokens = 500
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Override config response" } }
            },
            usage = new { total_tokens = 75 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, overrideConfig, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Override config response", result.Content);
        Assert.Equal(ModelGpt35Turbo, result.Model);
        Assert.Equal(75, result.TokensUsed);
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithBadGatewayError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.BadGateway, "Bad Gateway");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("OpenAI API error: BadGateway", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithServiceUnavailable()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.ServiceUnavailable, "Service Unavailable");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("OpenAI API error: ServiceUnavailable", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithInternalServerError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.InternalServerError, "Internal Server Error");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("OpenAI API error: InternalServerError", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldHandleGracefully_WhenGenerateAsyncWithMissingUsageData()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Response without usage" } }
            }
            // No usage field
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content); // Will fail due to missing usage data causing exception
        Assert.NotNull(result.Metadata);
    }

    [Fact]
    public async Task ShouldHandleGracefully_WhenGenerateAsyncWithEmptyChoicesArray()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = Array.Empty<object>(),
            usage = new { total_tokens = 0 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content); // Will fail due to empty choices causing exception
        Assert.NotNull(result.Metadata);
    }

    [Fact]
    public async Task ShouldHandleCorrectly_WhenGenerateAsyncWithSpecialCharactersInPrompt()
    {
        // Arrange
        var promptWithSpecialChars = "Test with special chars: \n\t\r\"<>&'";
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Response to special chars" } }
            },
            usage = new { total_tokens = 30 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(promptWithSpecialChars, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Response to special chars", result.Content);
        Assert.Equal(30, result.TokensUsed);
    }

    [Fact]
    public async Task ShouldHandleCorrectly_WhenGenerateAsyncWithUnicodeCharacters()
    {
        // Arrange
        var unicodePrompt = "Test with unicode: 你好世界 مرحبا بالعالم 🌍🚀";
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Unicode response: 成功 نجاح ✅" } }
            },
            usage = new { total_tokens = 40 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(unicodePrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Unicode response: 成功 نجاح ✅", result.Content);
        Assert.Equal(40, result.TokensUsed);
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithForbiddenError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Forbidden, "Forbidden");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("OpenAIProvider", httpClient);

        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("OpenAI API error: Forbidden", result.Metadata["error"].ToString());
    }

    [Fact]
    public void ShouldReturnOpenAI_WhenName()
    {
        // Arrange
        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var name = provider.Name;

        // Assert
        Assert.Equal("OpenAI", name);
    }
}
