using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Replay;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Infrastructure.Tests.LLMs.Replay.CassetteFixtures;

namespace Orkeon.Infrastructure.Tests.LLMs.Replay;

public sealed class ReplayLlmProviderTests
{
    private static async Task<FakeFileSystemService> RecordedAsync(Action<FakeScriptedLlmProvider> arrange, Func<RecordingLlmProvider, Task> calls)
    {
        var fs = NewFileSystem();
        var inner = new FakeScriptedLlmProvider();
        arrange(inner);
        await calls(new RecordingLlmProvider(inner, new LlmCassette(fs, CassettePath), "default"));
        return fs;
    }

    private static (ReplayLlmProvider Provider, FakeScriptedLlmProvider Network) Replaying(FakeFileSystemService fs)
    {
        var network = new FakeScriptedLlmProvider { Response = new LlmResponse { Content = "from the network" } };
        return (new ReplayLlmProvider(network, new LlmCassette(fs, CassettePath), "default"), network);
    }

    [Fact]
    public async Task A_recorded_call_is_answered_from_the_cassette_without_the_network()
    {
        var fs = await RecordedAsync(
            inner => inner.Response = new LlmResponse { Content = "pong", Model = "m1", PromptTokens = 3, CompletionTokens = 1, TokensUsed = 4 },
            p => p.ChatAsync(User("ping"), null, TestContext.Current.CancellationToken));
        var (provider, network) = Replaying(fs);

        var response = await provider.ChatAsync(User("ping"), null, TestContext.Current.CancellationToken);

        Assert.Equal("pong", response.Content);
        Assert.Equal("m1", response.Model);
        Assert.Equal(3, response.PromptTokens);
        Assert.Equal(4, response.TokensUsed);
        Assert.Equal(0, network.Calls);
    }

    [Fact]
    public async Task A_call_the_cassette_does_not_hold_is_a_replay_miss_and_never_reaches_the_network()
    {
        var fs = await RecordedAsync(_ => { }, p => p.ChatAsync(User("ping"), null, TestContext.Current.CancellationToken));
        var (provider, network) = Replaying(fs);

        var miss = await Assert.ThrowsAsync<ReplayMissException>(
            () => provider.ChatAsync(User("something else"), null, TestContext.Current.CancellationToken));

        Assert.Contains("default", miss.Message, StringComparison.Ordinal);
        Assert.Equal(0, network.Calls);
    }

    [Fact]
    public async Task An_absent_cassette_is_a_replay_miss()
    {
        var (provider, network) = Replaying(NewFileSystem());

        var miss = await Assert.ThrowsAsync<ReplayMissException>(
            () => provider.ChatAsync(User("ping"), null, TestContext.Current.CancellationToken));

        Assert.Contains(CassettePath, miss.Message, StringComparison.Ordinal);
        Assert.Equal(0, network.Calls);
    }

    [Fact]
    public async Task The_nth_identical_call_gets_the_nth_recorded_answer_and_a_further_one_misses()
    {
        var n = 0;
        var fs = await RecordedAsync(
            inner => inner.OnCall = () => inner.Response = new LlmResponse { Content = "answer " + n++ },
            async p =>
            {
                await p.ChatAsync(User("same"), null, TestContext.Current.CancellationToken);
                await p.ChatAsync(User("same"), null, TestContext.Current.CancellationToken);
            });
        var (provider, _) = Replaying(fs);

        var first = await provider.ChatAsync(User("same"), null, TestContext.Current.CancellationToken);
        var second = await provider.ChatAsync(User("same"), null, TestContext.Current.CancellationToken);

        Assert.Equal("answer 0", first.Content);
        Assert.Equal("answer 1", second.Content);
        await Assert.ThrowsAsync<ReplayMissException>(
            () => provider.ChatAsync(User("same"), null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_streamed_call_is_replayed_delta_for_delta()
    {
        var fs = await RecordedAsync(
            inner => inner.StreamEvents =
            [
                LlmStreamEvent.Content("po"),
                LlmStreamEvent.Reasoning("hm"),
                LlmStreamEvent.ToolCallFragment(new ToolCallDelta(0, "c1", "lookup", "{\"q\":")),
                LlmStreamEvent.Content("ng"),
                LlmStreamEvent.Complete(new LlmResponse { Content = "pong", RawResponseBody = "{\"choices\":[]}" }),
            ],
            async p =>
            {
                await foreach (var _ in p.ChatStreamingAsync(User("ping"), null, TestContext.Current.CancellationToken))
                {
                }
            });
        var (provider, network) = Replaying(fs);

        var seen = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync(User("ping"), null, TestContext.Current.CancellationToken))
            seen.Add(ev);

        Assert.Equal(
            [LlmStreamEventKind.ContentDelta, LlmStreamEventKind.ReasoningDelta, LlmStreamEventKind.ToolCallDelta, LlmStreamEventKind.ContentDelta, LlmStreamEventKind.Completed],
            seen.Select(e => e.Kind));
        Assert.Equal("po", seen[0].Delta);
        Assert.Equal("hm", seen[1].Delta);
        Assert.Equal(new ToolCallDelta(0, "c1", "lookup", "{\"q\":"), seen[2].ToolCall);
        Assert.Equal("pong", seen[4].FinalResponse!.Content);
        Assert.Equal("{\"choices\":[]}", seen[4].FinalResponse!.RawResponseBody);
        Assert.Equal(0, network.Calls);
    }

    [Fact]
    public async Task A_call_recorded_as_a_stream_answers_a_buffered_call_with_its_final_response()
    {
        var fs = await RecordedAsync(
            inner => inner.StreamEvents = [LlmStreamEvent.Content("pong"), LlmStreamEvent.Complete(new LlmResponse { Content = "pong" })],
            async p =>
            {
                await foreach (var _ in p.ChatStreamingAsync(User("ping"), null, TestContext.Current.CancellationToken))
                {
                }
            });
        var (provider, _) = Replaying(fs);

        var response = await provider.ChatAsync(User("ping"), null, TestContext.Current.CancellationToken);

        Assert.Equal("pong", response.Content);
    }

    [Fact]
    public async Task A_buffered_recording_streams_back_as_one_content_delta_and_the_end()
    {
        var fs = await RecordedAsync(
            inner => inner.Response = new LlmResponse { Content = "pong" },
            p => p.ChatAsync(User("ping"), null, TestContext.Current.CancellationToken));
        var (provider, _) = Replaying(fs);

        var seen = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync(User("ping"), null, TestContext.Current.CancellationToken))
            seen.Add(ev);

        Assert.Equal([LlmStreamEventKind.ContentDelta, LlmStreamEventKind.Completed], seen.Select(e => e.Kind));
        Assert.Equal("pong", seen[1].FinalResponse!.Content);
    }

    [Fact]
    public async Task A_prompt_call_is_replayed_and_a_failed_answer_keeps_its_error()
    {
        var fs = await RecordedAsync(
            inner => inner.Response = FakeScriptedLlmProvider.Failed("boom", "APIError"),
            p => p.GenerateAsync("one prompt", null, TestContext.Current.CancellationToken));
        var (provider, network) = Replaying(fs);

        var response = await provider.GenerateAsync("one prompt", null, TestContext.Current.CancellationToken);

        Assert.Equal("boom", response.Error);
        Assert.Equal("APIError", response.ErrorType);
        Assert.Equal(0, network.Calls);
    }
}
