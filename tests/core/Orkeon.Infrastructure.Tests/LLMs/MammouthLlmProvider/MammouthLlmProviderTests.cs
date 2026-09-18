using Orkeon.Domain.SharedKernel.ValueObjects;
using Polly;
using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Mammouth AI provider: OpenAI-compatible transport against <c>api.mammouth.ai/v1</c>,
/// no behaviour of its own (LLM-09). Documentation-backed on 2026-09-18 — the capability
/// pins restate what the vendor documents, and the warning pin is D-04's promise: what the
/// documentation does not say is reported to the caller, never dropped in silence, until
/// the first campaign measures it.
/// </summary>
public sealed class MammouthLlmProviderTests : IDisposable
{
    private static readonly LlmMessage[] OneUserMessage = [LlmMessage.User("hello")];

    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<MammouthLlmProvider> _logger = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
    private readonly LlmConfig _config = LlmConfig.Create("gemini-3.7-flash", TestApiKey);
    private TestHttpMessageHandler? _handler;

    private static string OkResponse(string content, int tokens) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content } } },
        usage = new { total_tokens = tokens }
    });

    private MammouthLlmProvider CreateProvider(HttpStatusCode status, string body, LlmConfig? config = null)
    {
        _handler = TestHttpMessageHandler.CreateWithResponse(status, body);
        _httpClientFactory.RegisterClient(nameof(MammouthLlmProvider), new HttpClient(_handler));
        return new MammouthLlmProvider(config ?? _config, _httpClientFactory, _noOpPolicy, _logger);
    }

    private async Task<JsonElement> SentPayloadAsync()
    {
        var request = Assert.Single(_handler!.CapturedRequests);
        var raw = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void ShouldReturnMammouth_WhenName()
    {
        using var provider = new MammouthLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        Assert.Equal("mammouth", provider.Name);
    }

    [Fact]
    public void ShouldDeclareDocumentedCapabilities()
    {
        using var provider = new MammouthLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Documented 2026-09-18 (D-04): response_format and thinking undocumented, so None
        // until measured; vision declared from the vendor's own `text, image` model list.
        Assert.Equal(ResponseFormatSupport.None, provider.Capabilities.ResponseFormat);
        Assert.Equal(ThinkingSupport.None, provider.Capabilities.Thinking);
        Assert.True(provider.Capabilities.Vision);
        Assert.False(provider.Capabilities.ReplaysReasoningContent);
        Assert.False(provider.Capabilities.ExplicitPromptCaching);
        Assert.False(provider.Capabilities.RequiresJsonKeywordInPrompt);
    }

    [Fact]
    public async Task ShouldCallTheProxyEndpoint_WithBearerAndNoExtraHeader_WhenGenerateAsync()
    {
        using var provider = CreateProvider(HttpStatusCode.OK, OkResponse("ok", 3));

        var response = await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ok", response.Content);
        var request = Assert.Single(_handler!.CapturedRequests);
        Assert.Equal("api.mammouth.ai", request.RequestUri!.Host);
        Assert.Equal("/v1/chat/completions", request.RequestUri.AbsolutePath);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(TestApiKey, request.Headers.Authorization?.Parameter);
        // No vendor header of any kind: the bearer token is the whole handshake.
        Assert.Equal(["Authorization"], request.Headers.Select(h => h.Key));
    }

    [Fact]
    public async Task ShouldCallTheProxyEndpoint_WhenChatAsync()
    {
        using var provider = CreateProvider(HttpStatusCode.OK, OkResponse("ok", 3));

        var response = await provider.ChatAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ok", response.Content);
        var request = Assert.Single(_handler!.CapturedRequests);
        Assert.Equal("api.mammouth.ai", request.RequestUri!.Host);
        Assert.Equal("/v1/chat/completions", request.RequestUri.AbsolutePath);
        var payload = await SentPayloadAsync();
        Assert.Equal("gemini-3.7-flash", payload.GetProperty("model").GetString());
    }

    [Fact]
    public async Task ShouldWarnWithoutWriting_WhenAResponseFormatIsDeclared()
    {
        var config = _config with { ResponseFormat = LlmResponseFormat.JsonObject() };
        using var provider = CreateProvider(HttpStatusCode.OK, OkResponse("ok", 3), config);

        await provider.ChatAsync(OneUserMessage, config, TestContext.Current.CancellationToken);

        var payload = await SentPayloadAsync();
        Assert.False(payload.TryGetProperty("response_format", out _));
        var warning = Assert.Single(_logger.LoggedMessages, m => m.Contains("response_format", StringComparison.Ordinal));
        Assert.Contains("Mammouth does not support it", warning, StringComparison.Ordinal);
        Assert.Contains("constrain the output in the prompt", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldWarnWithoutWriting_WhenAThinkingOptionIsDeclared()
    {
        var config = _config with { Thinking = new LlmThinkingConfig { Effort = "high" } };
        using var provider = CreateProvider(HttpStatusCode.OK, OkResponse("ok", 3), config);

        await provider.ChatAsync(OneUserMessage, config, TestContext.Current.CancellationToken);

        var payload = await SentPayloadAsync();
        Assert.False(payload.TryGetProperty("reasoning_effort", out _));
        Assert.False(payload.TryGetProperty("thinking", out _));
        Assert.Single(_logger.LoggedMessages, m => m.Contains("thinking", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldSurfaceTheVendorMessage_WhenTheProxyRefusesTheKey()
    {
        // The LiteLLM refusal shape, measured cold on 2026-09-18.
        using var provider = CreateProvider(
            HttpStatusCode.Unauthorized,
            """{"error":{"message":"Authentication Error, No api key passed in.","type":"auth_error","param":"None","code":"401"}}""");

        var response = await provider.ChatAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("", response.Content);
        var error = Assert.IsType<string>(response.Metadata["error"]);
        Assert.Contains("Mammouth API error: Unauthorized", error, StringComparison.Ordinal);
        Assert.Contains("Authentication Error, No api key passed in.", error, StringComparison.Ordinal);
        Assert.Equal("APIError", response.Metadata["error_type"]);
    }

    public void Dispose() => _handler?.Dispose();
}
