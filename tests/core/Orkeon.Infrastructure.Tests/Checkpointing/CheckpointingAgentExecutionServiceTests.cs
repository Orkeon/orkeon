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

    private static CrewTaskBuilder Declared(CrewTaskBuilder builder, Type? output) => output is null ? builder : builder.OutputPydantic(output);

    private static CrewTask Task(string key, string? description = null, string expected = "done", Type? output = null) =>
        Declared(new CrewTaskBuilder().WithKey(key).Description(description ?? $"do {key}").ExpectedOutput(expected), output).Build();

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
            await Decorate().ExecuteTaskAsync(_agent, Task("first", output: typeof(FakeTypedAgentExecutionService.Answer)), Context(), TestContext.Current.CancellationToken);
        var plan = await PlanOfAsync(id);

        TaskResult replayed;
        using (Enter(id, plan))
            replayed = await Decorate().ExecuteTaskAsync(_agent, Task("first", output: typeof(FakeTypedAgentExecutionService.Answer)), Context(), TestContext.Current.CancellationToken);

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

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldNumberTheQuestionsAgainFromOne_OnEveryAttemptOfARetriedTask()
    {
        var id = ExecutionId.New();
        var asker = new FakeQuestionAskingAgentExecutionService { FailFirst = 1 };
        var retrying = new Orkeon.Infrastructure.Crew.RetryingAgentExecutionService(
            asker, NullLogger<Orkeon.Infrastructure.Crew.RetryingAgentExecutionService>.Instance, new FakeImmediateTimeProvider());
        var decorated = new CheckpointingAgentExecutionService(retrying, _scope, _manager, _log);
        var task = new CrewTaskBuilder().WithKey("t").Description("do t").ExpectedOutput("done").Retry(new TaskRetryPolicy(2)).Build();

        await StartSessionAsync(id);
        using (Enter(id))
            await decorated.ExecuteTaskAsync(_agent, task, Context(), TestContext.Current.CancellationToken);

        Assert.Equal(["t#1.1", "t#1.1"], asker.RequestIds);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldCheckpointATaskThatAskedNothing_WhenASiblingSuspendedTheRunMeanwhile()
    {
        var id = ExecutionId.New();
        await StartSessionAsync(id);
        using (Enter(id))
        {
            var run = _scope.Current!;
            _inner.OnExecute = _ => run.Suspend(new PendingInputRequest { RequestId = "other#1.1", Kind = PendingInputKind.Text, Prompt = "?", TaskKey = "other#1" });

            var result = await Decorate().ExecuteTaskAsync(_agent, Task("quiet"), Context(), TestContext.Current.CancellationToken);

            Assert.True(result.Success);
        }

        var state = await _store.GetAsync(id.ToString(), TestContext.Current.CancellationToken);
        Assert.Equal(CheckpointStatus.Completed, state!.TaskCheckpoints["quiet#1"].Status);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldNotCheckpointATaskThatAsked_WhenTheRunSuspendedOnItsQuestion()
    {
        var id = ExecutionId.New();
        await StartSessionAsync(id);
        using (Enter(id))
        {
            var run = _scope.Current!;
            _inner.OnExecute = _ =>
            {
                var asked = TaskInputScope.Current!.NextRequestId();
                run.Suspend(new PendingInputRequest { RequestId = asked, Kind = PendingInputKind.Text, Prompt = "?", TaskKey = "asker#1" });
            };

            await Assert.ThrowsAsync<RunSuspendedException>(() =>
                Decorate().ExecuteTaskAsync(_agent, Task("asker"), Context(), TestContext.Current.CancellationToken));
        }

        var state = await _store.GetAsync(id.ToString(), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("asker#1", state!.TaskCheckpoints.Keys);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldRunTheTaskAgain_WhenTheStoredStructuredTypeIsNotTheOneTheTaskDeclares()
    {
        // The checkpoint names the type its structured output was produced as; the task declares none, so the name is not
        // resolved (Type.GetType on a name read from the store) and the checkpoint is refused.
        var id = ExecutionId.New();
        _inner.UntypedStructured = key => new FakeTypedAgentExecutionService.Answer($"structured-{key}");
        await StartSessionAsync(id);
        using (Enter(id))
            await Decorate().ExecuteTaskAsync(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);
        var plan = await PlanOfAsync(id);
        var before = _inner.Calls;

        using (Enter(id, plan))
            await Decorate().ExecuteTaskAsync(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);

        Assert.Equal(before + 1, _inner.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldMaskASecret_InTheErrorMessageItPersists()
    {
        var id = ExecutionId.New();
        _inner.ThrowOn.Add("leaky");
        await StartSessionAsync(id);
        using (Enter(id))
        {
            _inner.ThrowOn.Clear();
            _inner.OnExecute = _ => throw new InvalidOperationException("401 for key sk-abcdefghijklmnopqrstuvwxyz0123456789ABCD");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Decorate().ExecuteTaskAsync(_agent, Task("leaky"), Context(), TestContext.Current.CancellationToken));
        }

        var state = await _store.GetAsync(id.ToString(), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", state!.TaskCheckpoints["leaky#1"].ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", state.ErrorMessage, StringComparison.Ordinal);
    }

    private SimpleExecutionContext ContextWith(string name, string value) =>
        new(CrewId.Create(), new Dictionary<string, string> { [name] = value }, _memory, [], CancellationToken.None);

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldRunTheTaskAgain_WhenTheResumedRunIsGivenAnotherKickoffValue()
    {
        var id = ExecutionId.New();
        var task = Task("write", description: "Write about {topic}");
        await StartSessionAsync(id);
        using (Enter(id))
            await Decorate().ExecuteTaskAsync(_agent, task, ContextWith("topic", "A"), TestContext.Current.CancellationToken);
        var plan = await PlanOfAsync(id);
        var before = _inner.Calls;

        using (Enter(id, plan))
            await Decorate().ExecuteTaskAsync(_agent, task, ContextWith("topic", "B"), TestContext.Current.CancellationToken);
        var afterOtherValue = _inner.Calls;
        using (Enter(id, await PlanOfAsync(id)))
            await Decorate().ExecuteTaskAsync(_agent, task, ContextWith("topic", "B"), TestContext.Current.CancellationToken);

        // topic=B is not the run that topic=A checkpointed: it runs; the same value again is answered from its checkpoint.
        Assert.Equal(before + 1, afterOtherValue);
        Assert.Equal(afterOtherValue, _inner.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldGiveEachConcurrentInvocationItsOwnCheckpoint_WhateverOrderTheyArriveIn()
    {
        // Consensual runs one task for several agents at once: the order they reach the decorator is a race.
        var id = ExecutionId.New();
        var alice = new AgentBuilder().Role("Alice").Goal("Vote").Build();
        var bob = new AgentBuilder().Role("Bob").Goal("Vote").Build();
        await StartSessionAsync(id);
        using (Enter(id))
        {
            using (CheckpointSlot.Enter("a1"))
                await Decorate().ExecuteTaskAsync(bob, Task("vote"), Context(), TestContext.Current.CancellationToken);
            using (CheckpointSlot.Enter("a0"))
                await Decorate().ExecuteTaskAsync(alice, Task("vote"), Context(), TestContext.Current.CancellationToken);
        }

        var plan = await PlanOfAsync(id);
        var before = _inner.Calls;
        using (Enter(id, plan))
        {
            var decorated = Decorate();
            using (CheckpointSlot.Enter("a0"))
                Assert.True((await decorated.ExecuteTaskAsync(alice, Task("vote"), Context(), TestContext.Current.CancellationToken)).Resumed);
            using (CheckpointSlot.Enter("a1"))
                Assert.True((await decorated.ExecuteTaskAsync(bob, Task("vote"), Context(), TestContext.Current.CancellationToken)).Resumed);
        }

        Assert.Equal(before, _inner.Calls);
        Assert.Contains("vote@a0#1", plan.CompletedOutputs.Keys);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldRunTheTaskAgain_WhenTheStoredTypeOnlyStartsLikeAJsonType()
    {
        var id = ExecutionId.New();
        _inner.UntypedStructured = key => System.Text.Json.JsonDocument.Parse("{\"a\":1}").RootElement.Clone();
        await StartSessionAsync(id);
        using (Enter(id))
            await Decorate().ExecuteTaskAsync(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);
        var plan = await PlanOfAsync(id);
        var stored = System.Text.Json.Nodes.JsonNode.Parse(plan.CompletedOutputs["first#1"]!)!.AsObject();
        Assert.Equal(typeof(System.Text.Json.JsonElement).AssemblyQualifiedName, stored["structuredType"]!.GetValue<string>());
        stored["structuredType"] = "System.Text.Json.Evil, Evil.Assembly";
        var tampered = new ResumePlan(id.ToString(), new Dictionary<string, string?> { ["first#1"] = stored.ToJsonString() });
        var before = _inner.Calls;

        using (Enter(id, tampered))
            await Decorate().ExecuteTaskAsync(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);

        Assert.Equal(before + 1, _inner.Calls);
    }

    [Fact]
    [Trait("BR", "FWK-07")]
    public async System.Threading.Tasks.Task ShouldRunTheTypedTaskAgain_WhenItsStoredOutputCannotBeBuiltAsTheType()
    {
        var id = ExecutionId.New();
        await StartSessionAsync(id);
        using (Enter(id))
            await Decorate().ExecuteTaskAsync<FakeTypedAgentExecutionService.Answer>(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);
        var plan = await PlanOfAsync(id);
        var before = _inner.Calls;

        TaskResult<Unbuildable> replayed;
        using (Enter(id, plan))
            replayed = await Decorate().ExecuteTaskAsync<Unbuildable>(_agent, Task("first"), Context(), TestContext.Current.CancellationToken);

        Assert.Equal(before + 1, _inner.Calls);
        Assert.False(replayed.Resumed);
    }

    /// <summary>A type the serializer cannot build: its only constructor is private.</summary>
    public sealed class Unbuildable
    {
        private Unbuildable()
        {
        }

        public string? Text { get; set; }
    }
}
