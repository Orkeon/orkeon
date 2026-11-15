using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.Strategies.FailedTask;

namespace Orkeon.Infrastructure.Tests.Strategies;

public sealed class GraphCapFlagTests : IDisposable
{
    private readonly FailedTaskFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "CH-25")]
    public async System.Threading.Tasks.Task ShouldCarryTheCapFlagIntoTheSnapshot(bool capped)
    {
        var worker = _fixture.Agent("Worker");
        var task = _fixture.Task("only");
        _fixture.Execution.SetExecuteFunc((_, _, _, _) =>
            new TaskResult(true, "out", null, [], TimeSpan.Zero) { EndedOnIterationCap = capped });
        var crew = FailedTaskFixture.Build(
            new CrewBuilder().Goal("Graph").Process(ProcessType.Graph), [worker], [task]);
        var strategy = new GraphProcessStrategy(
            _fixture.Dependencies,
            new AgentDelegationToolsProvider(
                new MockAgentCommunicationService(), _fixture.Execution,
                NullLogger<AgentDelegationToolsProvider>.Instance),
            NullLogger<GraphProcessStrategy>.Instance,
            _fixture.Hook);

        await strategy.ExecuteSequentialAsync(crew, cancellationToken: TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(_fixture.Hook.CompletedTasks, s => s.TaskId == task.Id.ToString());
        Assert.True(snapshot.Success);
        Assert.Equal(capped, snapshot.EndedOnIterationCap);
    }
}
