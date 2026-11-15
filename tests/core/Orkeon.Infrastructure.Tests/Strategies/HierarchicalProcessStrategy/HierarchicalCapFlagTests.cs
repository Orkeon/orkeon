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
}
