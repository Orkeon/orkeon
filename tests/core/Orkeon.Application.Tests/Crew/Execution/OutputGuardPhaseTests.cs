using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Agent;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Application.Tests.Services;
using Orkeon.Constants.Protocol;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// FWK-19: what the guardian's output phase keeps from reaching the outside. A blocked output is not
/// stored in memory, does not reach <c>task.completed</c> and is not streamed as <c>llm.delta</c>; an
/// allowed one is streamed in full, in order, once it is judged.
/// </summary>
public class OutputGuardPhaseTests
{
    private static readonly string[] Fragments = ["The ", "secret ", "plan ", "is X."];

    private sealed class StreamingChatClient : IChatClient
    {
        public System.Threading.Tasks.Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, string.Concat(Fragments))]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var fragment in Fragments)
            {
                await System.Threading.Tasks.Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, fragment);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static readonly StreamingChatClient Client = new();

    private sealed class RecordingSink : ILlmDeltaSink
    {
        public List<string> Calls { get; } = [];

        public void OnDelta(string delta) => Calls.Add(delta);

        public void OnTurnCompleted() => Calls.Add("<turn>");
    }

    private static FakeGuardianPipeline Guardian(bool blocking) => new(context =>
        blocking && context.Phase == GuardPhase.Output
            ? GuardResult.Block("output refused by the policy",
                [new GuardViolation("FakeGuard", GuardPhase.Output, "policy", GuardThreatSeverity.High, DateTime.UtcNow)])
            : GuardResult.Allow())
    {
        Guarded = { GuardPhase.Output },
    };

    private static DomainAgent BuildAgent() => new AgentBuilder().Role("Researcher").Goal("Research").MaxIterations(5).Build();

    private static DomainTask BuildTask() =>
        DomainTask.Create(TaskDescription.From("Summarise the page"), ExpectedOutput.From("A summary"));

    private static SimpleExecutionContext BuildContext() =>
        new(CrewId.From(Guid.NewGuid()), [], NullMemoryScope.Instance, []);

    private static ExecutionOrchestrator Orchestrator(IGuardianPipeline guardian, ILlmDeltaSink? sink) =>
        new(NullLogger<ExecutionOrchestrator>.Instance, new ScriptedBasicLlmProvider(), Client, [], new FakeFileSystemService())
        {
            Guardian = guardian,
            DeltaSink = sink,
        };

    private static async System.Threading.Tasks.Task<(TaskResult Result, List<string> Deltas)> RunStreamedAsync(
        ExecutionOrchestrator orchestrator)
    {
        var channel = Channel.CreateUnbounded<CrewExecutionEvent>();
        TaskResult result;
        using (CrewStreamScope.Begin(channel.Writer))
        {
            result = await orchestrator.ExecuteTaskCoreAsync(BuildAgent(), BuildTask(), BuildContext(), TestContext.Current.CancellationToken);
        }

        channel.Writer.Complete();
        var deltas = new List<string>();
        await foreach (var ev in channel.Reader.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            if (ev.Kind == RunEventKinds.LlmDelta)
                deltas.Add(ev.Text!);
        }

        return (result, deltas);
    }

    [Fact]
    public async System.Threading.Tasks.Task ABlockedOutput_EmitsNoLlmDelta_AndRendersNothing()
    {
        var sink = new RecordingSink();

        var (result, deltas) = await RunStreamedAsync(Orchestrator(Guardian(blocking: true), sink));

        Assert.False(result.Success);
        Assert.Empty(deltas);
        Assert.Empty(sink.Calls);
    }

    [Fact]
    public async System.Threading.Tasks.Task AnAllowedOutput_EmitsEveryDelta_InOrder_AfterTheJudgement()
    {
        var sink = new RecordingSink();

        var (result, deltas) = await RunStreamedAsync(Orchestrator(Guardian(blocking: false), sink));

        Assert.True(result.Success, result.Error);
        Assert.Equal(Fragments, deltas);
        Assert.Equal([.. Fragments, "<turn>"], sink.Calls);
    }

    [Fact]
    public async System.Threading.Tasks.Task WithoutAGuardedOutputPhase_TheDeltasStreamAsTheyArrive()
    {
        var sink = new RecordingSink();
        var guardian = new FakeGuardianPipeline();

        var (_, deltas) = await RunStreamedAsync(Orchestrator(guardian, sink));

        Assert.Equal(Fragments, deltas);
        Assert.Equal([.. Fragments, "<turn>"], sink.Calls);
    }

    [Fact]
    public async System.Threading.Tasks.Task ABlockedOutput_IsNeitherStoredInMemory_NorHandedToTaskCompleted()
    {
        var orchestrator = Orchestrator(Guardian(blocking: true), sink: null);
        var memory = new TestMemoryCoordinator();
        var callbacks = new TestCallbackOrchestrator();
        var service = new AgentExecutionService(NullLogger<AgentExecutionService>.Instance, orchestrator, callbacks, memory);
        var task = BuildTask();

        var result = await service.ExecuteTaskAsync(BuildAgent(), task, BuildContext(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(memory.TaskResultStored);
        Assert.Null(memory.LastStoredResult);
        Assert.True(callbacks.TaskCompletedCalled);
        Assert.False(callbacks.LastResult!.Success);
        Assert.Equal(string.Empty, callbacks.LastResult.Output);
        Assert.Contains("Blocked by Guardian (output)", callbacks.LastResult.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task AnAllowedOutput_IsStoredInMemory()
    {
        var orchestrator = Orchestrator(Guardian(blocking: false), sink: null);
        var memory = new TestMemoryCoordinator();
        var service = new AgentExecutionService(
            NullLogger<AgentExecutionService>.Instance, orchestrator, new TestCallbackOrchestrator(), memory);

        var result = await service.ExecuteTaskAsync(BuildAgent(), BuildTask(), BuildContext(), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.True(memory.TaskResultStored);
        Assert.Equal("The secret plan is X.", memory.LastStoredResult);
    }
}
