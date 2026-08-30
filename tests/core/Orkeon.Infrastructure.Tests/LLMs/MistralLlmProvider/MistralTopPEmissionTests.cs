using System.Net;
using System.Text.Json;
using Polly;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// The base omits <c>top_p</c> when it equals 1.0, assuming omission means 1 on the wire.
/// Mistral's reasoning mode breaks that assumption: it runs its own internal top_p default
/// and validates greedy sampling against the EXPLICIT field, so <c>temperature: 0</c> plus
/// <c>reasoning_effort</c> with no <c>top_p</c> is refused outright (<c>"top_p must be 1
/// when using greedy sampling."</c>, measured 2026-08-30 — the same request with an explicit
/// <c>top_p: 1</c> passes). Omission-when-1 there is a silent drop of a configured value,
/// which is the failure mode this codebase refuses; the Mistral dialect therefore always
/// writes the configured <c>top_p</c>.
/// </summary>
public sealed class MistralTopPEmissionTests : IDisposable
{
    private const string MinimalSuccessResponse =
        """{"choices":[{"message":{"content":"ok"}}],"usage":{"total_tokens":2}}""";

    private readonly MockHttpClientFactory _httpClientFactory;
    private readonly MockHttpMessageHandler _handler;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;
    private readonly LlmConfig _config;

    public MistralTopPEmissionTests()
    {
        _httpClientFactory = new MockHttpClientFactory();
        _handler = _httpClientFactory.SetupDefaultHandler();
        _handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(MinimalSuccessResponse, System.Text.Encoding.UTF8, "application/json")
        });
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
        _config = LlmConfig.Create("mistral-medium-2604", TestApiKey);
    }

    private async Task<JsonDocument> ReadSentPayloadAsync()
    {
        Assert.NotNull(_handler.LastRequest);
        var body = await _handler.LastRequest!.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(body);
    }

    [Fact]
    public async Task ShouldEmitTopPOne_OnThePromptPath()
    {
        using var provider = new MistralLlmProvider(_config, _httpClientFactory, _noOpPolicy);

        await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        using var payload = await ReadSentPayloadAsync();
        Assert.Equal(1.0, payload.RootElement.GetProperty("top_p").GetDouble());
    }

    [Fact]
    public async Task ShouldEmitTopPOne_OnTheChatPath()
    {
        using var provider = new MistralLlmProvider(_config, _httpClientFactory, _noOpPolicy);

        await provider.ChatAsync(
            [LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        using var payload = await ReadSentPayloadAsync();
        Assert.Equal(1.0, payload.RootElement.GetProperty("top_p").GetDouble());
    }

    /// <summary>
    /// The omission stays everywhere else: OpenAI's reasoning models reject an explicit
    /// <c>top_p</c>, so "always send" is exactly as per-model wrong in the other direction.
    /// </summary>
    [Fact]
    public async Task ShouldKeepOmittingTopPOne_OnTheRestOfTheFamily()
    {
        var config = LlmConfig.Create("meta-llama/Llama-3.3-70B-Instruct-Turbo", TestApiKey);
        using var provider = new TogetherAiLlmProvider(config, _httpClientFactory, _noOpPolicy);

        await provider.ChatAsync(
            [LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        using var payload = await ReadSentPayloadAsync();
        Assert.False(payload.RootElement.TryGetProperty("top_p", out _));
    }

    public void Dispose()
    {
        _handler.Dispose();
        _httpClientFactory.Dispose();
    }
}
