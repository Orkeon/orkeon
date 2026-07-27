using Orkeon.Domain.SharedKernel.ValueObjects;
using Polly;
using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class HuggingFaceLlmProviderTests
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<HuggingFaceLlmProvider> _logger;
    private readonly LlmConfig _config;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;

    public HuggingFaceLlmProviderTests()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<HuggingFaceLlmProvider>();
        _config = LlmConfig.Create("meta-llama/Llama-3.1-8B-Instruct", "hf_test-api-key");
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
    }

    [Fact]
    public void ShouldReturnHuggingFace_WhenName()
    {
        using var provider = new HuggingFaceLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        Assert.Equal("huggingface", provider.Name);
    }

    [Fact]
    public async Task ShouldReturnContent_WhenGenerateAsyncWithValidResponse()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "HuggingFace response" } }
            },
            usage = new { total_tokens = 80, prompt_tokens = 30, completion_tokens = 50 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("HuggingFaceLlmProvider", httpClient);

        using var provider = new HuggingFaceLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("HuggingFace response", result.Content);
        Assert.Equal(80, result.TokensUsed);
        Assert.Equal("meta-llama/Llama-3.1-8B-Instruct", result.Model);
    }

    [Fact]
    public async Task ShouldBuildCorrectEndpoint_ForServerlessApi()
    {
        // Arrange — no BaseUrl set, should use default: https://router.huggingface.co/v1/chat/completions
        // (LLM-01 / G-01: api-inference.huggingface.co was retired; Inference Providers now
        // serves the OpenAI-compatible surface from the router host.)
        var configNoBaseUrl = LlmConfig.Create("mistralai/Mistral-7B-Instruct-v0.3", "hf_test-key");

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Serverless response" } }
            },
            usage = new { total_tokens = 50 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("HuggingFaceLlmProvider", httpClient);

        using var provider = new HuggingFaceLlmProvider(configNoBaseUrl, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — verify the request was made (if it returned content, the endpoint was valid)
        Assert.Equal("Serverless response", result.Content);

        // Verify the captured request URL uses the default OpenAI-compatible base URL
        Assert.Single(handler.CapturedRequests);
        var requestUrl = handler.CapturedRequests[0].RequestUri!.ToString();
        Assert.StartsWith("https://router.huggingface.co/v1/chat/completions", requestUrl);
    }

    [Fact]
    public async Task ShouldBuildCorrectEndpoint_ForDedicatedEndpoint()
    {
        // Arrange — custom BaseUrl for dedicated Inference Endpoint
        var configWithEndpoint = LlmConfig.Create("meta-llama/Llama-3.1-8B-Instruct", "hf_test-key") with
        {
            BaseUrl = new Uri("https://my-endpoint.endpoints.huggingface.cloud")
        };

        var responseContent = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "Dedicated endpoint response" } }
            },
            usage = new { total_tokens = 60 }
        });

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("HuggingFaceLlmProvider", httpClient);

        using var provider = new HuggingFaceLlmProvider(configWithEndpoint, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Dedicated endpoint response", result.Content);

        // Verify the request URL uses the custom BaseUrl
        Assert.Single(handler.CapturedRequests);
        var requestUrl = handler.CapturedRequests[0].RequestUri!.ToString();
        Assert.StartsWith("https://my-endpoint.endpoints.huggingface.cloud/chat/completions", requestUrl);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithMissingApiKey()
    {
        // Arrange
        var configWithoutKey = LlmConfig.Create("meta-llama/Llama-3.1-8B-Instruct", null);
        using var provider = new HuggingFaceLlmProvider(configWithoutKey, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("HuggingFace API key is required", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithUnauthorized()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Unauthorized, "Unauthorized");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("HuggingFaceLlmProvider", httpClient);

        using var provider = new HuggingFaceLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("HuggingFace API error", result.Metadata["error"].ToString());
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
        _httpClientFactory.RegisterClient("HuggingFaceLlmProvider", httpClient);

        using var provider = new HuggingFaceLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("error", result.Metadata.Keys);
    }

    [Fact]
    public async Task ShouldStreamTokens_WhenStreamingIsRequested()
    {
        // Arrange — streaming uses SSE format: "data: {json}\n\ndata: [DONE]\n\n"
        var sseContent = "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n\n" +
                         "data: {\"choices\":[{\"delta\":{\"content\":\" World\"}}]}\n\n" +
                         "data: [DONE]\n\n";

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, sseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("HuggingFaceLlmProvider", httpClient);

        using var provider = new HuggingFaceLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var tokens = new List<string>();
        await foreach (var token in provider.GenerateStreamingAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken))
        {
            tokens.Add(token);
        }

        // Assert
        Assert.Equal(2, tokens.Count);
        Assert.Equal("Hello", tokens[0]);
        Assert.Equal(" World", tokens[1]);
    }

    [Fact]
    public async Task ShouldExtractUsageMetadata_WhenGenerateAsyncWithTokenBreakdown()
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
        _httpClientFactory.RegisterClient("HuggingFaceLlmProvider", httpClient);

        using var provider = new HuggingFaceLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Usage test", result.Content);
        Assert.Equal(100, result.TokensUsed);
        Assert.Equal("huggingface", result.Metadata["provider"].ToString());
        Assert.Equal(40, Convert.ToInt32(result.Metadata["prompt_tokens"]));
        Assert.Equal(60, Convert.ToInt32(result.Metadata["completion_tokens"]));
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithException()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithException(new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("HuggingFaceLlmProvider", httpClient);

        using var provider = new HuggingFaceLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

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
        var configWithSystem = LlmConfig.Create("meta-llama/Llama-3.1-8B-Instruct", "hf_test-key") with
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
        _httpClientFactory.RegisterClient("HuggingFaceLlmProvider", httpClient);

        using var provider = new HuggingFaceLlmProvider(configWithSystem, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("System response", result.Content);
    }

    [Theory]
    [InlineData("meta-llama/Llama-3.3-70B-Instruct")]
    [InlineData("mistralai/Mistral-7B-Instruct-v0.3")]
    [InlineData("microsoft/Phi-3-mini-4k-instruct")]
    public async Task ShouldWork_WhenGenerateAsyncWithDifferentModels(string model)
    {
        // Arrange
        var configWithModel = LlmConfig.Create(model, "hf_test-key");

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
        _httpClientFactory.RegisterClient("HuggingFaceLlmProvider", httpClient);

        using var provider = new HuggingFaceLlmProvider(configWithModel, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"Response from {model}", result.Content);
        Assert.Equal(model, result.Model);
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithInternalServerError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.InternalServerError, "Internal Server Error");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("HuggingFaceLlmProvider", httpClient);

        using var provider = new HuggingFaceLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("HuggingFace API error: InternalServerError", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldHandleCancellation_WhenGenerateAsyncWithCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "{}");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("HuggingFaceLlmProvider", httpClient);

        using var provider = new HuggingFaceLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, null, cts.Token);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("error", result.Metadata.Keys);
    }
}
