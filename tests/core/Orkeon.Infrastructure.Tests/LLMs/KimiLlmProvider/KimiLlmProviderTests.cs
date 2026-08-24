using Orkeon.Domain.SharedKernel.ValueObjects;
using Polly;
using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class KimiLlmProviderTests
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<KimiLlmProvider> _logger;
    private readonly LlmConfig _config;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;

    public KimiLlmProviderTests()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<KimiLlmProvider>();
        _config = LlmConfig.Create("moonshot-v1-8k", TestApiKey);
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
    }

    [Fact]
    public void ShouldReturnKimi_WhenName()
    {
        using var provider = new KimiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        Assert.Equal("kimi", provider.Name);
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

        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, responseContent);
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("KimiLlmProvider", httpClient);

        using var provider = new KimiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Test response", result.Content);
        Assert.Equal(100, result.TokensUsed);
        Assert.Equal("moonshot-v1-8k", result.Model);
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithMissingApiKey()
    {
        // Arrange
        var configWithoutKey = LlmConfig.Create("moonshot-v1-8k", null);
        using var provider = new KimiLlmProvider(configWithoutKey, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Kimi API key is required", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithHttpError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Unauthorized, "Unauthorized");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("KimiLlmProvider", httpClient);

        using var provider = new KimiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Kimi API error", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithException()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithException(new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("KimiLlmProvider", httpClient);

        using var provider = new KimiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Connection refused", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldUseOverrideValues_WhenGenerateAsyncWithOverrideConfig()
    {
        // Arrange
        var overrideConfig = LlmConfig.Create("moonshot-v1-32k", "override-key") with
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
        _httpClientFactory.RegisterClient("KimiLlmProvider", httpClient);

        using var provider = new KimiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, overrideConfig, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Override response", result.Content);
        Assert.Equal("moonshot-v1-32k", result.Model);
    }

    [Theory]
    [InlineData("moonshot-v1-8k")]
    [InlineData("moonshot-v1-32k")]
    [InlineData("moonshot-v1-128k")]
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
        _httpClientFactory.RegisterClient("KimiLlmProvider", httpClient);

        using var provider = new KimiLlmProvider(configWithModel, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"Response from {model}", result.Content);
        Assert.Equal(model, result.Model);
    }

    [Fact]
    public async Task ShouldUseCustomUrl_WhenGenerateAsyncWithCustomBaseUrl()
    {
        // Arrange
        var configWithCustomUrl = LlmConfig.Create("moonshot-v1-8k", TestApiKey) with
        {
            BaseUrl = new Uri("https://custom-moonshot-proxy.example.com/v1")
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
        _httpClientFactory.RegisterClient("KimiLlmProvider", httpClient);

        using var provider = new KimiLlmProvider(configWithCustomUrl, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Custom URL response", result.Content);
    }

    [Fact]
    public async Task ShouldIncludeInRequest_WhenGenerateAsyncWithSystemMessage()
    {
        // Arrange
        var configWithSystem = LlmConfig.Create("moonshot-v1-8k", TestApiKey) with
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
        _httpClientFactory.RegisterClient("KimiLlmProvider", httpClient);

        using var provider = new KimiLlmProvider(configWithSystem, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("System response", result.Content);
    }

    [Fact]
    public async Task ShouldReturnErrorResponse_WhenGenerateAsyncWithForbiddenError()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.Forbidden, "Forbidden");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("KimiLlmProvider", httpClient);

        using var provider = new KimiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("Kimi API error: Forbidden", result.Metadata["error"].ToString());
    }

    [Fact]
    public async Task ShouldReturnError_WhenGenerateAsyncWithInvalidJson()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "{ invalid json }");
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("KimiLlmProvider", httpClient);

        using var provider = new KimiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

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
        _httpClientFactory.RegisterClient("KimiLlmProvider", httpClient);

        using var provider = new KimiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Act
        var result = await provider.GenerateAsync(TestPrompt, null, cts.Token);

        // Assert
        Assert.Empty(result.Content);
        Assert.Contains("error", result.Metadata.Keys);
    }
}

/// <summary>
/// Moonshot's hard temperature constraint (400 "invalid temperature: only 1 is allowed
/// for this model") self-heals: the request is re-sent once with the mandated value.
/// Which models mandate it is decided server-side, so the constraint is read from the
/// API's own rejection — no model list to drift.
/// </summary>
public sealed class KimiTemperatureConstraintTests
{
    private const string RejectionBody =
        """{"error":{"message":"invalid temperature: only 1 is allowed for this model","type":"invalid_request_error"}}""";

    private static (KimiLlmProvider Provider, TestHttpMessageHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> responses)
    {
        var handler = new TestHttpMessageHandler(responses);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("KimiLlmProvider", new HttpClient(handler));
        var provider = new KimiLlmProvider(
            LlmConfig.Create("kimi-k2.6", TestApiKey),
            factory,
            resiliencePolicy: null,
            new TestLogger<KimiLlmProvider>());
        return (provider, handler);
    }

    private static string Success() => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = "ok" } } },
        usage = new { total_tokens = 7 },
    });

    [Fact]
    public async Task The_mandated_temperature_is_retried_once_and_the_second_request_carries_it()
    {
        var calls = 0;
        var (provider, handler) = Build(_ => ++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(RejectionBody) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Success()) });
        using var _ = provider;

        var result = await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ok", result.Content);
        Assert.Equal(2, handler.CapturedRequests.Count);

        var retried = await handler.CapturedRequests[1].Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(retried);
        Assert.Equal(1d, doc.RootElement.GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task A_persistent_rejection_is_surfaced_after_exactly_one_adaptive_retry()
    {
        var (provider, handler) = Build(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(RejectionBody) });
        using var _ = provider;

        var result = await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Content);
        Assert.Contains("Kimi API error", result.Metadata["error"].ToString());
        Assert.Equal(2, handler.CapturedRequests.Count); // one adaptation, never a loop
    }

    [Fact]
    public async Task An_unrelated_bad_request_is_not_retried()
    {
        var (provider, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"message":"context length exceeded","type":"invalid_request_error"}}"""),
        });
        using var _ = provider;

        var result = await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Content);
        Assert.Single(handler.CapturedRequests);
    }
}
