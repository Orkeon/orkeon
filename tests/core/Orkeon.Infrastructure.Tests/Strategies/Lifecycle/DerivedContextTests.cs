using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Agent;
using Orkeon.Application.Context;
using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Communication;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tools;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Strategies.Lifecycle;

/// <summary>
/// GAP-21 decision 5 — a run derives the context of a coworker's sub-answer and of a peer's takeover
/// from the context of the task it serves, with <c>with</c>, never rebuilds one: same crew, same
/// memory scope, same init settings. <c>DelegateWorkTool</c> used to build the coworker's context with
/// <c>new</c> — its sub-answer stored as a task result under <c>memory: true</c>, the parent's
/// settings lost, a fresh crew id when no strategy passed one —, and an Autonomous takeover ran under
/// <c>CrewId.Create()</c>: its result never stored, the peer recalling nothing.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "MockMemoryScope has a no-op Dispose; the tools are owned by the agents.")]
public sealed class DerivedContextTests : IDisposable
{
    private readonly LifecycleFixture _fixture = new();
    private readonly StubExecutionOrchestrator _llm = new();
    private readonly MockMemoryCoordinator _memory = new();
    private readonly AgentExecutionService _execution;

    public DerivedContextTests()
    {
        _execution = new AgentExecutionService(
            NullLogger<AgentExecutionService>.Instance,
            _llm,
            new CallbackOrchestrator(NullLogger<CallbackOrchestrator>.Instance),
            _memory);
    }

    public void Dispose() => _fixture.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_coworker_runs_in_the_parents_context_with_its_settings_and_never_stores_its_sub_answer()
    {
        using var scope = new MockMemoryScope();
        var crewId = CrewId.Create();
        IReadOnlyList<Orkeon.Application.Execution.TaskOutput> previous =
            [new("t-1", "agent-1", "earlier output", DateTime.UtcNow, true, TimeSpan.Zero)];
        var parent = new SimpleExecutionContext(crewId, new Dictionary<string, string> { ["project"] = "orkeon" }, scope, previous)
        {
            RecallFromMemory = false,
        };
        var executions = new MockAgentExecutionService();
        var coworker = new Orkeon.Domain.Agent.AgentBuilder().Role("Writer").Goal("Write").Build();
        using var tool = new DelegateWorkTool(
            new MockAgentCommunicationService(), AgentId.Create(), _ => coworker.Id, executions, _ => coworker, () => parent);

        var response = await tool.CallAsync(DelegateRequest("Write the intro", "for the launch"), Ct);

        Assert.True(response.Success, response.Error);
        var child = executions.LastExecuteContext;
        Assert.NotNull(child);
        Assert.Equal(crewId, child.CrewId);
        Assert.Same(scope, child.Memory);
        Assert.Same(previous, child.PreviousOutputs);
        Assert.False(child.RecallFromMemory);
        Assert.False(child.StoreResultInMemory);
        Assert.Equal("orkeon", child.Variables["project"]);
        Assert.Equal("for the launch", child.Variables["delegation_context"]);
        Assert.False(parent.Variables.ContainsKey("delegation_context"));
    }

    [Fact]
    public async Task Without_the_context_of_a_run_a_synchronous_delegation_is_refused()
    {
        var executions = new MockAgentExecutionService();
        var coworker = new Orkeon.Domain.Agent.AgentBuilder().Role("Writer").Goal("Write").Build();
        using var tool = new DelegateWorkTool(
            new MockAgentCommunicationService(), AgentId.Create(), _ => coworker.Id, executions, _ => coworker, () => null);

        var response = await tool.CallAsync(DelegateRequest("Write the intro", ""), Ct);

        Assert.False(response.Success);
        Assert.Contains("no crew run", response.Error, StringComparison.Ordinal);
        Assert.Equal(0, executions.ExecuteTaskCallCount);
    }

    [Fact]
    public async Task A_sequential_delegation_with_memory_stores_the_task_result_once_never_the_sub_answer()
    {
        var (lead, writer) = DelegatingPair();
        var intro = _fixture.Task("intro");
        var crew = LifecycleFixture.Build(new CrewBuilder().Goal("Launch").Sequential(), [lead, writer], [intro]);
        intro.AssignTo(lead.Id);
        var strategy = new SequentialProcessStrategy(
            Dependencies(), Delegation(), NullLogger<SequentialProcessStrategy>.Instance);

        var output = await strategy.ExecuteSequentialAsync(crew, cancellationToken: Ct);

        Assert.True(output.Success, output.Error);
        var stored = Assert.Single(_memory.Stored);
        Assert.Equal(lead.Id, stored.Agent.Id);
        Assert.Equal(intro.Id, stored.Task.Id);
        var subAnswer = Assert.Single(_llm.Contexts, turn => turn.Agent.Id == writer.Id);
        Assert.Equal(crew.Id, subAnswer.Context.CrewId);
        Assert.False(subAnswer.Context.StoreResultInMemory);
    }

    [Fact]
    public async Task A_crew_run_again_without_being_reloaded_delegates_from_the_context_of_its_new_run()
    {
        // The agents keep the tools an earlier run gave them: the second run used to fail at its
        // start ("Tool delegate_work_to_coworker already exists"), and a tool kept as it was would
        // derive the coworker's context from the first run's.
        var (lead, writer) = DelegatingPair();
        var intro = _fixture.Task("intro");
        var crew = LifecycleFixture.Build(new CrewBuilder().Goal("Launch").Sequential(), [lead, writer], [intro]);
        intro.AssignTo(lead.Id);

        var first = await new SequentialProcessStrategy(Dependencies(), Delegation(), NullLogger<SequentialProcessStrategy>.Instance)
            .ExecuteSequentialAsync(crew, new Dictionary<string, string> { ["run"] = "1" }, Ct);
        var second = await new SequentialProcessStrategy(Dependencies(), Delegation(), NullLogger<SequentialProcessStrategy>.Instance)
            .ExecuteSequentialAsync(crew, new Dictionary<string, string> { ["run"] = "2" }, Ct);

        Assert.True(first.Success, first.Error);
        Assert.True(second.Success, second.Error);
        Assert.Single(lead.Tools, t => t.Name == "delegate_work_to_coworker");
        var subAnswers = _llm.Contexts.Where(turn => turn.Agent.Id == writer.Id).ToList();
        Assert.Equal(["1", "2"], subAnswers.Select(turn => turn.Context.Variables["run"]));
    }

    [Fact]
    public async Task A_delegation_in_another_mode_runs_under_the_crews_id()
    {
        var (lead, writer) = DelegatingPair();
        var intro = _fixture.Task("intro");
        var crew = LifecycleFixture.Build(
            new CrewBuilder().Goal("Launch").Process(ProcessType.Graph), [lead, writer], [intro]);
        intro.AssignTo(lead.Id);
        var strategy = new GraphProcessStrategy(
            Dependencies(), Delegation(), NullLogger<GraphProcessStrategy>.Instance);

        var output = await strategy.ExecuteSequentialAsync(crew, cancellationToken: Ct);

        Assert.True(output.Success, output.Error);
        var subAnswer = Assert.Single(_llm.Contexts, turn => turn.Agent.Id == writer.Id);
        Assert.Equal(crew.Id, subAnswer.Context.CrewId);
        Assert.False(subAnswer.Context.StoreResultInMemory);
        Assert.Equal(lead.Id, Assert.Single(_memory.Stored).Agent.Id);
    }

    [Fact]
    public async Task An_autonomous_takeover_runs_under_the_crews_id_recalls_and_stores_its_retained_output_once_under_the_peer()
    {
        var first = _fixture.Agent("First", allowDelegation: true);
        var peer = _fixture.Agent("Peer");
        var outline = _fixture.Task("outline");
        var draft = _fixture.Task("draft");
        _llm.Answer = (agent, task) => agent.Id == first.Id && task.Description.Value == "draft"
            ? new TaskResult(false, "", null, [], TimeSpan.Zero, Error: "no final answer")
            : new TaskResult(true, $"{task.Description.Value} by {agent.Role.Value}", null, [], TimeSpan.Zero);
        var manager = new MockManagerAgent();
        manager.SetAssignResult(new TaskAssignment(outline.Id, first.Id, "first agent", DateTime.UtcNow));
        var crew = LifecycleFixture.Build(
            new CrewBuilder().Goal("Write").Process(ProcessType.Autonomous), [first, peer], [outline, draft]);
        var strategy = new AutonomousProcessStrategy(
            Dependencies(), new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance), manager, TestManagerLlm.Resolver(),
            NullLogger<AutonomousProcessStrategy>.Instance);

        var output = await strategy.ExecuteAutonomousAsync(
            crew, AgentExecutionBudget.Default, new Dictionary<string, string> { ["audience"] = "developers" }, Ct);

        Assert.True(output.Success, output.Error);
        var takeover = Assert.Single(_llm.Contexts, turn => turn.Agent.Id == peer.Id);
        Assert.Equal(crew.Id, takeover.Context.CrewId);
        Assert.Equal(draft.Id, takeover.Task.Id);
        Assert.Equal("developers", takeover.Context.Variables["audience"]);
        Assert.True(takeover.Context.Variables.ContainsKey("delegation_context"));
        Assert.Contains(takeover.Context.PreviousOutputs, o => o.Content == "outline by First");

        // Stored once, under the peer that produced it, as the task's result; the failed attempt never.
        Assert.Equal(
            [(first.Id, outline.Id), (peer.Id, draft.Id)],
            _memory.Stored.Select(s => (s.Agent.Id, s.Task.Id)));
        // The peer recalled before answering, like any execution that answers a task.
        Assert.Equal(2, _memory.Recalls.Count(t => t.Id == draft.Id));
    }

    private CrewStrategyDependencies Dependencies() =>
        new(_fixture.Tasks, _fixture.Agents, _execution, _fixture.MemoryScope, _fixture.Events);

    private AgentDelegationToolsProvider Delegation() =>
        new(new MockAgentCommunicationService(), _execution, NullLogger<AgentDelegationToolsProvider>.Instance);

    /// <summary>
    /// A lead allowed to delegate whose turn hands part of its task to a writer, and the writer: the
    /// lead's turn calls its <c>delegate_work_to_coworker</c> tool and waits for the answer.
    /// </summary>
    private (DomainAgent Lead, DomainAgent Writer) DelegatingPair()
    {
        var lead = _fixture.Agent("Lead", allowDelegation: true);
        var writer = _fixture.Agent("Writer");
        _llm.Answer = (agent, task) =>
        {
            if (agent.Id != lead.Id)
                return new TaskResult(true, "the writer's paragraph", null, [], TimeSpan.Zero);

            var delegate_ = agent.Tools.First(t => t.Name == "delegate_work_to_coworker");
            var answer = delegate_.CallAsync(DelegateRequest("Write the first paragraph", "", "Writer"), CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.True(answer.Success, answer.Error);
            return new TaskResult(true, $"{task.Description.Value} assembled", null, [], TimeSpan.Zero);
        };
        return (lead, writer);
    }

    private static ToolCallRequest DelegateRequest(string task, string context, string coworker = "Writer") =>
        new(
            ToolName: "delegate_work_to_coworker",
            Parameters: new Dictionary<string, object?>
            {
                ["task"] = task,
                ["context"] = context,
                ["coworker_role"] = coworker,
                ["wait_for_result"] = true,
            });
}
