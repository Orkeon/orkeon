using Orkeon.Domain.Common;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Domain.Crew;

/// <summary>
/// Repository interface for Crew aggregate.
/// Phase 3.1.3: Extends generic repository with crew-specific operations.
/// </summary>
public interface ICrewRepository : ISpecificationRepository<DomainCrew, CrewId>
{
    /// <summary>
    /// Gets multiple crews by their identifiers.
    /// </summary>
    Task<IReadOnlyList<DomainCrew>> GetByIdsAsync(
        IEnumerable<CrewId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets crews by status.
    /// </summary>
    Task<IReadOnlyList<DomainCrew>> GetByStatusAsync(
        CrewStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets crews by process type.
    /// </summary>
    Task<IReadOnlyList<DomainCrew>> GetByProcessTypeAsync(
        ProcessType processType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets crews that contain a specific agent.
    /// </summary>
    Task<IReadOnlyList<DomainCrew>> GetByAgentAsync(
        AgentId agentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets crews that contain a specific task.
    /// </summary>
    Task<IReadOnlyList<DomainCrew>> GetByTaskAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets crews with recent executions.
    /// </summary>
    Task<IReadOnlyList<DomainCrew>> GetWithRecentExecutionsAsync(
        DateTime since,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets crew execution statistics.
    /// </summary>
    Task<CrewExecutionStatistics> GetExecutionStatisticsAsync(
        CrewId crewId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents execution statistics for a crew.
/// </summary>
public record CrewExecutionStatistics(
    int TotalExecutions,
    int SuccessfulExecutions,
    int FailedExecutions,
    double AverageExecutionTime,
    double SuccessRate,
    DateTime? LastExecutionDate
);
