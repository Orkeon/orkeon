using Orkeon.Domain.SharedKernel.ValueObjects;
using Polly;
using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// x.AI (Grok) provider: OpenAI-compatible transport against <c>api.x.ai</c>. The provider
/// was preceded by its own proof — a full 12-mode campaign passed against the live endpoint
/// through the generic OpenAI dialect before this class existed (2026-08-30, archived under
/// <c>llmproviders-test/custom-endpoints/</c>) — so every declaration pinned here restates a
/// measurement, not a hope.
/// </summary>
public class GrokLlmProviderTests
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<GrokLlmProvider> _logger;
    private readonly LlmConfig _config;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;

    public GrokLlmProviderTests()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<GrokLlmProvider>();
        _config = LlmConfig.Create("grok-4.6", TestApiKey);
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
    }

    private static string OkResponse(string content, int tokens) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content } } },
        usage = new { total_tokens = tokens }
    });

    [Fact]
    public void ShouldReturnGrok_WhenName()
    {
        using var provider = new GrokLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        Assert.Equal("grok", provider.Name);
    }

    [Fact]
    public void ShouldDeclareMeasuredCapabilities()
    {
        using var provider = new GrokLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // All measured on grok-4.6, 2026-08-30: json_schema honoured (M8), effort hint
        // accepted with the trace replayed (M7), images read (M9). The implicit cache
        // reports the OpenAI-standard cached_tokens (M10) - nothing to declare.
        Assert.Equal(ResponseFormatSupport.JsonSchema, provider.Capabilities.ResponseFormat);
        Assert.Equal(ThinkingSupport.EffortOnly, provider.Capabilities.Thinking);
        Assert.True(provider.Capabilities.Vision);
        Assert.False(provider.Capabilities.ExplicitPromptCaching);
        Assert.False(provider.Capabilities.ReplaysReasoningContent);
    }

    [Fact]
    public async Task ShouldCallTheXaiEndpoint_WhenGenerateAsync()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkResponse("ok", 3));
        _httpClientFactory.RegisterClient(nameof(GrokLlmProvider), new HttpClient(handler));
        using var provider = new GrokLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        var response = await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ok", response.Content);
        var request = handler.CapturedRequests.Single();
        Assert.Equal("api.x.ai", request.RequestUri!.Host);
        Assert.StartsWith("/v1", request.RequestUri.AbsolutePath, StringComparison.Ordinal);
    }
}
