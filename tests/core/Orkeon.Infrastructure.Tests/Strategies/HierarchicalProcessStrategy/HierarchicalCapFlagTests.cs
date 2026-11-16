using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.Strategies.Lifecycle;

namespace Orkeon.Infrastructure.Tests.Strategies;

public sealed class HierarchicalCapFlagTests : IDisposable
{
    private readonly LifecycleFixture _fixture = new();
    private readonly MockManagerAgent _manager = new();

    public void Dispose() => _fixture.Dispose();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "CH-25")]
    public async System.Threading.Tasks.Task ShouldCarryTheCapFlagIntoTheSnapshot(bool capped)
    {
        var lead = _fixture.Agent("Lead", allowDelegation: true);
        var worker = _fixture.Agent("Worker");
        var draft = _fixture.Task("draft");
        _fixture.Execution.SetExecuteFunc((_, _, _, _) =>
            new TaskResult(true, "out", null, [], TimeSpan.Zero) { EndedOnIterationCap = capped });
        _manager.SetAssignResult(new TaskAssignment(draft.Id, worker.Id, "the only worker", DateTime.UtcNow));
        var crew = LifecycleFixture.Build(
            new CrewBuilder().Goal("Pipeline").Hierarchical(lead), [lead, worker], [draft]);
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

        await strategy.ExecuteHierarchicalAsync(crew, lead.Id, cancellationToken: TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(_fixture.Hook.CompletedTasks, s => s.TaskId == draft.Id.ToString());
        Assert.True(snapshot.Success);
        Assert.Equal(capped, snapshot.EndedOnIterationCap);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldCarryTheMissingDeliverableIntoTheSnapshot(bool missing)
    {
        var lead = _fixture.Agent("Lead", allowDelegation: true);
        var worker = _fixture.Agent("Worker");
        var draft = _fixture.Task("draft");
        _fixture.Execution.SetExecuteFunc((_, _, _, _) =>
            new TaskResult(true, "out", null, [], TimeSpan.Zero) { DeliverableMissing = missing });
        _manager.SetAssignResult(new TaskAssignment(draft.Id, worker.Id, "the only worker", DateTime.UtcNow));
        var crew = LifecycleFixture.Build(
            new CrewBuilder().Goal("Pipeline").Hierarchical(lead), [lead, worker], [draft]);
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

        await strategy.ExecuteHierarchicalAsync(crew, lead.Id, cancellationToken: TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(_fixture.Hook.CompletedTasks, s => s.TaskId == draft.Id.ToString());
        Assert.True(snapshot.Success);
        Assert.Equal(missing, snapshot.DeliverableMissing);
    }

    private HierarchicalProcessStrategy Strategy() => new(
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

    [Fact]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldNotFlagTheDeliverable_WhenTheManagerRejectsTheTaskToTheEnd()
    {
        var lead = _fixture.Agent("Lead", allowDelegation: true);
        var worker = _fixture.Agent("Worker");
        var draft = _fixture.Task("draft");
        _fixture.Execution.SetExecuteFunc((_, _, _, _) =>
            new TaskResult(true, "out", null, [], TimeSpan.Zero) { DeliverableMissing = true });
        _manager.SetAssignResult(new TaskAssignment(draft.Id, worker.Id, "the only worker", DateTime.UtcNow));
        _manager.SetReviewResult(false);
        var crew = LifecycleFixture.Build(
            new CrewBuilder().Goal("Pipeline").Hierarchical(lead), [lead, worker], [draft]);

        await Strategy().ExecuteHierarchicalAsync(crew, lead.Id, cancellationToken: TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(_fixture.Hook.CompletedTasks, s => s.TaskId == draft.Id.ToString());
        Assert.False(snapshot.Success);
        Assert.False(snapshot.DeliverableMissing);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldHandTheRevisedDeliverableToTheNextTask()
    {
        var lead = _fixture.Agent("Lead", allowDelegation: true);
        var worker = _fixture.Agent("Worker");
        var draft = _fixture.Task("draft");
        var next = _fixture.Task("next", draft);
        var draftAttempts = 0;
        IReadOnlyList<Orkeon.Application.Execution.TaskOutput>? seenByNext = null;
        _fixture.Execution.SetExecuteFunc((_, task, context, _) =>
        {
            if (task.TaskId == next.Id)
            {
                seenByNext = context.PreviousOutputs;
                return new TaskResult(true, "next out", null, [], TimeSpan.Zero);
            }

            // The first attempt writes nothing; the revision the manager accepts writes the file.
            return ++draftAttempts == 1
                ? new TaskResult(true, "draft 1", null, [], TimeSpan.Zero) { DeliverableMissing = true }
                : new TaskResult(true, "draft 2", null, [], TimeSpan.Zero) { DeliverablePath = "/output/draft.md" };
        });
        _manager.SetAssignResult(new TaskAssignment(draft.Id, worker.Id, "the only worker", DateTime.UtcNow));
        _manager.SetReviewResults(false, true);
        var crew = LifecycleFixture.Build(
            new CrewBuilder().Goal("Pipeline").Hierarchical(lead), [lead, worker], [draft, next]);

        await Strategy().ExecuteHierarchicalAsync(crew, lead.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, draftAttempts);
        var snapshot = Assert.Single(_fixture.Hook.CompletedTasks, s => s.TaskId == draft.Id.ToString());
        Assert.True(snapshot.Success);
        Assert.False(snapshot.DeliverableMissing);
        Assert.NotNull(seenByNext);
        var previous = Assert.Single(seenByNext, o => o.TaskId == draft.Id.Value.ToString());
        Assert.Equal("/output/draft.md", previous.DeliverablePath);
    }
}
