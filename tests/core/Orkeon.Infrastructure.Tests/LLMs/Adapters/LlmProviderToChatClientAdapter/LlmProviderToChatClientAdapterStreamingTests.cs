using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.LLMs.ToolCalling;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Adapters;

/// <summary>
/// D5-03: the adapter's streaming fallback (taken whenever the provider does not stream, which
/// is what an unconfigured provider now declares) must not hand M.E.AI an empty
/// <see cref="ChatResponseUpdate"/>. The provider's refusal travels in the response metadata,
/// which an update does not carry, so the enumeration fails instead of ending on silence.
/// GAP-32: the streaming path is built like the buffered one — folded, its updates are the
/// buffered response: text, tool calls (native and of the text protocol), usage, finish reason,
/// model, the vendor's cost and the reasoning to replay, in one assistant message.
/// </summary>
public class LlmProviderToChatClientAdapterStreamingTests
{
    private const string MissingKeyError = "OpenAI API key is required";

    private static readonly ChatMessage[] OneUserMessage = [new(ChatRole.User, "hi")];

    [Fact]
    public async Task Streaming_fallback_fails_when_the_buffered_answer_is_a_provider_error()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(new LlmResponse
        {
            Content = "",
            Metadata = new Dictionary<string, object> { ["error"] = MissingKeyError },
        });

        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var updates = new List<ChatResponseUpdate>();
        var failure = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var update in adapter.GetStreamingResponseAsync(
                               OneUserMessage, cancellationToken: TestContext.Current.CancellationToken))
                updates.Add(update);
        });

        Assert.Contains(MissingKeyError, failure.Message, StringComparison.Ordinal);
        Assert.Empty(updates);
    }

    [Fact]
    public async Task Streaming_fallback_still_yields_an_update_when_the_model_simply_said_nothing()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(new LlmResponse { Content = "" });

        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in adapter.GetStreamingResponseAsync(
                           OneUserMessage, cancellationToken: TestContext.Current.CancellationToken))
            updates.Add(update);

        Assert.Single(updates);
        Assert.Equal("", updates[0].Text);
    }

    // ── GAP-32: streamed and buffered answers fold to the same response ─────

    private static LlmResponse Answered(string content, string? rawBody = null) => new()
    {
        Content = content,
        PromptTokens = 40,
        CompletionTokens = 12,
        TokensUsed = 52,
        CacheHitTokens = 30,
        CacheMissTokens = 10,
        Model = "vendor/model-x",
        RawResponseBody = rawBody,
        Metadata = new Dictionary<string, object>
        {
            ["reasoning_content"] = "weighing the options",
            [LlmUsageMetadataKeys.Cost] = 0.0021,
            [LlmUsageMetadataKeys.CostCurrency] = "USD",
        },
    };

    /// <summary>The same provider answering <paramref name="answer"/> buffered, and streamed in <paramref name="chunks"/>.</summary>
    private static MockStreamingLlmProvider Provider(LlmResponse answer, params string[] chunks)
    {
        var provider = new MockStreamingLlmProvider { SupportsStreaming = true };
        provider.SetChatResult(answer);
        provider.SetStreamingChunks(chunks);
        provider.SetCompletedResponse(answer);
        return provider;
    }

    private static async Task<(ChatResponse Buffered, ChatResponse Streamed)> BothAsync(LlmProviderToChatClientAdapter adapter)
    {
        var buffered = await adapter.GetResponseAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken);
        var streamed = await adapter.GetStreamingResponseAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken)
            .ToChatResponseAsync(TestContext.Current.CancellationToken);
        return (buffered, streamed);
    }

    /// <summary>What the provider receives when <paramref name="response"/>'s messages are sent back on the next turn.</summary>
    private static async Task<LlmMessage> ReplayedAsync(MockStreamingLlmProvider provider, LlmProviderToChatClientAdapter adapter, ChatResponse response)
    {
        await adapter.GetResponseAsync(
            [.. OneUserMessage, .. response.Messages, new ChatMessage(ChatRole.User, "go on")],
            cancellationToken: TestContext.Current.CancellationToken);
        return provider.LastChatMessages!.Single(m => m.Role == "assistant");
    }

    private static void AssertEquivalent(ChatResponse buffered, ChatResponse streamed)
    {
        Assert.Single(streamed.Messages);
        Assert.Equal(ChatRole.Assistant, streamed.Messages[0].Role);
        Assert.Equal(buffered.Text, streamed.Text);
        Assert.Equal(
            buffered.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Select(c => (c.CallId, c.Name, Args: System.Text.Json.JsonSerializer.Serialize(c.Arguments))),
            streamed.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Select(c => (c.CallId, c.Name, Args: System.Text.Json.JsonSerializer.Serialize(c.Arguments))));
        Assert.Equal(buffered.Usage?.InputTokenCount, streamed.Usage?.InputTokenCount);
        Assert.Equal(buffered.Usage?.OutputTokenCount, streamed.Usage?.OutputTokenCount);
        Assert.Equal(buffered.Usage?.TotalTokenCount, streamed.Usage?.TotalTokenCount);
        Assert.Equal(buffered.Usage?.AdditionalCounts, streamed.Usage?.AdditionalCounts);
        Assert.Equal(buffered.FinishReason, streamed.FinishReason);
        Assert.Equal(buffered.ModelId, streamed.ModelId);
        Assert.Equal(buffered.AdditionalProperties?[LlmUsageMetadataKeys.Cost], streamed.AdditionalProperties?[LlmUsageMetadataKeys.Cost]);
        Assert.Equal(buffered.AdditionalProperties?[LlmUsageMetadataKeys.CostCurrency], streamed.AdditionalProperties?[LlmUsageMetadataKeys.CostCurrency]);
    }

    [Fact]
    public async Task A_streamed_text_answer_folds_to_the_buffered_response_and_replays_the_same_reasoning()
    {
        var provider = Provider(Answered("The launch notes, drafted."), "The launch ", "notes, ", "drafted.");
        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var (buffered, streamed) = await BothAsync(adapter);

        AssertEquivalent(buffered, streamed);
        Assert.Equal(1, provider.ChatStreamingCallCount);
        Assert.Equal(0, provider.GenerateStreamingCallCount);
        Assert.Equal("weighing the options", (await ReplayedAsync(provider, adapter, buffered)).ReasoningContent);
        Assert.Equal("weighing the options", (await ReplayedAsync(provider, adapter, streamed)).ReasoningContent);
    }

    [Fact]
    public async Task A_streamed_answer_calling_native_tools_folds_to_the_same_calls_and_replays_them()
    {
        const string body = """{"choices":[{"message":{"role":"assistant","content":"Searching.","tool_calls":[{"id":"call_7","type":"function","function":{"name":"web_search","arguments":"{\"query\":\"orkeon\"}"}}]}}]}""";
        var provider = Provider(Answered("Searching.", body), "Search", "ing.");
        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var (buffered, streamed) = await BothAsync(adapter);

        AssertEquivalent(buffered, streamed);
        var call = Assert.Single(streamed.Messages[0].Contents.OfType<FunctionCallContent>());
        Assert.Equal("web_search", call.Name);
        Assert.Equal((await ReplayedAsync(provider, adapter, buffered)).RawToolCalls, (await ReplayedAsync(provider, adapter, streamed)).RawToolCalls);
    }

    [Fact]
    public async Task A_streamed_answer_calling_a_tool_in_the_text_protocol_folds_to_the_same_call()
    {
        const string text = """Looking. [TOOL_CALL]{tool => "web_search", args => {--query "orkeon"}}[/TOOL_CALL]""";
        var body = System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = text } } } });
        var provider = Provider(Answered(text, body), "Looking. ", text["Looking. ".Length..]);
        using var adapter = new LlmProviderToChatClientAdapter(
            provider, textFallbackParser: new TextFallbackToolCallParser(NullLogger<TextFallbackToolCallParser>.Instance));

        var (buffered, streamed) = await BothAsync(adapter);

        AssertEquivalent(buffered, streamed);
        Assert.Equal("web_search", Assert.Single(streamed.Messages[0].Contents.OfType<FunctionCallContent>()).Name);
    }

    [Fact]
    public async Task A_provider_that_does_not_stream_answers_the_streaming_call_with_the_buffered_response()
    {
        var provider = Provider(Answered("The launch notes, drafted."));
        provider.SupportsStreaming = false;
        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var (buffered, streamed) = await BothAsync(adapter);

        AssertEquivalent(buffered, streamed);
        Assert.Equal(0, provider.ChatStreamingCallCount);
        Assert.Equal("weighing the options", (await ReplayedAsync(provider, adapter, streamed)).ReasoningContent);
    }

    [Fact]
    public async Task A_stream_that_carried_no_text_still_folds_to_the_answer()
    {
        // A provider streaming reasoning only, or nothing: the last update carries the text.
        var provider = Provider(Answered("Only in the end."));
        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var (buffered, streamed) = await BothAsync(adapter);

        AssertEquivalent(buffered, streamed);
        Assert.Equal("Only in the end.", streamed.Text);
    }

    [Fact]
    public async Task A_stream_the_provider_refused_fails_like_the_buffered_call()
    {
        var refusal = new LlmResponse { Content = string.Empty, Metadata = new Dictionary<string, object> { ["error"] = "rate limited" } };
        var provider = Provider(refusal);
        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => adapter
            .GetStreamingResponseAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken)
            .ToChatResponseAsync(TestContext.Current.CancellationToken));

        Assert.Equal("rate limited", failure.Message);
    }
}
