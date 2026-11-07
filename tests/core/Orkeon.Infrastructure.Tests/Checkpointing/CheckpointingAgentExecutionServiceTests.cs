using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.Checkpointing;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Infrastructure.Orchestration;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Checkpointing;

/// <summary>
/// FWK-07: the decorator every strategy's execution service is wrapped in checkpoints each task as
/// it completes, under the stable key <c>"{TaskKey}#{n}"</c>, and answers a resumed run's completed
/// tasks from the checkpoint without asking the model.
/// </summary>
public sealed class CheckpointingAgentExecutionServiceTests : IDisposable
{
    private readonly InMemoryStateStore _store = new();
    private readonly CheckpointManager _manager;
    private readonly AsyncLocalRunScope _scope = new();
    private readonly FakeTypedAgentExecutionService _inner = new();
    private readonly DomainAgent _agent = new AgentBuilder().Role("Worker").Goal("Work").Build();
    private readonly MockMemoryScope _memory = new();

    public CheckpointingAgentExecutionServiceTests() =>
        _manager = new CheckpointManager(_store, NullLogger<CheckpointManager>.Instance);

    public void Dispose() => _memory.Dispose();

    private readonly MockLogger<CheckpointingAgentExecutionService> _log = new();

    private CheckpointingAgentExecutionService Decorate(ICheckpointManager? manager = null) =>
        new(_inner, _scope, manager ?? _manager, _log);

    private SimpleExecutionContext Context() =>
        new(CrewId.Create(), new Dictionary<string, string>(), _memory, [], CancellationToken.None);

    private static CrewTask Task(string key, string? description = null, string expected = "done") =>
        new CrewTaskBuilder().WithKey(key).Description(description ?? $"do {key}").ExpectedOutput(expected).Build();

    // The session is started here, the scope entered by the caller: an AsyncLocal set inside an async
    // method does not flow back to the code that awaited it.
    private System.Threading.Tasks.Task<string> StartSessionAsync(ExecutionId id) =>
        _manager.StartSessionAsync(id, "crew@/crew/crew.yaml", TestContext.Current.CancellationToken);

    private IDisposable Enter(ExecutionId id, ResumePlan? resume = null) =>
        _scope.Enter(new RunScope(id, "crew@/crew/crew.yaml", resume));

    private async System.Threading.Tasks.Task<ResumePlan> PlanOfAsync(ExecutionId id)
    {
        var state = await _store.GetAsync(id.ToString(), TestContext.Current.CancellationToken);
        return new ResumePlan(
            id.ToString(),
            state!.TaskCheckpoints.Where(c => c.Value.Status == CheckpointStatus.Completed)
                .ToDictionary(c => c.Key, c => c.Value.Output));
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldCheckpointTheFirstTaskBeforeTheSecondFails_WhenTheSecondThrows()
    {
        var id = ExecutionId.New();
        var decorated = Decorate();
        _inner.ThrowOn.Add("second");

        await StartSessionAsync(id);
        using (Enter(id))
        {
            await decorated.ExecuteTaskAsync(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                decorated.ExecuteTaskAsync(_agent, Task("second"), Context(), TestContext.Current.CancellationToken));
        }

        var state = await _store.GetAsync(id.ToString(), TestContext.Current.CancellationToken);
        Assert.Contains("first#1", state!.TaskCheckpoints.Keys);
        Assert.Contains("second#1", state.TaskCheckpoints.Keys);
        Assert.Equal(CheckpointStatus.Completed, state.TaskCheckpoints["first#1"].Status);
        Assert.NotNull(state.TaskCheckpoints["first#1"].Output);
        Assert.Equal(CheckpointStatus.Failed, state.TaskCheckpoints["second#1"].Status);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldCheckpointTheTypedOverloadToo_AndAnswerItFromTheCheckpointWhenResumed()
    {
        var id = ExecutionId.New();
        await StartSessionAsync(id);
        using (Enter(id))
        {
            var produced = await Decorate().ExecuteTaskAsync<FakeTypedAgentExecutionService.Answer>(
                _agent, Task("first"), Context(), TestContext.Current.CancellationToken);
            Assert.Equal("first", produced.StructuredOutput!.Text);
        }

        var plan = await PlanOfAsync(id);
        Assert.Contains("first#1", plan.CompletedOutputs.Keys);
        var callsBefore = _inner.Calls;

        using (Enter(id, plan))
        {
            var replayed = await Decorate().ExecuteTaskAsync<FakeTypedAgentExecutionService.Answer>(
                _agent, Task("first"), Context(), TestContext.Current.CancellationToken);

            Assert.True(replayed.Success);
            Assert.Equal("raw:first", replayed.RawOutput);
            Assert.Equal("first", replayed.StructuredOutput!.Text);
        }

        Assert.Equal(callsBefore, _inner.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldAnswerTheUntypedOverloadFromTheCheckpoint_WithoutCallingTheService()
    {
        var id = ExecutionId.New();
        await StartSessionAsync(id);
        using (Enter(id))
            await Decorate().ExecuteTaskAsync(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);
        var plan = await PlanOfAsync(id);
        var callsBefore = _inner.Calls;

        TaskResult replayed;
        using (Enter(id, plan))
            replayed = await Decorate().ExecuteTaskAsync(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);

        Assert.Equal(callsBefore, _inner.Calls);
        Assert.True(replayed.Resumed);
        Assert.True(replayed.Success);
        Assert.Equal("out:first", replayed.Output);
        Assert.Equal(42, replayed.TokensUsed);
        Assert.Equal(TimeSpan.FromSeconds(2), replayed.ExecutionTime);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldCarryTheResumedFlagAndTheUsage_OnTheTypedResult()
    {
        var id = ExecutionId.New();
        await StartSessionAsync(id);
        using (Enter(id))
            await Decorate().ExecuteTaskAsync<FakeTypedAgentExecutionService.Answer>(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);
        var plan = await PlanOfAsync(id);

        TaskResult<FakeTypedAgentExecutionService.Answer> replayed;
        using (Enter(id, plan))
            replayed = await Decorate().ExecuteTaskAsync<FakeTypedAgentExecutionService.Answer>(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);

        Assert.True(replayed.Resumed);
        Assert.Equal(7, replayed.TokensUsed);
        Assert.Equal(4, replayed.PromptTokens);
        Assert.Equal(3, replayed.CompletionTokens);
        Assert.Equal(2, replayed.IterationsUsed);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldHandBackTheStructuredOutputAsTheTypeItWasProducedAs_OnTheUntypedResult()
    {
        var id = ExecutionId.New();
        _inner.UntypedStructured = key => new FakeTypedAgentExecutionService.Answer($"structured-{key}");
        await StartSessionAsync(id);
        using (Enter(id))
            await Decorate().ExecuteTaskAsync(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);
        var plan = await PlanOfAsync(id);

        TaskResult replayed;
        using (Enter(id, plan))
            replayed = await Decorate().ExecuteTaskAsync(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);

        var structured = Assert.IsType<FakeTypedAgentExecutionService.Answer>(replayed.StructuredOutput);
        Assert.Equal("structured-first", structured.Text);
    }

    [Theory]
    [InlineData("description")]
    [InlineData("expected")]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldRunAnEditedTaskAgain_AndEveryTaskAfterIt_AndWarnOnce(string edited)
    {
        var id = ExecutionId.New();
        await StartSessionAsync(id);
        using (Enter(id))
        {
            var decorated = Decorate();
            await decorated.ExecuteTaskAsync(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);
            await decorated.ExecuteTaskAsync(_agent, Task("second"), Context(), TestContext.Current.CancellationToken);
            await decorated.ExecuteTaskAsync(_agent, Task("third"), Context(), TestContext.Current.CancellationToken);
        }

        var plan = await PlanOfAsync(id);
        var callsBefore = _inner.Calls;
        var first = edited == "description" ? Task("first", "do first, differently") : Task("first", expected: "done, otherwise");
        TaskResult a, b, c;
        using (Enter(id, plan))
        {
            var decorated = Decorate();
            a = await decorated.ExecuteTaskAsync(_agent, first, Context(), TestContext.Current.CancellationToken);
            b = await decorated.ExecuteTaskAsync(_agent, Task("second"), Context(), TestContext.Current.CancellationToken);
            c = await decorated.ExecuteTaskAsync(_agent, Task("third"), Context(), TestContext.Current.CancellationToken);
        }

        Assert.False(a.Resumed);
        Assert.False(b.Resumed);
        Assert.False(c.Resumed);
        Assert.Equal(callsBefore + 3, _inner.Calls);
        var warnings = _log.GetEntriesByLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
        var warning = Assert.Single(warnings);
        Assert.Contains("first#1", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldRunATaskAgain_WhenItsAgentChanged()
    {
        var id = ExecutionId.New();
        await StartSessionAsync(id);
        using (Enter(id))
            await Decorate().ExecuteTaskAsync(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);
        var plan = await PlanOfAsync(id);
        var other = new AgentBuilder().Role("Reviewer").Goal("Review").Build();

        TaskResult replayed;
        using (Enter(id, plan))
            replayed = await Decorate().ExecuteTaskAsync(other, Task("first"), Context(), TestContext.Current.CancellationToken);

        Assert.False(replayed.Resumed);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldNotTreatAnUntouchedTaskAsChanged_WhenTheCrewIsLoadedAgain()
    {
        // A second load gives every task and agent a new id: the fingerprint must not depend on them.
        var id = ExecutionId.New();
        await StartSessionAsync(id);
        using (Enter(id))
            await Decorate().ExecuteTaskAsync(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);
        var plan = await PlanOfAsync(id);
        var reloadedAgent = new AgentBuilder().Role("Worker").Goal("Work").Build();

        TaskResult replayed;
        using (Enter(id, plan))
            replayed = await Decorate().ExecuteTaskAsync(reloadedAgent, Task("first"), Context(), TestContext.Current.CancellationToken);

        Assert.True(replayed.Resumed);
        Assert.Empty(_log.GetEntriesByLevel(Microsoft.Extensions.Logging.LogLevel.Warning));
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldNumberTheInvocationsOfOneKey_SoARevisionIsNotTakenForTheFirstRun()
    {
        var id = ExecutionId.New();
        await StartSessionAsync(id);
        using (Enter(id))
        {
            var decorated = Decorate();
            await decorated.ExecuteTaskAsync(_agent, Task("write"), Context(), TestContext.Current.CancellationToken);
            await decorated.ExecuteTaskAsync(_agent, Task("write"), Context(), TestContext.Current.CancellationToken);
        }

        var state = await _store.GetAsync(id.ToString(), TestContext.Current.CancellationToken);
        Assert.Contains("write#1", state!.TaskCheckpoints.Keys);
        Assert.Contains("write#2", state.TaskCheckpoints.Keys);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldRunTheSecondInvocation_WhenOnlyTheFirstWasCompleted()
    {
        var id = ExecutionId.New();
        await StartSessionAsync(id);
        using (Enter(id))
            await Decorate().ExecuteTaskAsync(_agent, Task("write"), Context(), TestContext.Current.CancellationToken);
        var plan = await PlanOfAsync(id);
        var callsBefore = _inner.Calls;

        TaskResult first, second;
        using (Enter(id, plan))
        {
            var decorated = Decorate();
            first = await decorated.ExecuteTaskAsync(_agent, Task("write"), Context(), TestContext.Current.CancellationToken);
            second = await decorated.ExecuteTaskAsync(_agent, Task("write"), Context(), TestContext.Current.CancellationToken);
        }

        Assert.True(first.Resumed);
        Assert.False(second.Resumed);
        Assert.Equal(callsBefore + 1, _inner.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldNotCheckpointAnUnsuccessfulResult_SoTheTaskRunsAgainOnResume()
    {
        var id = ExecutionId.New();
        _inner.FailOn.Add("first");
        await StartSessionAsync(id);
        using (Enter(id))
        {
            var result = await Decorate().ExecuteTaskAsync(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);
            Assert.False(result.Success);
        }

        var plan = await PlanOfAsync(id);
        Assert.Empty(plan.CompletedOutputs);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldStayInactive_OutsideAnyRun()
    {
        var result = await Decorate().ExecuteTaskAsync(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(1, _inner.Calls);
        Assert.Empty(await _store.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldStayInactive_ForATaskThatHasNoKey()
    {
        var id = ExecutionId.New();
        await StartSessionAsync(id);
        using (Enter(id))
            await Decorate().ExecuteTaskAsync(
                _agent,
                new CrewTaskBuilder().Description("delegated").ExpectedOutput("done").Build(),
                Context(),
                TestContext.Current.CancellationToken);

        var state = await _store.GetAsync(id.ToString(), TestContext.Current.CancellationToken);
        Assert.Empty(state!.TaskCheckpoints);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldFailTheTask_WhenItsCheckpointCannotBeWritten()
    {
        var manager = new FakeCheckpointManager { CheckpointFailure = new IOException("disk full") };

        using (_scope.Enter(new RunScope(ExecutionId.New(), "crew")))
        {
            var failure = await Assert.ThrowsAsync<IOException>(() =>
                Decorate(manager).ExecuteTaskAsync(_agent, Task("first"), Context(), TestContext.Current.CancellationToken));
            Assert.Equal("disk full", failure.Message);
        }
    }
}
