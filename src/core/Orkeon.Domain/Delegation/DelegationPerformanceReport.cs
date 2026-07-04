using Orkeon.Domain.Common;

namespace Orkeon.Domain.Delegation;

/// <summary>Report containing delegation performance metrics.</summary>
public record DelegationPerformanceReport
{
    /// <summary>Gets the identifier of the agent this report is for.</summary>
    public AgentId AgentId { get; }
    /// <summary>Gets the total number of delegations.</summary>
    public int TotalDelegations { get; }
    /// <summary>Gets the number of successful delegations.</summary>
    public int SuccessfulDelegations { get; }
    /// <summary>Gets the number of failed delegations.</summary>
    public int FailedDelegations { get; }
    /// <summary>Gets the success rate as a ratio of successful to total delegations.</summary>
    public double SuccessRate => TotalDelegations == 0 ? 0 : (double)SuccessfulDelegations / TotalDelegations;
    /// <summary>Gets the average execution time across all delegations.</summary>
    public TimeSpan AverageExecutionTime { get; }
    /// <summary>Gets the number of delegations grouped by type.</summary>
    public Dictionary<string, int> DelegationsByType { get; }
    /// <summary>Gets the timestamp when this report was generated.</summary>
    public DateTime ReportGeneratedAt { get; }
    /// <summary>Gets the time period covered by this report.</summary>
    public TimeSpan ReportPeriod { get; }

    /// <summary>Initializes a new instance of <see cref="DelegationPerformanceReport"/>.</summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="totalDelegations">Total number of delegations.</param>
    /// <param name="successfulDelegations">Number of successful delegations.</param>
    /// <param name="failedDelegations">Number of failed delegations.</param>
    /// <param name="averageExecutionTime">Average execution time.</param>
    /// <param name="delegationsByType">Delegations grouped by type.</param>
    /// <param name="reportPeriod">The time period covered.</param>
    public DelegationPerformanceReport(
        AgentId agentId,
        int totalDelegations,
        int successfulDelegations,
        int failedDelegations,
        TimeSpan averageExecutionTime,
        Dictionary<string, int>? delegationsByType = null,
        TimeSpan reportPeriod = default)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        AgentId = agentId;
        TotalDelegations = totalDelegations;
        SuccessfulDelegations = successfulDelegations;
        FailedDelegations = failedDelegations;
        AverageExecutionTime = averageExecutionTime;
        DelegationsByType = delegationsByType ?? [];
        ReportGeneratedAt = DateTime.UtcNow;
        ReportPeriod = reportPeriod;
    }

    /// <summary>Creates an empty report for the given agent.</summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <returns>An empty <see cref="DelegationPerformanceReport"/>.</returns>
    public static DelegationPerformanceReport Empty(AgentId agentId)
    {
        return new DelegationPerformanceReport(agentId, 0, 0, 0, TimeSpan.Zero);
    }
}
