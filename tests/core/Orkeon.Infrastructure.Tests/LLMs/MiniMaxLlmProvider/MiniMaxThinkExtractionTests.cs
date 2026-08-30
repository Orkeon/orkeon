using System.Net;
using System.Text.Json;
using Polly;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// MiniMax delivers its reasoning INLINE: every reply starts with a
/// <c>&lt;think&gt;...&lt;/think&gt;</c> block inside <c>content</c>, with no separate
/// field (measured 2026-08-30 on the first live campaign — 158 characters for a one-line
/// hello, the trace everywhere, M9's verdict polluted by it). Left as-is, every agent built
/// on MiniMax speaks its private reasoning out loud. The dialect therefore splits the block
/// out — visible answer in <see cref="LlmResponse.Content"/>, trace in the
/// <c>reasoning_content</c> metadata — and puts it back verbatim on replay, because the
/// vendor documents that multi-turn history must keep the think blocks.
/// </summary>
public sealed class MiniMaxThinkExtractionTests : IDisposable
{
    private const string ThinkingReply =
        """{"choices":[{"message":{"role":"assistant","content":"<think>\nThe user greets; keep it short.\n</think>\n\nHello! Nice to meet you."}}],"usage":{"total_tokens":40,"prompt_tokens":10,"completion_tokens":30}}""";

    private const string PlainReply =
        """{"choices":[{"message":{"role":"assistant","content":"Just the answer."}}],"usage":{"total_tokens":8,"prompt_tokens":4,"completion_tokens":4}}""";

    private readonly MockHttpClientFactory _httpClientFactory;
    private readonly MockHttpMessageHandler _handler;
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy;
    private readonly LlmConfig _config;

    public MiniMaxThinkExtractionTests()
    {
        _httpClientFactory = new MockHttpClientFactory();
        _handler = _httpClientFactory.SetupDefaultHandler();
        _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();
        _config = LlmConfig.Create("MiniMax-M2", TestApiKey);
    }

    private MiniMaxLlmProvider CreateProvider() => new(_config, _httpClientFactory, _noOpPolicy);

    private void Respond(string body) =>
        _handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });

    [Fact]
    public async Task ShouldSplitTheThinkBlock_OnThePromptPath()
    {
        Respond(ThinkingReply);
        using var provider = CreateProvider();

        var response = await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Hello! Nice to meet you.", response.Content);
        Assert.True(response.Metadata.TryGetValue("reasoning_content", out var trace));
        Assert.Equal("The user greets; keep it short.", ((string)trace!).Trim());
    }

    [Fact]
    public async Task ShouldSplitTheThinkBlock_OnTheChatPath()
    {
        Respond(ThinkingReply);
        using var provider = CreateProvider();

        var response = await provider.ChatAsync(
            [LlmMessage.User("hello")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Hello! Nice to meet you.", response.Content);
        Assert.True(response.Metadata.ContainsKey("reasoning_content"));
    }

    [Fact]
    public async Task ShouldLeaveContentUntouched_WhenThereIsNoThinkBlock()
    {
        Respond(PlainReply);
        using var provider = CreateProvider();

        var response = await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Just the answer.", response.Content);
        Assert.False(response.Metadata.ContainsKey("reasoning_content"));
    }

    /// <summary>
    /// The vendor documents that multi-turn history must KEEP the think blocks. Orkeon
    /// removed them from the visible content, so replay is where they go back: the assistant
    /// turn is reconstructed with the trace re-inlined ahead of the answer, verbatim shape.
    /// </summary>
    [Fact]
    public async Task ShouldReinlineTheTrace_WhenReplayingAnAssistantTurn()
    {
        Respond(PlainReply);
        using var provider = CreateProvider();

        await provider.ChatAsync(
            [
                LlmMessage.User("hello"),
                new LlmMessage
                {
                    Role = "assistant",
                    Content = "Hello! Nice to meet you.",
                    ReasoningContent = "The user greets; keep it short.",
                },
                LlmMessage.User("and again?"),
            ],
            cancellationToken: TestContext.Current.CancellationToken);

        var body = await _handler.LastRequest!.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var payload = JsonDocument.Parse(body);
        var assistant = payload.RootElement.GetProperty("messages").EnumerateArray()
            .Single(m => m.GetProperty("role").GetString() == "assistant");
        Assert.Equal(
            "<think>\nThe user greets; keep it short.\n</think>\n\nHello! Nice to meet you.",
            assistant.GetProperty("content").GetString());
        Assert.False(assistant.TryGetProperty("reasoning_content", out _));
    }

    public void Dispose()
    {
        _handler.Dispose();
        _httpClientFactory.Dispose();
    }
}
