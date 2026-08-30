using Orkeon.Domain.SharedKernel.ValueObjects;
using Polly;
using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// MiniMax provider: OpenAI-compatible transport against <c>api.minimax.io</c>.
/// Campaign-backed since 2026-08-30: every pin below restates a live measurement (the
/// provider doc comment carries the verdicts).
/// </summary>
public class MiniMaxLlmProviderTests
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<MiniMaxLlmProvider> _logger;
    private readonly LlmConfig _config;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;

    public MiniMaxLlmProviderTests()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<MiniMaxLlmProvider>();
        _config = LlmConfig.Create("MiniMax-M2", TestApiKey);
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
    }

    private static string OkResponse(string content, int tokens) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content } } },
        usage = new { total_tokens = tokens }
    });

    [Fact]
    public void ShouldReturnMiniMax_WhenName()
    {
        using var provider = new MiniMaxLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);
        Assert.Equal("minimax", provider.Name);
    }

    [Fact]
    public void ShouldDeclareDocumentedCapabilities()
    {
        using var provider = new MiniMaxLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        // Measured (2026-08-30): vision through the VL family (per model, D-03 - the text
        // flagship answers "I'm unable to view the image"); response_format None BY
        // MEASUREMENT (accepted but non-binding: schema ignored, json_object fenced);
        // thinking None (always on, inline, no knob - the dialect splits the trace out).
        Assert.Equal(ResponseFormatSupport.None, provider.Capabilities.ResponseFormat);
        Assert.Equal(ThinkingSupport.None, provider.Capabilities.Thinking);
        Assert.True(provider.Capabilities.Vision);
        Assert.False(provider.Capabilities.ExplicitPromptCaching);
        // True since the first campaign: the trace arrives inline (<think> in content), is
        // split out by the dialect, and the vendor documents that history must keep it.
        Assert.True(provider.Capabilities.ReplaysReasoningContent);
    }

    [Fact]
    public async Task ShouldCallTheInternationalEndpoint_WhenGenerateAsync()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkResponse("ok", 3));
        _httpClientFactory.RegisterClient(nameof(MiniMaxLlmProvider), new HttpClient(handler));
        using var provider = new MiniMaxLlmProvider(_config, _httpClientFactory, _noOpPolicy, _logger);

        var response = await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ok", response.Content);
        var request = handler.CapturedRequests.Single();
        Assert.Equal("api.minimax.io", request.RequestUri!.Host);
        Assert.StartsWith("/v1", request.RequestUri.AbsolutePath, StringComparison.Ordinal);
    }
}
