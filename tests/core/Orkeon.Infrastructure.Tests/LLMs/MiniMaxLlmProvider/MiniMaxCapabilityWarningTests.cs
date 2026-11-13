using System.Net;
using System.Text.Json;
using Polly;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// MiniMax is the provider whose measurements make the two "None" warning branches
/// uniquely load-bearing: <c>response_format</c> is ACCEPTED by the API but non-binding
/// (schema ignored, json_object fenced in markdown — measured 2026-08-30), and the
/// reasoning pass is always on with no knob. The provider's own rationale leans on the
/// warning machinery ("precisely the silent drop the warning exists to surface"), so the
/// warnings must fire — and must not claim "this API has no such field" when the measured
/// truth is "the field exists and is ignored".
/// </summary>
public sealed class MiniMaxCapabilityWarningTests : IDisposable
{
    private const string OkResponse =
        """{"choices":[{"message":{"role":"assistant","content":"ok"}}],"usage":{"total_tokens":2,"prompt_tokens":1,"completion_tokens":1}}""";

    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<MiniMaxLlmProvider> _logger = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
    private readonly TestHttpMessageHandler _handler;

    public MiniMaxCapabilityWarningTests()
    {
        _handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkResponse);
        _httpClientFactory.RegisterClient(nameof(MiniMaxLlmProvider), new HttpClient(_handler));
    }

    private MiniMaxLlmProvider CreateProvider(LlmConfig config) =>
        new(config, _httpClientFactory, _noOpPolicy, _logger);

    private async Task<JsonElement> SentPayloadAsync()
    {
        var request = Assert.Single(_handler.CapturedRequests);
        var raw = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task ShouldWarnWithoutWriting_WhenAResponseFormatIsDeclared()
    {
        var config = LlmConfig.Create("MiniMax-M2", TestApiKey) with
        {
            ResponseFormat = LlmResponseFormat.JsonObject(),
        };
        using var provider = CreateProvider(config);

        await provider.ChatAsync(
            [LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        var payload = await SentPayloadAsync();
        Assert.False(payload.TryGetProperty("response_format", out _));

        var warning = Assert.Single(
            _logger.LoggedMessages, m => m.Contains("response_format", StringComparison.Ordinal));
        // The honest diagnosis: the option is not honoured — which covers both an API
        // without the field AND MiniMax's field-accepted-but-ignored reality. Claiming
        // "this API has no response-format field" would be false here.
        Assert.Contains("non-binding", warning, StringComparison.Ordinal);
        Assert.Contains("constrain the output in the prompt", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldOmitTheOffSwitchAndWarn_WhenTheModelAlwaysReasons()
    {
        // MiniMax-M3.1-Flash refuses the off switch (campaign M18, #356): the effort travels, the switch is reported.
        var config = LlmConfig.Create("MiniMax-M3.1-Flash", TestApiKey) with
        {
            Thinking = new LlmThinkingConfig { Enabled = false, Effort = "high" },
        };
        using var provider = CreateProvider(config);

        await provider.ChatAsync(
            [LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        var payload = await SentPayloadAsync();
        Assert.Equal("high", payload.GetProperty("reasoning_effort").GetString());
        Assert.False(payload.TryGetProperty("thinking", out _));
        var warning = Assert.Single(
            _logger.LoggedMessages, m => m.Contains("thinking.enabled", StringComparison.Ordinal));
        Assert.Contains("always reasons", warning, StringComparison.Ordinal);
    }

    public void Dispose() => _handler.Dispose();
}
