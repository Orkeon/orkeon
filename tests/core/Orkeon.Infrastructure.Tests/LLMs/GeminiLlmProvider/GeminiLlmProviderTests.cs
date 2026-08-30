using Orkeon.Domain.SharedKernel.ValueObjects;
using Polly;
using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Google Gemini provider (PUB-15): OpenAI-compatible transport against the
/// generativelanguage endpoint, effort-only thinking, undeclared response_format.
/// </summary>
public class GeminiLlmProviderTests
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<GeminiLlmProvider> _logger;
    private readonly LlmConfig _config;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;

    public GeminiLlmProviderTests()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<GeminiLlmProvider>();
        _config = LlmConfig.Create("gemini-3.7-flash", TestApiKey);
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
    }

    private static string OkResponse(string content, int tokens) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content } } },
        usage = new { total_tokens = tokens }
    });

    [Fact]
    public void ShouldReturnGemini_WhenName()
    {
        using var provider = new GeminiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        Assert.Equal("gemini", provider.Name);
    }

    [Fact]
    public void ShouldDeclareVerifiedCapabilities()
    {
        using var provider = new GeminiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Verified against the OpenAI-compatibility docs (2026-08-18): reasoning_effort
        // is supported (effort-only), vision flows through image_url. response_format was
        // undocumented on the compat surface then and stayed undeclared; measured live on
        // 2026-08-30, the surface accepts both json_object and json_schema and honours the
        // schema (additionalProperties enforced), so the declaration follows the measurement.
        Assert.Equal(ThinkingSupport.EffortOnly, provider.Capabilities.Thinking);
        Assert.True(provider.Capabilities.Vision);
        Assert.Equal(ResponseFormatSupport.JsonSchema, provider.Capabilities.ResponseFormat);
    }

    [Fact]
    public async Task ShouldCallGeminiOpenAiCompatibleEndpoint_WhenGenerateAsync()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkResponse("Test response", 100));
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("GeminiLlmProvider", httpClient);

        using var provider = new GeminiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.CapturedRequests);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
            request.RequestUri!.ToString());
        // Bearer auth with the Gemini API key, per the compatibility docs.
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task ShouldReturnContent_WhenGenerateAsyncWithValidResponse()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkResponse("Test response", 100));
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("GeminiLlmProvider", httpClient);

        using var provider = new GeminiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        var result = await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Test response", result.Content);
        Assert.Equal(100, result.TokensUsed);
        Assert.Equal("gemini-3.7-flash", result.Model);
    }

    [Fact]
    public async Task ShouldReturnContent_WhenChatAsyncWithValidResponse()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkResponse("Chat response", 80));
        var httpClient = new HttpClient(handler);
        _httpClientFactory.RegisterClient("GeminiLlmProvider", httpClient);

        using var provider = new GeminiLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        var result = await provider.ChatAsync(
        [
            LlmMessage.User(TestPrompt)
        ], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Chat response", result.Content);
        Assert.Equal(80, result.TokensUsed);
    }
}
