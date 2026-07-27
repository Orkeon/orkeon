using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// HuggingFace provider-selection suffixes (LLM-06, G-23) and the DeepSeek dialect guard
/// (G-22).
/// </summary>
public class HuggingFaceRoutingTests
{
    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<HuggingFaceLlmProvider> _logger = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();

    private static readonly string OkBody = JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = "ok" } } },
        usage = new { total_tokens = 1, prompt_tokens = 1, completion_tokens = 0 },
    });

    private async Task<JsonElement> SendAsync(string model)
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkBody);
        _httpClientFactory.RegisterClient("HuggingFaceLlmProvider", new HttpClient(handler));

        using var provider = new HuggingFaceLlmProvider(
            LlmConfig.Create(model, TestApiKey), _httpClientFactory, _noOpPolicy, _logger);
        await provider.GenerateAsync(TestPrompt, cancellationToken: TestContext.Current.CancellationToken);

        var raw = await handler.CapturedRequests.Single().Content!
            .ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    // ── G-23: routing policies ──────────────────────────────────────────────

    [Theory]
    [InlineData(HuggingFaceRoutingPolicy.Fastest, "openai/gpt-oss-120b:fastest")]
    [InlineData(HuggingFaceRoutingPolicy.Cheapest, "openai/gpt-oss-120b:cheapest")]
    [InlineData(HuggingFaceRoutingPolicy.Preferred, "openai/gpt-oss-120b:preferred")]
    public void ShouldBuildTheSuffixedModelId_ForEachPolicy(HuggingFaceRoutingPolicy policy, string expected)
    {
        Assert.Equal(expected, HuggingFaceLlmProvider.WithRoutingPolicy("openai/gpt-oss-120b", policy));
    }

    [Fact]
    public void ShouldBuildTheSuffixedModelId_ForAPinnedPartner()
    {
        Assert.Equal("openai/gpt-oss-120b:groq", HuggingFaceLlmProvider.WithPartner("openai/gpt-oss-120b", "groq"));
    }

    [Theory]
    [InlineData("openai/gpt-oss-120b:cheapest")]
    [InlineData("openai/gpt-oss-120b:groq")]
    [InlineData("meta-llama/Llama-3.1-8B-Instruct")]
    public async Task ShouldForwardAValidModelId_WithoutComplaint(string model)
    {
        var body = await SendAsync(model);

        Assert.Equal(model, body.GetProperty("model").GetString());
        Assert.DoesNotContain(_logger.LogEntries, e => e.Message.Contains("suffix", StringComparison.Ordinal));
    }

    /// <summary>
    /// An unknown suffix is still forwarded — the partner list moves faster than this
    /// framework releases — but it is no longer invisible.
    /// </summary>
    [Fact]
    public async Task ShouldForwardAnUnknownSuffix_ButReportIt()
    {
        var body = await SendAsync("openai/gpt-oss-120b:fastesst");

        Assert.Equal("openai/gpt-oss-120b:fastesst", body.GetProperty("model").GetString());
        Assert.True(_logger.HasLoggedWarning("fastesst"));
    }

    // ── G-22: the DeepSeek Anthropic-dialect endpoint ───────────────────────

    /// <summary>
    /// <c>api.deepseek.com/anthropic</c> speaks the Messages API. Host inference matches
    /// <c>deepseek.com</c> and would route it to the OpenAI-compatible provider, producing
    /// malformed requests whose error surfaces far from its cause.
    /// </summary>
    [Fact]
    public void ShouldRefuseTheDeepSeekAnthropicEndpoint_WithAnActionableMessage()
    {
        var factory = new LlmProviderFactory(new TestHttpClientFactory(), NullLoggerFactory.Instance);
        var config = LlmConfig.Create("deepseek-v4-flash", TestApiKey) with
        {
            BaseUrl = new Uri("https://api.deepseek.com/anthropic"),
        };

        var ex = Assert.Throws<NotSupportedException>(() => factory.Create(config));

        Assert.Contains("Anthropic Messages API", ex.Message, StringComparison.Ordinal);
        Assert.Contains("https://api.deepseek.com", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldStillAcceptTheStandardDeepSeekEndpoint()
    {
        var factory = new LlmProviderFactory(new TestHttpClientFactory(), NullLoggerFactory.Instance);
        var config = LlmConfig.Create("deepseek-v4-flash", TestApiKey) with
        {
            BaseUrl = new Uri("https://api.deepseek.com"),
        };

        Assert.Equal("deepseek", factory.Create(config).Name);
    }
}
