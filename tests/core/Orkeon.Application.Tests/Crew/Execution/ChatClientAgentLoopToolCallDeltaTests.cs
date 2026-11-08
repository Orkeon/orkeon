using System.Threading.Channels;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using Microsoft.Extensions.AI;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.Security;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Constants.Protocol;
using Orkeon.Domain.Agent;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// FWK-20: a streamed turn hands the fragments of the tool call the model builds to the host's sink and to the run's
/// stream; with a DLP interceptor the arguments are held per call and written once, redacted; the guardian's hold
/// keeps them back like the text, and the fold of the turn does not change.
/// </summary>
public sealed class ChatClientAgentLoopToolCallDeltaTests
{
    private sealed class RecordingSink : ILlmDeltaSink
    {
        public List<string> Calls { get; } = [];

        public void OnDelta(string delta) => Calls.Add("text:" + delta);

        public void OnToolCallDelta(ToolCallDelta toolCall) =>
            Calls.Add($"tool|{toolCall.Index}|{toolCall.Id}|{toolCall.Name}|{toolCall.ArgumentsDelta}");

        public void OnTurnCompleted() => Calls.Add("<turn>");
    }

    private static ChatResponseUpdate Fragment(int index, string? id, string? name, string arguments) =>
        new(ChatRole.Assistant, [])
        {
            RawRepresentation = new ToolCallDelta(index, id, name, arguments),
        };

    private static FakeStreamingChatClient SecretSplitAcrossTwoDeltas()
    {
        var client = new FakeStreamingChatClient();
        client.Enqueue(
            Fragment(0, "call_1", "send_mail", ""),
            Fragment(0, null, null, "{\"key\":\"sk-SEC"),
            Fragment(0, null, null, "RET-123\"}"),
            new ChatResponseUpdate(ChatRole.Assistant, "Done."));
        return client;
    }

    private static ChatClientAgentLoop BuildLoop(IChatClient client, ILlmDeltaSink? sink, ToolCallDeltaDlp? dlp = null)
    {
        var logger = new SpyExecutionLogger();
        var gate = new LlmCallGate(logger, new ScriptedBasicLlmProvider());
        var composer = new ChatOptionsComposer(logger, [], new FakeFileSystemService(), ToolInvocationPipeline.Unguarded);
        return new ChatClientAgentLoop(
            logger, client, gate, composer, new ChatToolDispatcher(logger, ToolInvocationPipeline.Unguarded), sink, toolCallDlp: dlp);
    }

    private static System.Threading.Tasks.Task<AgentLoopResult> RunAsync(ChatClientAgentLoop loop) =>
        loop.ExecuteAsync(
            new AgentBuilder().Role("Writer").Goal("Write").MaxIterations(4).Build(),
            DomainTask.Create(TaskDescription.From("Chat loop task"), ExpectedOutput.From("An answer")),
            "system", "user", [], TestContext.Current.CancellationToken);

    private static ToolCallDeltaDlp Dlp(FakeDlpInterceptor interceptor) =>
        new(interceptor, new DlpPolicy { Channel = DlpChannel.ExternalOutput, DefaultAction = DlpAction.Mask });

    [Fact]
    [Trait("BR", "FWK-20")]
    public async System.Threading.Tasks.Task ShouldHandEachFragmentToTheSinkAsItArrives_WhenNoInterceptorIsConfigured()
    {
        using var client = SecretSplitAcrossTwoDeltas();
        var sink = new RecordingSink();

        var result = await RunAsync(BuildLoop(client, sink));

        Assert.Equal("Done.", result.Output);
        Assert.Equal(AgentExitReason.Completed, result.ExitReason);
        Assert.Equal(
            [
                "tool|0|call_1|send_mail|",
                "tool|0|||{\"key\":\"sk-SEC",
                "tool|0|||RET-123\"}",
                "text:Done.",
                "<turn>",
            ],
            sink.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public async System.Threading.Tasks.Task ShouldWriteTheFragmentsToTheRunsStream_WithTheTaskAndTheAgent()
    {
        using var client = SecretSplitAcrossTwoDeltas();
        var channel = Channel.CreateUnbounded<CrewExecutionEvent>();

        using (CrewStreamScope.Begin(channel.Writer))
        using (LlmUsageScope.Begin(LlmUsageOperations.Agent, crewId: "crew-1", agentId: "Writer", taskId: "task-1"))
            await RunAsync(BuildLoop(client, sink: null));

        channel.Writer.Complete();
        var events = new List<CrewExecutionEvent>();
        await foreach (var e in channel.Reader.ReadAllAsync(TestContext.Current.CancellationToken))
            events.Add(e);

        var toolDeltas = events.Where(e => e.ToolCall is not null).ToList();
        Assert.Equal(3, toolDeltas.Count);
        Assert.All(toolDeltas, e =>
        {
            Assert.Equal(RunEventKinds.LlmDelta, e.Kind);
            Assert.Null(e.Text);
            Assert.Equal("task-1", e.TaskId);
            Assert.Equal("Writer", e.AgentRole);
        });
        Assert.Equal("{\"key\":\"sk-SEC", toolDeltas[1].ToolCall!.ArgumentsDelta);
        Assert.Equal("Done.", Assert.Single(events, e => e.Text is not null).Text);
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public async System.Threading.Tasks.Task ShouldRedactASecretSplitBetweenTwoDeltas_WhenAnInterceptorIsConfigured()
    {
        using var client = SecretSplitAcrossTwoDeltas();
        var sink = new RecordingSink();
        var interceptor = new FakeDlpInterceptor();

        await RunAsync(BuildLoop(client, sink, Dlp(interceptor)));

        // One scan of the whole arguments, one delta carrying the redacted text, and nothing of the secret anywhere.
        Assert.Equal(["{\"key\":\"sk-SECRET-123\"}"], interceptor.Scanned);
        var toolCalls = sink.Calls.Where(c => c.StartsWith("tool|", StringComparison.Ordinal)).ToList();
        Assert.Equal(["tool|0|call_1|send_mail|{\"key\":\"" + FakeDlpInterceptor.Mask + "\"}"], toolCalls);
        Assert.DoesNotContain(sink.Calls, c => c.Contains("SEC", StringComparison.Ordinal) && !c.Contains(FakeDlpInterceptor.Mask, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public async System.Threading.Tasks.Task ShouldEmitNothingOfTheCallBeforeItEnds_WhenAnInterceptorIsConfigured()
    {
        using var client = new FakeStreamingChatClient();
        client.Enqueue(Fragment(1, "call_x", "tool_x", "{\"a\":1}"), Fragment(0, "call_y", "tool_y", "{}"), new ChatResponseUpdate(ChatRole.Assistant, "ok"));
        var sink = new RecordingSink();

        await RunAsync(BuildLoop(client, sink, Dlp(new FakeDlpInterceptor())));

        // The text streams as it comes; each call comes out whole, in index order, before the turn ends.
        Assert.Equal(["text:ok", "tool|0|call_y|tool_y|{}", "tool|1|call_x|tool_x|{\"a\":1}", "<turn>"], sink.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public async System.Threading.Tasks.Task ShouldDropTheHeldArguments_WhenTheStreamFailsBeforeTheCallEnds()
    {
        using var client = SecretSplitAcrossTwoDeltas();
        client.FailureAfterUpdates = () => new HttpRequestException("cut");
        var sink = new RecordingSink();

        var result = await RunAsync(BuildLoop(client, sink, Dlp(new FakeDlpInterceptor())));

        Assert.Equal(AgentExitReason.LlmCallFailed, result.ExitReason);
        Assert.DoesNotContain(sink.Calls, c => c.StartsWith("tool|", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public async System.Threading.Tasks.Task ShouldHoldTheFragmentsLikeTheText_WhileTheOutputPhaseIsGuarded()
    {
        using var client = SecretSplitAcrossTwoDeltas();
        var sink = new RecordingSink();

        using (OutputHold.Begin(out var hold))
        {
            await RunAsync(BuildLoop(client, sink));

            Assert.Empty(sink.Calls);
            hold.Release();
        }

        Assert.Equal(
            ["tool|0|call_1|send_mail|", "tool|0|||{\"key\":\"sk-SEC", "tool|0|||RET-123\"}", "text:Done.", "<turn>"],
            sink.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public async System.Threading.Tasks.Task ShouldNeverLetTheFragmentsOut_WhenTheGuardianBlocksTheOutput()
    {
        using var client = SecretSplitAcrossTwoDeltas();
        var sink = new RecordingSink();
        var channel = Channel.CreateUnbounded<CrewExecutionEvent>();

        using (CrewStreamScope.Begin(channel.Writer))
        using (OutputHold.Begin(out var hold))
        {
            await RunAsync(BuildLoop(client, sink, Dlp(new FakeDlpInterceptor())));
            hold.Drop();
        }

        channel.Writer.Complete();
        var events = new List<CrewExecutionEvent>();
        await foreach (var e in channel.Reader.ReadAllAsync(TestContext.Current.CancellationToken))
            events.Add(e);
        Assert.Empty(sink.Calls);
        Assert.Empty(events);
    }
}
