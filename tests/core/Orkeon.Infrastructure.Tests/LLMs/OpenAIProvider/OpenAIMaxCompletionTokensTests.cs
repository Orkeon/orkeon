using System.Net;
using System.Text.Json;
using Polly;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// OpenAI retired <c>max_tokens</c> on its current chat models: the 2026-08-30 campaign
/// failed ten modes out of twelve on <c>gpt-5.6-sol</c> with
/// <c>"Unsupported parameter: 'max_tokens' is not supported with this model. Use
/// 'max_completion_tokens' instead."</c> — the first ten real requests Orkeon ever sent
/// to api.openai.com. The replacement field is accepted by the older generations too
/// (verified live against <c>gpt-4o-mini</c> the same day), so the OpenAI dialect writes
/// <c>max_completion_tokens</c> unconditionally.
/// </summary>
/// <remarks>
/// The rename is OpenAI's own, not the compatible family's: DeepSeek, Together and the other
/// OpenAI-compatible vendors still document and expect <c>max_tokens</c> — all of them
/// passed their campaigns with it the same day. Hence the base keeps writing
/// <c>max_tokens</c> and only <see cref="OpenAIProvider"/> overrides the field name.
/// </remarks>
public sealed class OpenAIMaxCompletionTokensTests : IDisposable
{
    private const string MinimalSuccessResponse =
        """{"choices":[{"message":{"content":"ok"}}],"usage":{"total_tokens":2}}""";

    private readonly MockHttpClientFactory _httpClientFactory;
    private readonly MockHttpMessageHandler _handler;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;
    private readonly LlmConfig _config;

    public OpenAIMaxCompletionTokensTests()
    {
        _httpClientFactory = new MockHttpClientFactory();
        _handler = _httpClientFactory.SetupDefaultHandler();
        _handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(MinimalSuccessResponse, System.Text.Encoding.UTF8, "application/json")
        });
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
        _config = LlmConfig.Create("gpt-5.6-sol", TestApiKey) with { MaxTokens = 512 };
    }

    private async Task<JsonDocument> ReadSentPayloadAsync()
    {
        Assert.NotNull(_handler.LastRequest);
        Assert.NotNull(_handler.LastRequest!.Content);
        var body = await _handler.LastRequest.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(body);
    }

    [Fact]
    public async Task ShouldSendMaxCompletionTokens_OnThePromptPath()
    {
        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy);

        await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        using var payload = await ReadSentPayloadAsync();
        Assert.Equal(512, payload.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(payload.RootElement.TryGetProperty("max_tokens", out _));
    }

    [Fact]
    public async Task ShouldSendMaxCompletionTokens_OnTheChatPath()
    {
        using var provider = new OpenAIProvider(_config, _httpClientFactory, _noOpPolicy);

        await provider.ChatAsync(
            [LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        using var payload = await ReadSentPayloadAsync();
        Assert.Equal(512, payload.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(payload.RootElement.TryGetProperty("max_tokens", out _));
    }

    /// <summary>
    /// The rename must not leak into the rest of the family: the compatible vendors still
    /// expect <c>max_tokens</c>, and every one of them passed its 2026-08-30 campaign with it.
    /// </summary>
    [Fact]
    public async Task ShouldKeepMaxTokens_OnTheOpenAiCompatibleFamily()
    {
        var config = LlmConfig.Create("meta-llama/Llama-3.3-70B-Instruct-Turbo", TestApiKey) with { MaxTokens = 512 };
        using var provider = new TogetherAiLlmProvider(config, _httpClientFactory, _noOpPolicy);

        await provider.ChatAsync(
            [LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        using var payload = await ReadSentPayloadAsync();
        Assert.Equal(512, payload.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.False(payload.RootElement.TryGetProperty("max_completion_tokens", out _));
    }

    public void Dispose()
    {
        _handler.Dispose();
        _httpClientFactory.Dispose();
    }
}
