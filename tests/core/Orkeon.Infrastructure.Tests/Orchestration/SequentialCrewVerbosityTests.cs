using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Orchestration;
using Orkeon.Infrastructure.Parsing;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Application.Interfaces.Services;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// CH-03: a verbose crew makes its non-verbose agents verbose for the run, observed at the seam
/// the orchestrator calls to execute the tasks.
/// </summary>
public sealed class SequentialCrewVerbosityTests
{
    private static async Task<bool?> RunAsync(bool crewVerbose)
    {
        var agent = DomainAgent.Create(AgentRole.From("writer"), AgentGoal.From("write"));
        Assert.False(agent.Verbose);
        var seam = new RecordingVerbosityProcessStrategy(agent);
        var repo = new FakeCrewRepository();
        var orchestrator = new SequentialCrewOrchestrator(
            repo,
            NullLogger<SequentialCrewOrchestrator>.Instance,
            new FakeCrewExecutionStateManager(),
            seam,
            new ExecutionPlanParser(),
            new RecordingDomainEventDispatcher());

        var crew = DomainCrew.Create("goal", ProcessType.Sequential, verbose: crewVerbose);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());
        await repo.AddAsync(crew, TestContext.Current.CancellationToken);

        await orchestrator.KickoffAsync(crew.Id, new CrewInput("ctx", new Dictionary<string, object>()), TestContext.Current.CancellationToken);
        return seam.ObservedVerbose;
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async Task ShouldMakeAgentVerboseDuringRun_WhenCrewIsVerbose()
        => Assert.True(await RunAsync(crewVerbose: true));

    [Fact]
    [Trait("BR", "CH-03")]
    public async Task ShouldLeaveAgentQuiet_WhenCrewIsNotVerbose()
        => Assert.False(await RunAsync(crewVerbose: false));
}
