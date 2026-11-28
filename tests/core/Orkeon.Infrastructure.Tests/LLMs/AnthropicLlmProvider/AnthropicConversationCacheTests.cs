using System.Net;
using System.Text;
using System.Text.Json;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// #490: in a tool loop the conversation carries <c>cache_control</c> breakpoints, so each call reads
/// what the previous one wrote; at most <see cref="LlmCacheConfig.MaxBreakpoints"/> per request.
/// </summary>
public class AnthropicConversationCacheTests
{
    private const string OkBody = """{"content":[{"type":"text","text":"ok"}],"usage":{"input_tokens":1,"output_tokens":1}}""";

    private const string StreamBody =
        "data: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":1}}}\n\n"
        + "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"ok\"}}\n\n"
        + "data: {\"type\":\"message_delta\",\"usage\":{\"output_tokens\":1}}\n\n"
        + "data: {\"type\":\"message_stop\"}\n\n";

    private static readonly LlmCacheConfig AllOn = new() { CacheSystemPrompt = true, CacheTools = true };

    private static string ToolCalls(int count) => JsonSerializer.Serialize(
        Enumerable.Range(0, count).Select(i => new { id = $"call_{i}", function = new { name = "file_read", arguments = "{}" } }));

    /// <summary>A tool loop: the task, then <paramref name="rounds"/> assistant turns each followed by its results.</summary>
    private static LlmMessage[] ToolLoop(int rounds, int parallel, string? thinking = null)
    {
        var messages = new List<LlmMessage> { LlmMessage.System("You are careful."), LlmMessage.User("Task") };
        for (var r = 0; r < rounds; r++)
        {
            var calls = ToolCalls(parallel).Replace("call_", $"r{r}_call_", StringComparison.Ordinal);
            messages.Add(new LlmMessage { Role = "assistant", Content = $"Round {r}.", RawToolCalls = calls, RawThinkingBlocks = thinking });
            for (var i = 0; i < parallel; i++)
                messages.Add(new LlmMessage { Role = "tool", Content = $"result {r}.{i}", ToolCallId = $"r{r}_call_{i}" });
        }

        return [.. messages];
    }

    private static async Task<JsonElement> SendAsync(LlmMessage[] messages, LlmCacheConfig? cache, bool stream = false)
    {
        using var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = stream
                ? new StringContent(StreamBody, Encoding.UTF8, "text/event-stream")
                : new StringContent(OkBody, Encoding.UTF8, "application/json"),
        });
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("AnthropicLlmProvider", new HttpClient(handler));
        var config = LlmConfig.Create("claude-sonnet-5", TestApiKey) with
        {
            Cache = cache,
            Tools = [new ToolSchema("file_read", "Reads a file", [])],
        };
        using var provider = new AnthropicLlmProvider(
            config, factory, Policy.NoOpAsync<HttpResponseMessage>(), new TestLogger<AnthropicLlmProvider>());

        var ct = TestContext.Current.CancellationToken;
        if (stream)
        {
            await foreach (var _ in provider.ChatStreamingAsync(messages, cancellationToken: ct)) { }
        }
        else
        {
            await provider.ChatAsync(messages, cancellationToken: ct);
        }

        var raw = await handler.CapturedRequests.Single().Content!.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    /// <summary>Every content block of the messages, in wire order (a string content counts as one).</summary>
    private static List<JsonElement> Blocks(JsonElement payload) =>
        payload.GetProperty("messages").EnumerateArray()
            .SelectMany<JsonElement, JsonElement>(m => m.GetProperty("content") is { ValueKind: JsonValueKind.Array } content
                ? content.EnumerateArray()
                : [m.GetProperty("content")])
            .ToList();

    private static bool Marked(JsonElement block) =>
        block.ValueKind == JsonValueKind.Object && block.TryGetProperty("cache_control", out _);

    private static int CountBreakpoints(JsonElement payload)
    {
        var count = Blocks(payload).Count(Marked);
        if (payload.TryGetProperty("system", out var system) && system.ValueKind == JsonValueKind.Array)
            count += system.EnumerateArray().Count(Marked);
        if (payload.TryGetProperty("tools", out var tools))
            count += tools.EnumerateArray().Count(Marked);
        return count;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("BR", "CH-490")]
    public async Task ShouldMarkTheLastBlockOfTheConversation_OnBothPaths(bool stream)
    {
        var payload = await SendAsync(ToolLoop(rounds: 2, parallel: 2), AllOn, stream);

        var blocks = Blocks(payload);
        Assert.True(Marked(blocks[^1]));
        Assert.Equal("tool_result", blocks[^1].GetProperty("type").GetString());
        Assert.InRange(CountBreakpoints(payload), 3, LlmCacheConfig.MaxBreakpoints);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("BR", "CH-490")]
    public async Task ShouldPlaceASecondBreakpoint_WhenTheLastCallAddedMoreBlocksThanTheLookback(bool stream)
    {
        // The latest turn holds 1 text + 12 tool_use + 12 tool_result blocks: beyond the 20-block lookback.
        var payload = await SendAsync(ToolLoop(rounds: 2, parallel: 12), AllOn, stream);

        var messages = payload.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(LlmCacheConfig.MaxBreakpoints, CountBreakpoints(payload));
        // The second one sits on the block the previous call marked: the last result before the latest assistant turn.
        var previousResults = messages[^3].GetProperty("content").EnumerateArray().ToList();
        Assert.True(Marked(previousResults[^1]));
        Assert.Equal("r0_call_11", previousResults[^1].GetProperty("tool_use_id").GetString());
        Assert.True(Marked(messages[^1].GetProperty("content").EnumerateArray().Last()));
    }

    [Fact]
    [Trait("BR", "CH-490")]
    public async Task ShouldPlaceOneConversationBreakpoint_WhenTheLastCallStaysWithinTheLookback()
    {
        var payload = await SendAsync(ToolLoop(rounds: 3, parallel: 3), AllOn);

        Assert.Single(Blocks(payload), Marked);
        Assert.Equal(3, CountBreakpoints(payload));
    }

    [Fact]
    [Trait("BR", "CH-490")]
    public async Task ShouldNeverMarkAThinkingBlock()
    {
        const string thinking = """[{"type":"thinking","thinking":"hmm","signature":"sig"},{"type":"redacted_thinking","data":"x"}]""";
        var payload = await SendAsync(ToolLoop(rounds: 2, parallel: 12, thinking), AllOn);

        var thinkingBlocks = Blocks(payload)
            .Where(b => b.ValueKind == JsonValueKind.Object
                && b.GetProperty("type").GetString() is "thinking" or "redacted_thinking")
            .ToList();
        Assert.Equal(4, thinkingBlocks.Count);
        Assert.All(thinkingBlocks, b => Assert.False(Marked(b)));
        Assert.True(CountBreakpoints(payload) <= LlmCacheConfig.MaxBreakpoints);
    }

    [Fact]
    [Trait("BR", "CH-490")]
    public async Task ShouldPromoteAPlainStringMessage_ToAMarkedTextBlock()
    {
        var payload = await SendAsync([LlmMessage.User("hello")], new LlmCacheConfig { CacheConversation = true, Ttl = "1h" });

        var block = Assert.Single(payload.GetProperty("messages")[0].GetProperty("content").EnumerateArray());
        Assert.Equal("hello", block.GetProperty("text").GetString());
        Assert.Equal("1h", block.GetProperty("cache_control").GetProperty("ttl").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("BR", "CH-490")]
    public async Task ShouldMarkNoConversationBlock_WhenTheSwitchIsOff(bool stream)
    {
        var payload = await SendAsync(
            ToolLoop(rounds: 2, parallel: 12),
            AllOn with { CacheConversation = false },
            stream);

        Assert.DoesNotContain(Blocks(payload), Marked);
        Assert.Equal(2, CountBreakpoints(payload));
        // Without any cache the wire shape stays the plain string of a simple message.
        var uncached = await SendAsync([LlmMessage.User("hello")], cache: null, stream);
        Assert.Equal(JsonValueKind.String, uncached.GetProperty("messages")[0].GetProperty("content").ValueKind);
    }

    [Fact]
    [Trait("BR", "CH-490")]
    public void ShouldFollowTheOtherSwitches_WhenTheConversationIsLeftUnset()
    {
        Assert.True(new LlmCacheConfig { CacheSystemPrompt = true }.CacheConversation);
        Assert.True(new LlmCacheConfig { CacheTools = true }.CacheConversation);
        Assert.False(LlmCacheConfig.Off.CacheConversation);
        Assert.False(new LlmCacheConfig { CacheSystemPrompt = true, CacheConversation = false }.CacheConversation);
        Assert.True(LlmCacheConfig.Request(false, false, true, "1h")!.RequestsAnyBreakpoint);
        Assert.Null(LlmCacheConfig.Request(false, false, null, null));
        Assert.Equal(new LlmCacheConfig { CacheSystemPrompt = true }, LlmCacheConfig.Request(true, false, null));
    }
}
