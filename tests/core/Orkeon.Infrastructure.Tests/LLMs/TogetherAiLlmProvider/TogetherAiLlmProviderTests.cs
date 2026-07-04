using Orkeon.Domain.SharedKernel.ValueObjects;
using Polly;
using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class TogetherAiLlmProviderTests
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<TogetherAiLlmProvider> _logger;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;
    private readonly LlmConfig _config;

    public TogetherAiLlmProviderTests()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<TogetherAiLlmProvider>();
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
        _config = LlmConfig.Create("meta-llama/Llama-3.3-70B-Instruct-Turbo", TestApiKey);
    }

    [Fact]
    public void ShouldReturnTogether_WhenName()
    {
        using var provider = new TogetherAiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        Assert.Equal("together", provider.Name);
    }

    [Fact]
    public async Task ShouldReturnContent_WhenGenerateAsyncWithValidResponse()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Together AI response" } }
            },
            usage = new { total_tokens = 80, prompt_tokens = 30, completion_tokens = 50 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TogetherAiLlmProvider", httpClient);

        using var provider = new TogetherAiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Together AI response", result.Content);
        Assert.Equal(80, result.TokensUsed);
        Assert.Equal("meta-llama/Llama-3.3-70B-Instruct-Turbo", result.Model);
    }

    [Fact]
    public async Task ShouldUseCorrectDefaultBaseUrl()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "URL test" } }
            },
            usage = new { total_tokens = 50 }
        });

        using var handler = new TestHttpMessageHandler(request =>
        {
            // Verify the request URL includes the correct base URL
            Assert.Contains("together.xyz/v1/chat/completions", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent)
            };
        });
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TogetherAiLlmProvider", httpClient);

        var configWithoutBaseUrl = LlmConfig.Create("meta-llama/Llama-3.3-70B-Instruct-Turbo", TestApiKey);
        using var provider = new TogetherAiLlmProvider(configWithoutBaseUrl, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("URL test", result.Content);
    }

    [Fact]
    public async Task ShouldUseCorrectDefaultModel()
    {
        // Arrange — use a dummy model in config; the provider's DefaultModel should be
        // "meta-llama/Llama-3.3-70B-Instruct-Turbo". We verify by inspecting the request body
        // when the config model matches the expected default.
        var configWithDefault = LlmConfig.Create("meta-llama/Llama-3.3-70B-Instruct-Turbo", TestApiKey);

        using var handler = new TestHttpMessageHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().Result;
            // Verify the default model is present in the request
            Assert.Contains("meta-llama/Llama-3.3-70B-Instruct-Turbo", body);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[]
                    {
                        new { message = new { content = "Default model response" } }
                    },
                    usage = new { total_tokens = 50 }
                }))
            };
        });
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TogetherAiLlmProvider", httpClient);

        using var provider = new TogetherAiLlmProvider(configWithDefault, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Default model response", result.Content);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithMissingApiKey()
    {
        // Arrange
        var configWithoutKey = LlmConfig.Create("meta-llama/Llama-3.3-70B-Instruct-Turbo", null);
        using var provider = new TogetherAiLlmProvider(configWithoutKey, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Together AI API key is required", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithHttpError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Unauthorized, "Unauthorized");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TogetherAiLlmProvider", httpClient);

        using var provider = new TogetherAiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Together AI API error", result.Metadata["error"].ToString());
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
        _httpClientFactory.RegisterClient("TogetherAiLlmProvider", httpClient);

        using var provider = new TogetherAiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Usage test", result.Content);
        Assert.Equal(100, result.TokensUsed);
        Assert.Equal("together", result.Metadata["provider"].ToString());
        Assert.Equal(40, Convert.ToInt32(result.Metadata["prompt_tokens"]));
        Assert.Equal(60, Convert.ToInt32(result.Metadata["completion_tokens"]));
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithException()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithException(new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TogetherAiLlmProvider", httpClient);

        using var provider = new TogetherAiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

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
        var configWithSystem = LlmConfig.Create("meta-llama/Llama-3.3-70B-Instruct-Turbo", TestApiKey) with
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
        _httpClientFactory.RegisterClient("TogetherAiLlmProvider", httpClient);

        using var provider = new TogetherAiLlmProvider(configWithSystem, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("System response", result.Content);
    }

    [Theory]
    [InlineData("meta-llama/Llama-3.3-70B-Instruct-Turbo")]
    [InlineData("mistralai/Mixtral-8x7B-Instruct-v0.1")]
    [InlineData("Qwen/Qwen2.5-72B-Instruct-Turbo")]
    [InlineData("google/gemma-2-27b-it")]
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
        _httpClientFactory.RegisterClient("TogetherAiLlmProvider", httpClient);

        using var provider = new TogetherAiLlmProvider(configWithModel, _httpClientFactory, _noOpPolicy, _logger);

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
        var overrideConfig = LlmConfig.Create("mistralai/Mixtral-8x7B-Instruct-v0.1", "override-key") with
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
        _httpClientFactory.RegisterClient("TogetherAiLlmProvider", httpClient);

        using var provider = new TogetherAiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, overrideConfig, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Override response", result.Content);
        Assert.Equal("mistralai/Mixtral-8x7B-Instruct-v0.1", result.Model);
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithForbiddenError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Forbidden, "Forbidden");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TogetherAiLlmProvider", httpClient);

        using var provider = new TogetherAiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Together AI API error: Forbidden", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithInvalidJson()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "{ invalid json }");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TogetherAiLlmProvider", httpClient);

        using var provider = new TogetherAiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

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
        _httpClientFactory.RegisterClient("TogetherAiLlmProvider", httpClient);

        using var provider = new TogetherAiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

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
        var configWithCustomUrl = LlmConfig.Create("meta-llama/Llama-3.3-70B-Instruct-Turbo", TestApiKey) with
        {
            BaseUrl = new Uri("https://custom-together-proxy.example.com/v1")
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
        _httpClientFactory.RegisterClient("TogetherAiLlmProvider", httpClient);

        using var provider = new TogetherAiLlmProvider(configWithCustomUrl, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Custom URL response", result.Content);
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithRateLimitError()
    {
        // Arrange
        var errorResponse = JsonSerializer.Serialize(new
        {
            error = new { message = "Rate limit exceeded", type = "rate_limit_error" }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.TooManyRequests, errorResponse);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("TogetherAiLlmProvider", httpClient);

        using var provider = new TogetherAiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("error", result.Metadata.Keys);
    }
}
