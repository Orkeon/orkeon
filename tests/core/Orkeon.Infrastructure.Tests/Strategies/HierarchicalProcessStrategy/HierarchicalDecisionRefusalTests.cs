using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.Strategies.Lifecycle;

namespace Orkeon.Infrastructure.Tests.Strategies;

/// <summary>
/// BR-07 at run time: the hierarchical process refuses, before any task runs, a crew holding a
/// decision task, which only the sequential, parallel and graph processes run.
/// </summary>
public sealed class HierarchicalDecisionRefusalTests : IDisposable
{
    private readonly LifecycleFixture _fixture = new();
    private readonly MockManagerAgent _manager = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    [Trait("BR", "BR-07")]
    public async System.Threading.Tasks.Task ShouldFailBeforeFirstTask_WhenHierarchicalCrewHoldsDecisionTask()
    {
        var lead = _fixture.Agent("Lead", allowDelegation: true);
        var worker = _fixture.Agent("Worker");
        var draft = _fixture.Task("draft");
        var triage = DecisionRefusal.DecisionTask(_fixture, "triage");
        _manager.SetAssignResult(new TaskAssignment(draft.Id, worker.Id, "the only worker", DateTime.UtcNow));
        var crew = LifecycleFixture.Build(
            new CrewBuilder().Goal("Pipeline").Hierarchical(lead), [lead, worker], [draft, triage]);
        var strategy = new HierarchicalProcessStrategy(
            _fixture.Tasks,
            _fixture.Agents,
            NullLogger<HierarchicalProcessStrategy>.Instance,
            _manager,
            TestManagerLlm.Resolver(),
            _fixture.Execution,
            _fixture.MemoryScope,
            new MockMemoryCoordinator(),
            _fixture.Hook,
            _fixture.Events);

        await DecisionRefusal.AssertRefusedAsync(
            _fixture,
            () => strategy.ExecuteHierarchicalAsync(crew, lead.Id, cancellationToken: TestContext.Current.CancellationToken),
            "triage",
            ProcessType.Hierarchical);
    }
}

/// <summary>What the three refusal suites share: the decision task they add, and the refusal they expect.</summary>
internal static class DecisionRefusal
{
    public static CrewTask DecisionTask(LifecycleFixture fixture, string name)
    {
        var task = new CrewTaskBuilder().Description(name).ExpectedOutput("An outcome")
            .WithDecision(TaskDecision.Create(
                [ChoiceQuestionSpec.Create("intent", "Pick", new Dictionary<string, string> { ["a"] = "first", ["b"] = "second" })],
                null, null, false, null))
            .Build();
        fixture.Tasks.AddAsync(task).GetAwaiter().GetResult();
        return task;
    }

    /// <summary>
    /// The run fails at entry: these three processes end an unexpected fault by throwing it after the
    /// crew-failed hook, so the refusal comes back as the exception, with no task run.
    /// </summary>
    public static async System.Threading.Tasks.Task AssertRefusedAsync(
        LifecycleFixture fixture,
        Func<System.Threading.Tasks.Task<Orkeon.Domain.Crew.CrewOutput>> run,
        string taskName,
        ProcessType process)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(run);

        var expected = DecisionTaskRules.UnsupportedProcessMessage(taskName, process);
        Assert.Equal(expected, ex.Message);
        Assert.Equal(0, fixture.Execution.ExecuteTaskCallCount);
        Assert.Empty(fixture.Hook.StartedTasks);
        var failure = Assert.Single(fixture.Hook.Failures);
        Assert.Empty(failure.Tasks);
        Assert.Empty(fixture.Hook.Completions);
    }
}
