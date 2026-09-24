using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.CostTracking;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// The one measuring point of LLM usage (STUDIO-42 D-01): every generation call through the
/// metered provider reaches the sink exactly once — buffered or streamed, counted by the
/// provider or estimated, whoever makes it — stamped with the attribution in effect.
/// </summary>
public sealed class MeteredLlmProviderTests
{
    private static LlmResponse Answer(int prompt, int completion, string content = "ok") => new()
    {
        Content = content,
        TokensUsed = prompt + completion,
        PromptTokens = prompt,
        CompletionTokens = completion,
        Model = "vendor/model-x",
    };

    private static LlmResponse Billed(LlmResponse response, double charge) => response with
    {
        Metadata = new Dictionary<string, object>
        {
            [LlmUsageMetadataKeys.Cost] = charge,
            [LlmUsageMetadataKeys.CostCurrency] = "USD",
        },
    };

    private static LlmMessage[] Conversation(string text = "hello") => [LlmMessage.User(text)];

    [Fact]
    public async Task A_chat_call_reaches_the_sink_once_with_what_the_provider_counted()
    {
        var provider = new MockLlmProvider { Name = "vendor" };
        provider.SetChatResult(Answer(120, 30) with { CacheHitTokens = 100, CacheMissTokens = 20 });
        var sink = new MockLlmUsageSink();

        await MeteredLlmProvider.Wrap(provider, sink).ChatAsync(Conversation(), cancellationToken: TestContext.Current.CancellationToken);

        var usage = Assert.Single(sink.Recorded);
        Assert.Equal("vendor", usage.Provider);
        Assert.Equal("vendor/model-x", usage.Model);
        Assert.Equal(120, usage.PromptTokens);
        Assert.Equal(30, usage.CompletionTokens);
        Assert.Equal(100, usage.CacheHitTokens);
        Assert.Equal(20, usage.CacheMissTokens);
        Assert.False(usage.Estimated);
        Assert.Null(usage.Cost);
    }

    [Fact]
    public async Task Every_call_is_counted_once_and_only_once()
    {
        var provider = new MockLlmProvider();
        provider.SetGenerateResult(Answer(10, 1));
        provider.SetChatResult(Answer(20, 2));
        var sink = new MockLlmUsageSink();
        var metered = MeteredLlmProvider.Wrap(provider, sink);
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 3; i++)
            await metered.GenerateAsync("prompt", cancellationToken: ct);
        for (var i = 0; i < 2; i++)
            await metered.ChatAsync(Conversation(), cancellationToken: ct);

        Assert.Equal(provider.GenerateCallCount + provider.ChatCallCount, sink.Recorded.Count);
        Assert.Equal((3 * 11) + (2 * 22), sink.TotalTokens);
    }

    [Fact]
    public async Task A_call_carries_the_attribution_in_effect_when_it_started()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(Answer(1, 1));
        var sink = new MockLlmUsageSink();

        using (LlmUsageScope.Begin(LlmUsageOperations.Agent, crewId: "crew-1", agentId: "Writer", taskId: "task-1"))
            await MeteredLlmProvider.Wrap(provider, sink).ChatAsync(Conversation(), cancellationToken: TestContext.Current.CancellationToken);

        var usage = Assert.Single(sink.Recorded);
        Assert.Equal(LlmUsageOperations.Agent, usage.OperationType);
        Assert.Equal("crew-1", usage.CrewId);
        Assert.Equal("Writer", usage.AgentId);
        Assert.Equal("task-1", usage.TaskId);
    }

    [Fact]
    public async Task A_call_nothing_claimed_is_still_counted_as_unattributed()
    {
        var provider = new MockLlmProvider();
        provider.SetGenerateResult(Answer(5, 5));
        var sink = new MockLlmUsageSink();

        await MeteredLlmProvider.Wrap(provider, sink).GenerateAsync("orphan", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(LlmUsageOperations.Unattributed, Assert.Single(sink.Recorded).OperationType);
    }

    [Fact]
    public async Task A_response_with_no_usage_at_all_is_estimated_and_says_so()
    {
        // Silence is not evidence that nothing was spent: several endpoints answer without a
        // usage block, and the meter used to stand at zero through a whole session.
        var provider = new MockLlmProvider();
        provider.SetGenerateResult(new LlmResponse { Content = "une réponse assez longue pour compter" });
        var sink = new MockLlmUsageSink();

        await MeteredLlmProvider.Wrap(provider, sink).GenerateAsync(
            "dis-moi quelque chose d'assez long pour compter", cancellationToken: TestContext.Current.CancellationToken);

        var usage = Assert.Single(sink.Recorded);
        Assert.True(usage.Estimated);
        Assert.True(usage.PromptTokens > 0);
        Assert.True(usage.CompletionTokens > 0);
        Assert.Null(usage.Cost);
    }

    [Fact]
    public async Task A_silent_chat_is_estimated_from_the_whole_conversation()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(new LlmResponse { Content = "ok" });
        var sink = new MockLlmUsageSink();
        LlmMessage[] conversation = [LlmMessage.System(new string('a', 350)), LlmMessage.User(new string('b', 350))];

        await MeteredLlmProvider.Wrap(provider, sink).ChatAsync(conversation, cancellationToken: TestContext.Current.CancellationToken);

        var usage = Assert.Single(sink.Recorded);
        Assert.True(usage.Estimated);
        // 700 characters at ~3.5 per token, plus the per-message overhead a chat pays.
        Assert.Equal(LlmUsageEstimator.Prompt(conversation), usage.PromptTokens);
        Assert.True(usage.PromptTokens >= 200, $"prompt estimate was {usage.PromptTokens}");
    }

    [Fact]
    public async Task The_zero_a_provider_declares_is_taken_at_its_word()
    {
        // The echo provider calls no model and says so: zero is a count, not a silence.
        var provider = new MockLlmProvider();
        provider.SetGenerateResult(new LlmResponse { Content = "echo", PromptTokens = 0, CompletionTokens = 0 });
        var sink = new MockLlmUsageSink();

        await MeteredLlmProvider.Wrap(provider, sink).GenerateAsync("echo", cancellationToken: TestContext.Current.CancellationToken);

        var usage = Assert.Single(sink.Recorded);
        Assert.False(usage.Estimated);
        Assert.Equal(0, usage.PromptTokens + usage.CompletionTokens);
    }

    [Fact]
    public async Task A_total_only_response_lands_entirely_on_completion()
    {
        var provider = new MockLlmProvider();
        provider.SetGenerateResult(new LlmResponse { Content = "ok", TokensUsed = 42 });
        var sink = new MockLlmUsageSink();

        await MeteredLlmProvider.Wrap(provider, sink).GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        var usage = Assert.Single(sink.Recorded);
        Assert.Equal(0, usage.PromptTokens);
        Assert.Equal(42, usage.CompletionTokens);
    }

    [Fact]
    public async Task A_call_that_failed_before_any_answer_is_not_counted()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(new LlmResponse
        {
            Metadata = new Dictionary<string, object> { [LlmResponseMetadataKeys.Error] = "timeout after 30 s" },
        });
        var sink = new MockLlmUsageSink();

        var response = await MeteredLlmProvider.Wrap(provider, sink).ChatAsync(Conversation(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("timeout after 30 s", response.Error);
        Assert.Empty(sink.Recorded);
    }

    [Theory]
    [InlineData(0.0021)]
    [InlineData(0.0)]
    public async Task The_vendor_charge_reaches_the_sink_as_billed(double charge)
    {
        // A free call bills 0 — a price, not the silence of a vendor that bills nothing.
        var provider = new MockLlmProvider();
        provider.SetChatResult(Billed(Answer(100, 20), charge));
        var sink = new MockLlmUsageSink();

        await MeteredLlmProvider.Wrap(provider, sink).ChatAsync(Conversation(), cancellationToken: TestContext.Current.CancellationToken);

        var usage = Assert.Single(sink.Recorded);
        Assert.Equal((decimal)charge, usage.Cost);
        Assert.Equal("USD", usage.CostCurrency);
    }

    [Fact]
    public async Task A_call_the_vendor_did_not_bill_carries_an_unknown_cost()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(Answer(100, 20));
        var sink = new MockLlmUsageSink();

        await MeteredLlmProvider.Wrap(provider, sink).ChatAsync(Conversation(), cancellationToken: TestContext.Current.CancellationToken);

        var usage = Assert.Single(sink.Recorded);
        Assert.Null(usage.Cost);
        Assert.Null(usage.CostCurrency);
    }

    [Fact]
    public async Task A_throwing_sink_never_fails_the_call()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(Answer(1, 1, "still answered"));

        var response = await MeteredLlmProvider.Wrap(provider, new ThrowingUsageSink())
            .ChatAsync(Conversation(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("still answered", response.Content);
    }

    [Fact]
    public async Task A_chat_stream_is_counted_once_with_the_usage_of_its_final_response()
    {
        var provider = new MockStreamingLlmProvider();
        provider.SetStreamingChunks(["Hel", "lo"]);
        provider.SetCompletedResponse(Answer(200, 30, "Hello"));
        var sink = new MockLlmUsageSink();
        var metered = (IStreamingLlmProvider)MeteredLlmProvider.Wrap(provider, sink);

        var events = new List<LlmStreamEvent>();
        await foreach (var streamed in metered.ChatStreamingAsync(Conversation(), cancellationToken: TestContext.Current.CancellationToken))
        {
            // The meter moves with the answer, before the consumer sees the end of the stream.
            if (streamed.Kind == LlmStreamEventKind.Completed)
                Assert.Single(sink.Recorded);
            events.Add(streamed);
        }

        Assert.Equal(3, events.Count);
        var usage = Assert.Single(sink.Recorded);
        Assert.Equal(200, usage.PromptTokens);
        Assert.Equal(30, usage.CompletionTokens);
        Assert.False(usage.Estimated);
    }

    [Fact]
    public async Task A_chat_stream_without_usage_is_estimated_from_the_conversation_and_the_answer()
    {
        var provider = new MockStreamingLlmProvider();
        provider.SetStreamingChunks(["a streamed answer"]);
        var sink = new MockLlmUsageSink();
        var metered = (IStreamingLlmProvider)MeteredLlmProvider.Wrap(provider, sink);
        var conversation = Conversation("a question long enough to be counted");

        await foreach (var _ in metered.ChatStreamingAsync(conversation, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        var usage = Assert.Single(sink.Recorded);
        Assert.True(usage.Estimated);
        Assert.Equal(LlmUsageEstimator.Prompt(conversation), usage.PromptTokens);
        Assert.True(usage.CompletionTokens > 0);
        Assert.Null(usage.Cost);
    }

    [Fact]
    public async Task A_text_stream_is_counted_once_at_its_end_as_an_estimate_never_a_cost()
    {
        // D-06: the text-only stream has no usage channel at all. It is counted when it ends,
        // over what was sent and what came back — marked, and never priced.
        var provider = new MockStreamingLlmProvider { Name = "vendor" };
        provider.SetStreamingChunks(["Hello ", "world, ", "this is streamed"]);
        var sink = new MockLlmUsageSink();
        var metered = (IStreamingLlmProvider)MeteredLlmProvider.Wrap(provider, sink);

        var received = new List<string>();
        await foreach (var chunk in metered.GenerateStreamingAsync("stream me", LlmConfig.Create("model-y"), TestContext.Current.CancellationToken))
        {
            Assert.Empty(sink.Recorded);
            received.Add(chunk);
        }

        Assert.Equal(3, received.Count);
        var usage = Assert.Single(sink.Recorded);
        Assert.True(usage.Estimated);
        Assert.Equal(LlmUsageEstimator.Prompt("stream me"), usage.PromptTokens);
        Assert.Equal(LlmUsageEstimator.FromCharacterCount(string.Concat(received).Length), usage.CompletionTokens);
        Assert.Equal("model-y", usage.Model);
        Assert.Equal("vendor", usage.Provider);
        Assert.Null(usage.Cost);
    }

    [Fact]
    public async Task A_stream_abandoned_early_counts_what_it_had_streamed()
    {
        var provider = new MockStreamingLlmProvider();
        provider.SetStreamingChunks(["first chunk", "second chunk"]);
        var sink = new MockLlmUsageSink();
        var metered = (IStreamingLlmProvider)MeteredLlmProvider.Wrap(provider, sink);

        await foreach (var _ in metered.ChatStreamingAsync(Conversation(), cancellationToken: TestContext.Current.CancellationToken))
            break;

        var usage = Assert.Single(sink.Recorded);
        Assert.True(usage.Estimated);
        Assert.Equal(LlmUsageEstimator.FromCharacterCount("first chunk".Length), usage.CompletionTokens);
    }

    [Fact]
    public async Task A_text_stream_that_ends_with_nothing_still_counts_what_was_sent()
    {
        // The model answered nothing, but it read the prompt: the call happened.
        var provider = new MockStreamingLlmProvider();
        provider.SetStreamingChunks([]);
        var sink = new MockLlmUsageSink();
        var metered = (IStreamingLlmProvider)MeteredLlmProvider.Wrap(provider, sink);

        await foreach (var _ in metered.GenerateStreamingAsync("a prompt worth counting", cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        var usage = Assert.Single(sink.Recorded);
        Assert.True(usage.Estimated);
        Assert.Equal(LlmUsageEstimator.Prompt("a prompt worth counting"), usage.PromptTokens);
        Assert.Equal(0, usage.CompletionTokens);
    }

    [Fact]
    public async Task A_refused_stream_that_delivered_nothing_is_not_counted()
    {
        var sink = new MockLlmUsageSink();
        var metered = (IStreamingLlmProvider)MeteredLlmProvider.Wrap(new RefusingStreamingProvider(), sink);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in metered.ChatStreamingAsync(Conversation(), cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Empty(sink.Recorded);
    }

    [Fact]
    public async Task Streaming_through_a_provider_that_cannot_stream_is_counted_once()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(Answer(40, 4, "buffered"));
        provider.SetGenerateResult(Answer(30, 3, "buffered too"));
        var sink = new MockLlmUsageSink();
        var metered = (IStreamingLlmProvider)MeteredLlmProvider.Wrap(provider, sink);
        var ct = TestContext.Current.CancellationToken;

        var events = new List<LlmStreamEvent>();
        await foreach (var streamed in metered.ChatStreamingAsync(Conversation(), cancellationToken: ct))
            events.Add(streamed);
        var chunks = new List<string>();
        await foreach (var chunk in metered.GenerateStreamingAsync("prompt", cancellationToken: ct))
            chunks.Add(chunk);

        Assert.False(metered.SupportsStreaming);
        Assert.Equal([LlmStreamEventKind.ContentDelta, LlmStreamEventKind.Completed], events.Select(e => e.Kind));
        Assert.Equal(["buffered too"], chunks);
        Assert.Equal([44, 33], sink.Recorded.Select(u => u.PromptTokens + u.CompletionTokens));
        Assert.All(sink.Recorded, u => Assert.False(u.Estimated));
    }

    [Fact]
    public async Task A_stream_keeps_the_attribution_it_started_with()
    {
        // An async iterator resumes from a yield in its consumer's context: the attribution is
        // read when the stream starts, not wherever the consumer happens to be at its end.
        var provider = new MockStreamingLlmProvider();
        provider.SetStreamingChunks(["one", "two"]);
        var sink = new MockLlmUsageSink();
        var metered = (IStreamingLlmProvider)MeteredLlmProvider.Wrap(provider, sink);

        var stream = metered.GenerateStreamingAsync("p", cancellationToken: TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await using (stream)
        {
            using (LlmUsageScope.Begin("stream", agentId: "Scripted"))
                Assert.True(await stream.MoveNextAsync());

            while (await stream.MoveNextAsync())
            {
            }
        }

        var usage = Assert.Single(sink.Recorded);
        Assert.Equal("stream", usage.OperationType);
        Assert.Equal("Scripted", usage.AgentId);
    }

    [Fact]
    public void Without_a_sink_the_provider_is_left_as_it_is()
    {
        var provider = new MockLlmProvider();

        Assert.Same(provider, MeteredLlmProvider.Wrap(provider, sink: null));
    }

    [Fact]
    public async Task A_metered_provider_is_never_metered_twice()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(Answer(10, 10));
        var sink = new MockLlmUsageSink();

        var once = MeteredLlmProvider.Wrap(provider, sink);
        var twice = MeteredLlmProvider.Wrap(once, sink);
        await twice.ChatAsync(Conversation(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(once, twice);
        Assert.Single(sink.Recorded);
    }

    [Fact]
    public void The_metered_provider_speaks_for_the_one_it_wraps()
    {
        var capabilities = new LlmProviderCapabilities { ResponseFormat = ResponseFormatSupport.JsonObject, Vision = true };
        var provider = new MockStreamingLlmProvider { Name = "vendor", SupportsStreaming = true };
        var plain = new MockLlmProvider { Name = "plain", BaseConfig = LlmConfig.Create("model-z"), Capabilities = capabilities };
        var sink = new MockLlmUsageSink();

        var meteredStreaming = MeteredLlmProvider.Wrap(provider, sink);
        var meteredPlain = MeteredLlmProvider.Wrap(plain, sink);

        Assert.Equal("vendor", meteredStreaming.Name);
        Assert.True(((IStreamingLlmProvider)meteredStreaming).SupportsStreaming);
        Assert.Same(provider, MeteredLlmProvider.Unwrap(meteredStreaming));
        Assert.Equal("model-z", meteredPlain.BaseConfig?.Model);
        Assert.Same(capabilities, meteredPlain.Capabilities);
        Assert.Same(plain, MeteredLlmProvider.Unwrap(plain));
    }

    private sealed class ThrowingUsageSink : ILlmUsageSink
    {
        public void Record(CostUsageEvent usage) => throw new InvalidOperationException("sink down");
    }

    /// <summary>A vendor that refuses the streamed request before sending a single delta.</summary>
    private sealed class RefusingStreamingProvider : ILlmProvider, IStreamingLlmProvider
    {
        private readonly bool _refuses = true;

        public string Name => "refusing";

        public bool SupportsStreaming => true;

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<string> GenerateStreamingAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
            LlmMessage[] messages, LlmConfig? config = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (_refuses)
                throw new HttpRequestException("401 Unauthorized: invalid API key");
            yield return LlmStreamEvent.Content("never sent");
        }
    }
}
