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
/// API's own rejection — no model list to drift. Only a temperature that was set and sent is
/// replaced: one nothing sets is not sent (GAP-36), and leaves nothing to retry.
/// </summary>
public sealed class KimiTemperatureConstraintTests
{
    private const string RejectionBody =
        """{"error":{"message":"invalid temperature: only 1 is allowed for this model","type":"invalid_request_error"}}""";

    private static (KimiLlmProvider Provider, TestHttpMessageHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> responses, LlmConfig? configured = null)
    {
        var handler = new TestHttpMessageHandler(responses);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("KimiLlmProvider", new HttpClient(handler));
        var provider = new KimiLlmProvider(
            configured ?? LlmConfig.Create("kimi-k2.6", TestApiKey) with { Temperature = 0.7 },
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
    public async Task A_temperature_that_was_never_sent_leaves_nothing_to_retry()
    {
        var (provider, handler) = Build(
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(RejectionBody) },
            LlmConfig.Create("kimi-k2.6", TestApiKey));
        using var owned = provider;

        var result = await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Content);
        var sent = Assert.Single(handler.CapturedRequests);
        using var doc = JsonDocument.Parse(await sent.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(doc.RootElement.TryGetProperty("temperature", out _));
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

/// <summary>
/// The K3 family (the default since 2026-11-11) fixes its temperature and top_p and refuses a
/// <c>thinking</c> field: those fields are left out before the request leaves, on the buffered and
/// the streaming paths alike, and each is reported as a structured warning.
/// </summary>
public sealed class KimiFixedSamplingTests
{
    private static string Success() => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = "ok" } } },
        usage = new { total_tokens = 7 },
    });

    private static (KimiLlmProvider Provider, TestHttpMessageHandler Handler, TestLogger<KimiLlmProvider> Logger) Build(
        LlmConfig config, string body)
    {
        var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, body);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("KimiLlmProvider", new HttpClient(handler));
        var logger = new TestLogger<KimiLlmProvider>();
        return (new KimiLlmProvider(config, factory, resiliencePolicy: null, logger), handler, logger);
    }

    private static async Task<JsonElement> SentBodyAsync(TestHttpMessageHandler handler)
    {
        var raw = await handler.CapturedRequests.Single().Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    private static LlmConfig Steered(string model) => LlmConfig.Create(model, TestApiKey) with
    {
        Temperature = 0.2,
        TopP = 0.9,
        Thinking = new LlmThinkingConfig { Enabled = false },
    };

    [Theory]
    [InlineData("kimi-k3.1-pro")]
    [InlineData("kimi-k3")]
    [InlineData("Kimi-K3.1-Pro-Highspeed")]
    public async Task ShouldLeaveOutFixedFieldsAndWarn_WhenModelIsInTheK3Family(string model)
    {
        var (provider, handler, logger) = Build(Steered(model), Success());
        using var owned = provider;

        var result = await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ok", result.Content);
        var body = await SentBodyAsync(handler);
        Assert.False(body.TryGetProperty("temperature", out _));
        Assert.False(body.TryGetProperty("top_p", out _));
        Assert.True(logger.HasLoggedWarning("Option 'temperature'"));
        Assert.True(logger.HasLoggedWarning("Option 'top_p'"));
        // The K3 family takes the off switch, and it stops the trace (campaign M18, #356): it is sent.
        Assert.Equal("disabled", body.GetProperty("thinking").GetProperty("type").GetString());
        Assert.False(logger.HasLoggedWarning("Option 'thinking.enabled'"));
    }

    [Fact]
    public async Task ShouldLeaveOutFixedFields_WhenTheK3ModelStreams()
    {
        const string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\ndata: [DONE]\n\n";
        var (provider, handler, logger) = Build(Steered("kimi-k3.1-pro"), sse);
        using var owned = provider;

        var tokens = new List<string>();
        await foreach (var token in provider.GenerateStreamingAsync("hello", cancellationToken: TestContext.Current.CancellationToken))
            tokens.Add(token);

        Assert.Equal(["ok"], tokens);
        var body = await SentBodyAsync(handler);
        Assert.False(body.TryGetProperty("temperature", out _));
        Assert.False(body.TryGetProperty("top_p", out _));
        Assert.Equal("disabled", body.GetProperty("thinking").GetProperty("type").GetString());
        Assert.True(logger.HasLoggedWarning("Option 'temperature'"));
    }

    [Fact]
    public async Task ShouldSendNoSamplingFieldAndNoWarning_WhenDefaultModelHasDefaultConfiguration()
    {
        var (provider, handler, logger) = Build(LlmConfig.OnProfile() with { ApiKey = TestApiKey }, Success());
        using var owned = provider;

        await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        var body = await SentBodyAsync(handler);
        Assert.Equal("kimi-k3.1-pro", body.GetProperty("model").GetString());
        Assert.False(body.TryGetProperty("temperature", out _));
        Assert.False(body.TryGetProperty("thinking", out _));
        Assert.False(logger.HasLoggedWarning("was declared"));
    }

    [Fact]
    public async Task ShouldKeepTheSteeredFields_WhenModelIsOutsideTheK3Family()
    {
        var (provider, handler, logger) = Build(Steered("kimi-k2.6"), Success());
        using var owned = provider;

        await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        var body = await SentBodyAsync(handler);
        Assert.Equal(0.2, body.GetProperty("temperature").GetDouble());
        Assert.Equal(0.9, body.GetProperty("top_p").GetDouble());
        Assert.Equal("disabled", body.GetProperty("thinking").GetProperty("type").GetString());
        Assert.False(logger.HasLoggedWarning("was declared"));
    }

    [Fact]
    public async Task ShouldOmitTheOffSwitchAndWarn_WhenTheModelAlwaysReasons()
    {
        // kimi-k2.7-code answers "only type=enabled is allowed" to the off switch (campaign M18, #356).
        var (provider, handler, logger) = Build(Steered("kimi-k2.7-code"), Success());
        using var owned = provider;

        await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        var body = await SentBodyAsync(handler);
        Assert.False(body.TryGetProperty("thinking", out _));
        Assert.Equal(0.2, body.GetProperty("temperature").GetDouble());
        Assert.True(logger.HasLoggedWarning("Option 'thinking.enabled'"));
        Assert.True(logger.HasLoggedWarning("always reasons"));
    }

    [Theory]
    [InlineData("kimi-k3", true)]
    [InlineData("kimi-k3.1-pro", true)]
    [InlineData("kimi-k3-preview", true)]
    [InlineData("kimi-k30", false)]
    [InlineData("kimi-k2.6", false)]
    [InlineData(null, false)]
    public void IsFixedSamplingModel_ShouldMatchTheK3FamilyAtABoundary(string? model, bool expected) =>
        Assert.Equal(expected, KimiLlmProvider.IsFixedSamplingModel(model));
}
