using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Crew;
using Orkeon.Infrastructure.Consensus;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.Strategies.FailedTask;

namespace Orkeon.Infrastructure.Tests.Process.Consensus;

public sealed class ConsensualCapFlagTests : IDisposable
{
    private readonly FailedTaskFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "CH-25")]
    public async System.Threading.Tasks.Task ShouldCarryTheCapFlagIntoTheSnapshot(bool capped)
    {
        var task = _fixture.Task("only");
        _fixture.Execution.SetExecuteFunc((_, _, _, _) =>
            new TaskResult(true, "out", null, [], TimeSpan.Zero) { EndedOnIterationCap = capped });
        var crew = FailedTaskFixture.Build(
            new CrewBuilder().Goal("Vote").Consensual(), [_fixture.Agent("Solo")], [task]);
        var strategy = new ConsensualProcessStrategy(
            new MajorityVotingStrategy(),
            new FakeBallotCollector(),
            _fixture.Dependencies,
            new MockMemoryCoordinator(),
            NullLogger<ConsensualProcessStrategy>.Instance,
            Options.Create(new ConsensualProcessOptions()),
            _fixture.Hook);

        await strategy.ExecuteConsensualAsync(crew, ct: TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(_fixture.Hook.CompletedTasks, s => s.TaskId == task.Id.ToString());
        Assert.True(snapshot.Success);
        Assert.Equal(capped, snapshot.EndedOnIterationCap);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldCarryTheMissingDeliverableIntoTheSnapshot(bool missing)
    {
        var task = _fixture.Task("only");
        _fixture.Execution.SetExecuteFunc((_, _, _, _) =>
            new TaskResult(true, "out", null, [], TimeSpan.Zero) { DeliverableMissing = missing });
        var crew = FailedTaskFixture.Build(
            new CrewBuilder().Goal("Vote").Consensual(), [_fixture.Agent("Solo")], [task]);
        var strategy = new ConsensualProcessStrategy(
            new MajorityVotingStrategy(),
            new FakeBallotCollector(),
            _fixture.Dependencies,
            new MockMemoryCoordinator(),
            NullLogger<ConsensualProcessStrategy>.Instance,
            Options.Create(new ConsensualProcessOptions()),
            _fixture.Hook);

        await strategy.ExecuteConsensualAsync(crew, ct: TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(_fixture.Hook.CompletedTasks, s => s.TaskId == task.Id.ToString());
        Assert.True(snapshot.Success);
        Assert.Equal(missing, snapshot.DeliverableMissing);
    }
}
