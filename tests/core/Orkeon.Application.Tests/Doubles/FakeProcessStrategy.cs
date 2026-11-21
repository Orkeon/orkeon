using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainCrewOutput = Orkeon.Domain.Crew.CrewOutput;
using DomainTaskOutput = Orkeon.Domain.Task.ValueObjects.TaskOutput;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IProcessStrategy"/> standing for the task execution seam of a kickoff: whatever the
/// process, it runs <see cref="During"/> inside the run the orchestrator opened, then succeeds with one task.
/// </summary>
internal sealed class FakeProcessStrategy : IProcessStrategy, IProcessStrategyFactory
{
    /// <summary>What the run does in place of its tasks; nothing when null.</summary>
    public Func<CancellationToken, System.Threading.Tasks.Task>? During { get; set; }

    /// <summary>How many times the orchestrator called the seam.</summary>
    public int Calls { get; private set; }

    public IProcessStrategy CreateStrategy(ProcessType processType) => this;

    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteSequentialAsync(
        DomainCrew crew, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken);

    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteHierarchicalAsync(
        DomainCrew crew, AgentId? managerAgentId, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken);

    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteParallelAsync(
        DomainCrew crew, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken);

    public System.Threading.Tasks.Task<DomainCrewOutput> ExecuteAutonomousAsync(
        DomainCrew crew, Orkeon.Domain.Autonomous.AgentExecutionBudget budget, IReadOnlyDictionary<string, string>? inputVariables = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken);

    private async System.Threading.Tasks.Task<DomainCrewOutput> RunAsync(CancellationToken cancellationToken)
    {
        Calls++;
        if (During is { } during)
            await during(cancellationToken).ConfigureAwait(false);
        return DomainCrewOutput.CreateSuccess(
            "ok",
            null,
            [DomainTaskOutput.Create("ok", "text", null, TaskId.Create(), true, TimeSpan.Zero)],
            TimeSpan.Zero);
    }
}
