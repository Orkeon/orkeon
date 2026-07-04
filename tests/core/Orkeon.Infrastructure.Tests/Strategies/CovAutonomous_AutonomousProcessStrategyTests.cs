using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Communication;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using ExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;

namespace Orkeon.Infrastructure.Tests.CovAutonomous;

/// <summary>
/// Coverage tests for <see cref="AutonomousProcessStrategy"/>. Uses the shared
/// manual <c>Mock*</c> doubles plus the real <see cref="InMemoryAgentChannel"/>;
/// no network, DB, or Docker.
/// </summary>
public sealed class CovAutonomous_AutonomousProcessStrategyTests : IDisposable
{
    private readonly MockTaskRepository _taskRepo = new();
    private readonly MockAgentRepository _agentRepo = new();
    private readonly MockAgentExecutionService _execService = new();
    private readonly MockManagerAgent _manager = new();
    private readonly MockMemoryScope _memoryScope = new();
    private readonly InMemoryAgentChannel _channel =
        new(NullLogger<InMemoryAgentChannel>.Instance);

    private AutonomousProcessStrategy CreateStrategy() => new(
        _taskRepo, _agentRepo, _execService, _channel, _manager, _memoryScope,
        NullLogger<AutonomousProcessStrategy>.Instance);

    // ── Helpers ────────────────────────────────────────────────────────────

    private static DomainAgent CreateAgent(string role, bool allowDelegation = false)
        => new AgentBuilder()
            .Role(role)
            .Goal($"Goal for {role}")
            .Backstory($"Backstory for {role}")
            .AllowDelegation(allowDelegation)
            .Build();

    private static DomainTask CreateTask(string desc)
        => new CrewTaskBuilder()
            .Description(desc)
            .ExpectedOutput("Complete it")
            .Build();

    private DomainCrew BuildCrew(IEnumerable<DomainAgent> agents, IEnumerable<DomainTask> tasks)
    {
        var crew = DomainCrew.Create("Autonomous crew", ProcessType.Autonomous);
        foreach (var a in agents)
        {
            _agentRepo.AddAgentToStore(a);
            crew.AddAgent(a.Id);
        }
        foreach (var t in tasks)
        {
            _taskRepo.AddTaskToStore(t);
            crew.AddTask(t.Id);
        }
        return crew;
    }

    // ── Constructor guards ─────────────────────────────────────────────────

    [Theory]
    [InlineData("taskRepository")]
    [InlineData("agentRepository")]
    [InlineData("executionService")]
    [InlineData("channel")]
    [InlineData("managerAgent")]
    [InlineData("memoryScope")]
    [InlineData("logger")]
    public void Constructor_NullArgument_Throws(string paramName)
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new AutonomousProcessStrategy(
            paramName == "taskRepository" ? null! : _taskRepo,
            paramName == "agentRepository" ? null! : _agentRepo,
            paramName == "executionService" ? null! : _execService,
            paramName == "channel" ? null! : _channel,
            paramName == "managerAgent" ? null! : _manager,
            paramName == "memoryScope" ? null! : _memoryScope,
            paramName == "logger" ? null! : NullLogger<AutonomousProcessStrategy>.Instance));
        Assert.Equal(paramName, ex.ParamName);
    }

    // ── Unsupported modes ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteSequentialAsync_Throws()
    {
        var crew = BuildCrew([CreateAgent("a")], []);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            CreateStrategy().ExecuteSequentialAsync(crew, ExecutionPlan.Create(crew.Tasks), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteHierarchicalAsync_Throws()
    {
        var crew = BuildCrew([CreateAgent("a")], []);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            CreateStrategy().ExecuteHierarchicalAsync(crew, AgentId.Create(), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteParallelAsync_Throws()
    {
        var crew = BuildCrew([CreateAgent("a")], []);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            CreateStrategy().ExecuteParallelAsync(crew, ExecutionPlan.Create(crew.Tasks), cancellationToken: TestContext.Current.CancellationToken));
    }

    // ── ExecuteAutonomousAsync guard clauses ───────────────────────────────

    [Fact]
    public async Task ExecuteAutonomousAsync_NullCrew_Throws()
        => await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CreateStrategy().ExecuteAutonomousAsync(null!, AgentExecutionBudget.Default, cancellationToken: TestContext.Current.CancellationToken));

    [Fact]
    public async Task ExecuteAutonomousAsync_NullBudget_Throws()
    {
        var crew = BuildCrew([CreateAgent("a")], []);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CreateStrategy().ExecuteAutonomousAsync(crew, null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAutonomousAsync_NoAgents_Throws()
    {
        var crew = DomainCrew.Create("empty", ProcessType.Autonomous);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateStrategy().ExecuteAutonomousAsync(crew, AgentExecutionBudget.Default, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("No agents", ex.Message);
    }

    // ── Happy path ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAutonomousAsync_SingleTask_ProducesSuccessOutput()
    {
        var agent = CreateAgent("worker");
        var task = CreateTask("do work");
        var crew = BuildCrew([agent], [task]);

        _manager.SetAssignResult(new TaskAssignment(
            task.Id, agent.Id, "best fit", DateTime.UtcNow));
        _execService.SetExecuteResult(new TaskResult(
            true, "task done", null, [], TimeSpan.FromMilliseconds(50)));

        var result = await CreateStrategy().ExecuteAutonomousAsync(crew, AgentExecutionBudget.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("task done", result.Output);
        Assert.Single(result.TaskOutputs);
        var metadata = result.Metadata!.ToDictionary();
        Assert.Equal("autonomous", metadata["process_type"]);
        Assert.Equal(1, metadata["agent_count"]);
        // Manager assigned and execution service ran.
        Assert.Equal(1, _manager.AssignTaskCallCount);
        Assert.Equal(1, _execService.ExecuteTaskCallCount);
    }

    [Fact]
    public async Task ExecuteAutonomousAsync_PropagatesMeasuredTokenTelemetry()
    {
        // Arrange — two tasks executed directly, each costing 60 tokens (40/20).
        var agent = CreateAgent("metered-worker");
        var task1 = CreateTask("metered work 1");
        var task2 = CreateTask("metered work 2");
        var crew = BuildCrew([agent], [task1, task2]);

        _execService.SetExecuteFunc((a, t, ctx, ct) =>
            new TaskResult(true, "metered done", null, [], TimeSpan.FromMilliseconds(10), TokensUsed: 60)
            {
                PromptTokens = 40,
                CompletionTokens = 20,
            });

        // Act
        var result = await CreateStrategy().ExecuteAutonomousAsync(
            crew, AgentExecutionBudget.Default, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — measured sums reach the crew metadata under the canonical keys (R10.8);
        // fails on the legacy code, whose autonomous metadata carried no token entry.
        Assert.True(result.Success);
        Assert.Equal(120, result.Metadata.GetRequired<int>(
            Orkeon.Domain.Crew.ValueObjects.CrewMetadata.TotalTokensKey));
        Assert.Equal(80, result.Metadata.GetRequired<int>(
            Orkeon.Domain.Crew.ValueObjects.CrewMetadata.PromptTokensKey));
        Assert.Equal(40, result.Metadata.GetRequired<int>(
            Orkeon.Domain.Crew.ValueObjects.CrewMetadata.CompletionTokensKey));
    }

    [Fact]
    public async Task ExecuteAutonomousAsync_AssignmentToUnknownAgent_FallsBackToFirst()
    {
        var agent = CreateAgent("worker");
        var task = CreateTask("do work");
        var crew = BuildCrew([agent], [task]);

        // Manager points at an agent that is not in the crew → fallback to agents[0].
        _manager.SetAssignResult(new TaskAssignment(
            task.Id, AgentId.Create(), "stale", DateTime.UtcNow));
        _execService.SetExecuteResult(new TaskResult(true, "ok", null, [], TimeSpan.Zero));

        var result = await CreateStrategy().ExecuteAutonomousAsync(crew, AgentExecutionBudget.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(agent.Id, _execService.LastExecuteAgent!.Id);
    }

    [Fact]
    public async Task ExecuteAutonomousAsync_MissingTask_IsSkipped()
    {
        var agent = CreateAgent("worker");
        var crew = DomainCrew.Create("crew", ProcessType.Autonomous);
        _agentRepo.AddAgentToStore(agent);
        crew.AddAgent(agent.Id);
        // Add a task id to the crew but never store it → repo returns null → skipped.
        crew.AddTask(TaskId.Create());

        var result = await CreateStrategy().ExecuteAutonomousAsync(crew, AgentExecutionBudget.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Empty(result.TaskOutputs);
        Assert.Equal(0, _execService.ExecuteTaskCallCount);
    }

    [Fact]
    public async Task ExecuteAutonomousAsync_AgentNotInRepository_IsNotLoaded()
    {
        // Crew references an agent that the repository cannot resolve.
        var resolvable = CreateAgent("resolvable");
        var crew = DomainCrew.Create("crew", ProcessType.Autonomous);
        _agentRepo.AddAgentToStore(resolvable);
        crew.AddAgent(resolvable.Id);
        crew.AddAgent(AgentId.Create()); // not stored → skipped during load

        var task = CreateTask("t");
        _taskRepo.AddTaskToStore(task);
        crew.AddTask(task.Id);

        _manager.SetAssignResult(new TaskAssignment(task.Id, resolvable.Id, "r", DateTime.UtcNow));
        _execService.SetExecuteResult(new TaskResult(true, "done", null, [], TimeSpan.Zero));

        var result = await CreateStrategy().ExecuteAutonomousAsync(crew, AgentExecutionBudget.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var metadata = result.Metadata!.ToDictionary();
        Assert.Equal(1, metadata["agent_count"]); // only resolvable loaded
    }

    // ── Delegation path ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAutonomousAsync_FailedDelegatingAgent_DelegatesViaChannel()
    {
        var delegator = CreateAgent("delegator", allowDelegation: true);
        var helper = CreateAgent("helper");
        var task = CreateTask("hard task");
        var crew = BuildCrew([delegator, helper], [task]);

        _manager.SetAssignResult(new TaskAssignment(task.Id, delegator.Id, "lead", DateTime.UtcNow));

        // First (assignment) call fails for the delegator; the delegation handler then
        // routes to the helper, which succeeds.
        _execService.SetExecuteFunc((agent, _, _, _) =>
            agent.Id == delegator.Id
                ? new TaskResult(false, "", null, [], TimeSpan.Zero, "needs help")
                : new TaskResult(true, "helper solved it", null, [], TimeSpan.FromMilliseconds(10), TokensUsed: 42));

        var budget = AgentExecutionBudget.Default;
        var result = await CreateStrategy().ExecuteAutonomousAsync(crew, budget, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("helper solved it", result.Output);
        // The execution service ran for the delegator (assignment) and the helper (delegation).
        Assert.True(_execService.ExecuteTaskCallCount >= 2);
        Assert.True(budget.CurrentDelegationDepth >= 1);
        // Tokens consumed by delegated work are accounted on the parent budget.
        Assert.True(budget.CurrentTokensConsumed >= 42);
    }

    [Fact]
    public async Task ExecuteAutonomousAsync_FailedNonDelegatingAgent_ReturnsFailureOutput()
    {
        var agent = CreateAgent("solo", allowDelegation: false);
        var task = CreateTask("task");
        var crew = BuildCrew([agent], [task]);

        _manager.SetAssignResult(new TaskAssignment(task.Id, agent.Id, "x", DateTime.UtcNow));
        _execService.SetExecuteResult(new TaskResult(false, "failed body", null, [], TimeSpan.Zero, "boom"));

        var result = await CreateStrategy().ExecuteAutonomousAsync(crew, AgentExecutionBudget.Default, cancellationToken: TestContext.Current.CancellationToken);

        // Crew output is always success=true (it aggregates), but the task output is a failure.
        Assert.Single(result.TaskOutputs);
        Assert.False(result.TaskOutputs[0].Success);
    }

    [Fact]
    public async Task ExecuteAutonomousAsync_DelegationWithNoOtherAgent_YieldsFailedTaskOutput()
    {
        // Delegating agent fails but there are no other candidates → AttemptDelegationAsync
        // throws InvalidOperationException, which propagates out as a crew failure is not
        // caught here (only BudgetExhausted is). Verify it surfaces.
        var solo = CreateAgent("solo", allowDelegation: true);
        var task = CreateTask("task");
        var crew = BuildCrew([solo], [task]);

        _manager.SetAssignResult(new TaskAssignment(task.Id, solo.Id, "x", DateTime.UtcNow));
        _execService.SetExecuteResult(new TaskResult(false, "", null, [], TimeSpan.Zero, "fail"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateStrategy().ExecuteAutonomousAsync(crew, AgentExecutionBudget.Default, cancellationToken: TestContext.Current.CancellationToken));
    }

    // ── Budget exhaustion ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAutonomousAsync_ToolCallBudgetExhausted_ReturnsPartialOutput()
    {
        var agent = CreateAgent("worker");
        var task = CreateTask("task");
        var crew = BuildCrew([agent], [task]);

        _manager.SetAssignResult(new TaskAssignment(task.Id, agent.Id, "x", DateTime.UtcNow));
        _execService.SetExecuteResult(new TaskResult(true, "irrelevant", null, [], TimeSpan.Zero));

        // MaxToolCalls = 0 → the RecordToolCall in ExecuteTaskAutonomouslyAsync throws
        // BudgetExhaustedException, caught to produce a partial output.
        var budget = new AgentExecutionBudget { MaxToolCalls = 0 };

        var result = await CreateStrategy().ExecuteAutonomousAsync(crew, budget, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(result.TaskOutputs);
        Assert.False(result.TaskOutputs[0].Success);
        Assert.Contains("BUDGET EXHAUSTED", result.TaskOutputs[0].Output);
    }

    [Fact]
    public async Task ExecuteAutonomousAsync_WallTimeExhausted_StopsLoopGracefully()
    {
        var agent = CreateAgent("worker");
        var task = CreateTask("task");
        var crew = BuildCrew([agent], [task]);

        _manager.SetAssignResult(new TaskAssignment(task.Id, agent.Id, "x", DateTime.UtcNow));

        // Zero wall time → the budget.AssertWallTime() at the top of the loop throws
        // BudgetExhaustedException, caught by the orchestrator → empty results, still success.
        var budget = new AgentExecutionBudget { MaxWallTime = TimeSpan.Zero };

        var result = await CreateStrategy().ExecuteAutonomousAsync(crew, budget, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Empty(result.TaskOutputs);
    }

    [Fact]
    public async Task ExecuteAutonomousAsync_Cancellation_PropagatesOperationCanceled()
    {
        var agent = CreateAgent("worker");
        var task = CreateTask("task");
        var crew = BuildCrew([agent], [task]);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateStrategy().ExecuteAutonomousAsync(crew, AgentExecutionBudget.Default, null, cts.Token));
    }

    // ── Channel handler behaviour ────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAutonomousAsync_RegistersHandlers_ThenUnregistersAfterCompletion()
    {
        var agent = CreateAgent("worker");
        var task = CreateTask("task");
        var crew = BuildCrew([agent], [task]);

        _manager.SetAssignResult(new TaskAssignment(task.Id, agent.Id, "x", DateTime.UtcNow));
        _execService.SetExecuteResult(new TaskResult(true, "done", null, [], TimeSpan.Zero));

        await CreateStrategy().ExecuteAutonomousAsync(crew, AgentExecutionBudget.Default, cancellationToken: TestContext.Current.CancellationToken);

        // After completion, all handlers must be unregistered: an unknown-handler request
        // returns a failure response (rather than succeeding through a leftover handler).
        var probe = AgentChannelRequest.Create(AgentId.Create(), agent.Id, "info", "ping");
        var resp = await _channel.RequestAsync(probe, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.False(resp.Success);
    }

    [Fact]
    public async Task ChannelHandler_NonDelegateIntent_AcknowledgesViaRole()
    {
        // Build a long-running crew so the handler stays registered while we probe it.
        var agent = CreateAgent("acker");
        var blocker = CreateTask("blocking");
        var crew = BuildCrew([agent], [blocker]);

        _manager.SetAssignResult(new TaskAssignment(blocker.Id, agent.Id, "x", DateTime.UtcNow));

        var probeAck = new TaskCompletionSource<AgentChannelResponse>();
        _execService.SetExecuteFunc((_, _, _, ct) =>
        {
            // While inside execution the handler is registered → probe an info intent.
            var req = AgentChannelRequest.Create(AgentId.Create(), agent.Id, "clarify", "what?");
            var r = _channel.RequestAsync(req, TimeSpan.FromSeconds(2), ct).GetAwaiter().GetResult();
            probeAck.SetResult(r);
            return new TaskResult(true, "done", null, [], TimeSpan.Zero);
        });

        await CreateStrategy().ExecuteAutonomousAsync(crew, AgentExecutionBudget.Default, cancellationToken: TestContext.Current.CancellationToken);

        var ack = await probeAck.Task;
        Assert.True(ack.Success);
        Assert.Contains("acknowledges", ack.Payload);
        Assert.Contains("clarify", ack.Payload);
    }

    [Fact]
    public async Task ChannelHandler_DelegateIntent_ExecutesDelegatedTaskAndReturnsPayload()
    {
        var agent = CreateAgent("worker");
        var blocker = CreateTask("blocking");
        var crew = BuildCrew([agent], [blocker]);

        _manager.SetAssignResult(new TaskAssignment(blocker.Id, agent.Id, "x", DateTime.UtcNow));

        var probe = new TaskCompletionSource<AgentChannelResponse>();
        var delegateFired = false;
        _execService.SetExecuteFunc((_, _, _, ct) =>
        {
            // While the handler is registered (first synchronous call), fire a delegate
            // request to the same agent and capture the response.
            if (!delegateFired)
            {
                delegateFired = true;
                var req = AgentChannelRequest.Create(
                    AgentId.Create(), agent.Id, "delegate", "delegated payload");
                var r = _channel.RequestAsync(req, TimeSpan.FromSeconds(2), ct).GetAwaiter().GetResult();
                if (!probe.Task.IsCompleted) probe.TrySetResult(r);
            }
            return new TaskResult(true, "delegated-result", null, [], TimeSpan.Zero, TokensUsed: 5);
        });

        await CreateStrategy().ExecuteAutonomousAsync(crew, AgentExecutionBudget.Default, cancellationToken: TestContext.Current.CancellationToken);

        var resp = await probe.Task;
        Assert.True(resp.Success);
        Assert.Equal("delegated-result", resp.Payload);
    }

    public void Dispose()
    {
        _memoryScope.Dispose();
        GC.SuppressFinalize(this);
    }
}
