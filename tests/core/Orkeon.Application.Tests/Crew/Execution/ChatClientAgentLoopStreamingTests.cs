using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using Microsoft.Extensions.AI;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.Security;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Constants.Protocol;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// GAP-32: the chat-client loop streams a turn when someone reads it — a streamed run
/// (<see cref="CrewStreamScope"/>) or a host's <see cref="ILlmDeltaSink"/> (<c>orkeon run --stream</c>,
/// the REPL's console) —, hands each fragment to them as it arrives, and folds the updates into the
/// turn it then reads like a buffered one. Without either, the call stays buffered: the chat client
/// below refuses to stream unless it is asked to.
/// </summary>
public class ChatClientAgentLoopStreamingTests
{
    /// <summary>A chat client that streams each scripted turn's updates, and refuses buffered calls.</summary>
    private sealed class StreamingChatClient : IChatClient
    {
        private readonly Queue<ChatResponseUpdate[]> _turns = new();

        public int StreamedCalls { get; private set; }

        /// <summary>The failure every turn ends with after its updates, when the stream fails (LLM-12).</summary>
        public Func<Exception>? FailureAfterUpdates { get; set; }

        public void Enqueue(params ChatResponseUpdate[] updates) => _turns.Enqueue(updates);

        public System.Threading.Tasks.Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A turn someone reads is streamed.");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamedCalls++;
            foreach (var update in _turns.Count > 0 ? _turns.Dequeue() : [new ChatResponseUpdate(ChatRole.Assistant, "default")])
            {
                await System.Threading.Tasks.Task.Yield();
                yield return update;
            }

            if (FailureAfterUpdates is { } failure)
                throw failure();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>A chat client whose buffered answer is the fold of the stream it reads underneath.</summary>
    private sealed class FoldingChatClient : IChatClient
    {
        private readonly Queue<ChatResponseUpdate[]> _turns = new();

        public int BufferedCalls { get; private set; }

        public void Enqueue(params ChatResponseUpdate[] updates) => _turns.Enqueue(updates);

        public async System.Threading.Tasks.Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            BufferedCalls++;
            return await Underneath().ToChatResponseAsync(cancellationToken);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The loop reads nobody: its call stays buffered.");

        private async IAsyncEnumerable<ChatResponseUpdate> Underneath()
        {
            foreach (var update in _turns.Count > 0 ? _turns.Dequeue() : [new ChatResponseUpdate(ChatRole.Assistant, "default")])
            {
                await System.Threading.Tasks.Task.Yield();
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingSink : ILlmDeltaSink
    {
        public List<string> Calls { get; } = [];

        public void OnDelta(string delta) => Calls.Add(delta);

        public void OnTurnCompleted() => Calls.Add("<turn>");
    }

    /// <summary>A host's sink that gives each turn its own recorder (#211), as the console sink does.</summary>
    private sealed class TurnOpeningSink : ILlmDeltaSink
    {
        public List<string?> Labels { get; } = [];
        public List<RecordingSink> Turns { get; } = [];
        public List<string> DirectCalls { get; } = [];

        public ILlmDeltaSink BeginTurn(string? label)
        {
            Labels.Add(label);
            var turn = new RecordingSink();
            Turns.Add(turn);
            return turn;
        }

        public void OnDelta(string delta) => DirectCalls.Add(delta);

        public void OnTurnCompleted() => DirectCalls.Add("<turn>");
    }

    private static DomainTask BuildTask() =>
        DomainTask.Create(TaskDescription.From("Chat loop task"), ExpectedOutput.From("An answer"));

    private static ChatClientAgentLoop BuildLoop(IChatClient client, ILlmDeltaSink? sink)
    {
        var logger = new SpyExecutionLogger();
        var gate = new LlmCallGate(logger, new ScriptedBasicLlmProvider());
        var composer = new ChatOptionsComposer(logger, [], new FakeFileSystemService(), ToolInvocationPipeline.Unguarded);
        return new ChatClientAgentLoop(logger, client, gate, composer, new ChatToolDispatcher(logger, ToolInvocationPipeline.Unguarded), sink);
    }

    private static System.Threading.Tasks.Task<AgentLoopResult> RunAsync(ChatClientAgentLoop loop) =>
        loop.ExecuteAsync(
            new AgentBuilder().Role("Writer").Goal("Write").MaxIterations(4).Build(),
            BuildTask(), "system", "user", [], TestContext.Current.CancellationToken);

    [Fact]
    public async System.Threading.Tasks.Task A_host_sink_receives_each_fragment_then_the_turns_end_and_the_turn_is_the_fragments()
    {
        using var client = new StreamingChatClient();
        client.Enqueue(new ChatResponseUpdate(ChatRole.Assistant, "Hel"), new ChatResponseUpdate(ChatRole.Assistant, "lo."));
        var sink = new RecordingSink();

        var result = await RunAsync(BuildLoop(client, sink));

        Assert.Equal(AgentExitReason.Completed, result.ExitReason);
        Assert.Equal("Hello.", result.Output);
        Assert.Equal(["Hel", "lo.", "<turn>"], sink.Calls);
        Assert.Equal(1, client.StreamedCalls);
    }

    [Fact]
    public async System.Threading.Tasks.Task Each_call_streams_through_the_turn_the_host_sink_opens_for_it()
    {
        using var client = new StreamingChatClient();
        client.Enqueue(new ChatResponseUpdate(ChatRole.Assistant, "Hel"), new ChatResponseUpdate(ChatRole.Assistant, "lo."));
        var sink = new TurnOpeningSink();

        var result = await RunAsync(BuildLoop(client, sink));

        Assert.Equal("Hello.", result.Output);
        Assert.Empty(sink.DirectCalls);
        var turn = Assert.Single(sink.Turns);
        Assert.Equal(["Hel", "lo.", "<turn>"], turn.Calls);
        Assert.Single(sink.Labels);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_turn_that_only_calls_a_tool_leaves_the_sink_untouched()
    {
        using var client = new StreamingChatClient();
        client.Enqueue(new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("c1", "missing_tool")]));
        client.Enqueue(new ChatResponseUpdate(ChatRole.Assistant, "Done."));
        var sink = new RecordingSink();

        var result = await RunAsync(BuildLoop(client, sink));

        Assert.Equal("Done.", result.Output);
        Assert.Equal(["Done.", "<turn>"], sink.Calls);
        Assert.Equal(2, client.StreamedCalls);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_streamed_run_hears_each_fragment_with_the_task_and_the_agent_of_the_call()
    {
        using var client = new StreamingChatClient();
        client.Enqueue(new ChatResponseUpdate(ChatRole.Assistant, "Draft "), new ChatResponseUpdate(ChatRole.Assistant, "one."));
        var channel = Channel.CreateUnbounded<CrewExecutionEvent>();

        AgentLoopResult result;
        using (CrewStreamScope.Begin(channel.Writer))
        using (LlmUsageScope.Begin(LlmUsageOperations.Agent, crewId: "crew-1", agentId: "Writer", taskId: "task-1"))
            result = await RunAsync(BuildLoop(client, sink: null));

        channel.Writer.Complete();
        var events = new List<CrewExecutionEvent>();
        await foreach (var executionEvent in channel.Reader.ReadAllAsync(TestContext.Current.CancellationToken))
            events.Add(executionEvent);
        Assert.Equal("Draft one.", result.Output);
        Assert.Equal(["Draft ", "one."], events.Select(e => e.Text));
        Assert.All(events, e =>
        {
            Assert.Equal(RunEventKinds.LlmDelta, e.Kind);
            Assert.Equal("task-1", e.TaskId);
            Assert.Equal("Writer", e.AgentRole);
        });
    }

    [Fact]
    public async System.Threading.Tasks.Task A_streamed_turn_whose_stream_fails_mid_answer_fails_the_task_with_the_providers_reason()
    {
        // LLM-12: Llm:TimeoutSeconds elapsed after the first fragments of a Studio run. The
        // adapter fails the enumeration; the loop exits LlmCallFailed with that sentence and
        // never retries without tools — the fragments already streamed are not an answer.
        const string reason = "Z.AI did not answer within Llm:TimeoutSeconds = 30 s: the HTTP timeout elapsed";
        using var client = new StreamingChatClient { FailureAfterUpdates = () => new HttpRequestException(reason) };
        client.Enqueue(new ChatResponseUpdate(ChatRole.Assistant, "Hel"));
        var sink = new RecordingSink();

        var result = await RunAsync(BuildLoop(client, sink));

        Assert.Equal(AgentExitReason.LlmCallFailed, result.ExitReason);
        Assert.Equal(reason, result.LastError);
        Assert.Equal(string.Empty, result.Output);
        Assert.Equal(1, client.StreamedCalls);   // no tool-free retry
        Assert.Equal(["Hel"], sink.Calls);       // what was streamed, no turn end
    }

    // Pinned (green by construction): the turn does not change when the provider streams underneath.
    [Fact]
    [Trait("BR", "BR-11")]
    public async System.Threading.Tasks.Task ShouldRunTheAgentTurnUnchanged_WhenTheProviderStreamsUnderneath()
    {
        using var client = new FoldingChatClient();
        client.Enqueue(new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("c1", "missing_tool")]));
        client.Enqueue(new ChatResponseUpdate(ChatRole.Assistant, "Do"), new ChatResponseUpdate(ChatRole.Assistant, "ne."));

        var result = await RunAsync(BuildLoop(client, sink: null));

        Assert.Equal(AgentExitReason.Completed, result.ExitReason);
        Assert.Equal("Done.", result.Output);
        Assert.Equal(2, client.BufferedCalls);
    }
}
