using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;
using DomainTaskOutput = Orkeon.Domain.Task.ValueObjects.TaskOutput;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IProcessStrategy"/> standing for the task execution seam: at the moment
/// the orchestrator calls it, it records whether the probe agent is verbose for the run.
/// </summary>
public sealed class RecordingVerbosityProcessStrategy(DomainAgent probe) : IProcessStrategy, IProcessStrategyFactory
{
    /// <summary>The verbosity of the probe agent seen during the call; null when never called.</summary>
    public bool? ObservedVerbose;

    public IProcessStrategy CreateStrategy(ProcessType processType) => this;

    public Task<DomainCrewOutput> ExecuteSequentialAsync(DomainCrew crew, IReadOnlyDictionary<string, string>? vars = null, CancellationToken ct = default)
        => Observe();

    public Task<DomainCrewOutput> ExecuteHierarchicalAsync(DomainCrew crew, AgentId? mgr, IReadOnlyDictionary<string, string>? vars = null, CancellationToken ct = default)
        => Observe();

    public Task<DomainCrewOutput> ExecuteParallelAsync(DomainCrew crew, IReadOnlyDictionary<string, string>? vars = null, CancellationToken ct = default)
        => Observe();

    public Task<DomainCrewOutput> ExecuteAutonomousAsync(DomainCrew crew, Orkeon.Domain.Autonomous.AgentExecutionBudget budget, IReadOnlyDictionary<string, string>? vars = null, CancellationToken ct = default)
        => Observe();

    private Task<DomainCrewOutput> Observe()
    {
        ObservedVerbose = CrewVerbosity.IsVerbose(probe);
        return Task.FromResult(DomainCrewOutput.CreateSuccess(
            "ok",
            null,
            [DomainTaskOutput.Create("ok", "text", null, TaskId.Create(), true, TimeSpan.Zero)],
            TimeSpan.Zero));
    }
}
