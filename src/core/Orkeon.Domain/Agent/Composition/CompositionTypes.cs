namespace Orkeon.Domain.Agent.Composition;

/// <summary>Predicted performance metrics for a crew composition.</summary>
public sealed record PredictedPerformance
{
    /// <summary>Gets the predicted success rate (0.0 to 1.0).</summary>
    public double SuccessRate { get; init; }
    /// <summary>Gets the predicted efficiency score (0.0 to 1.0).</summary>
    public double EfficiencyScore { get; init; }
    /// <summary>Gets the predicted quality score (0.0 to 1.0).</summary>
    public double QualityScore { get; init; }
    /// <summary>Gets the estimated execution duration.</summary>
    public TimeSpan EstimatedDuration { get; init; }
    /// <summary>Gets additional metric scores by name.</summary>
    public IReadOnlyDictionary<string, double> MetricScores { get; init; } = new Dictionary<string, double>();
}

/// <summary>Analysis of skill coverage in a crew.</summary>
public sealed record SkillCoverageAnalysis
{
    /// <summary>Gets a mapping of required skills to whether they are covered.</summary>
    public IReadOnlyDictionary<string, bool> RequiredSkillsCovered { get; init; } = new Dictionary<string, bool>();
    /// <summary>Gets the list of skills that are missing from the crew.</summary>
    public IReadOnlyList<string> MissingSkills { get; init; } = [];
    /// <summary>Gets the list of skills that are redundant in the crew.</summary>
    public IReadOnlyList<string> RedundantSkills { get; init; } = [];
    /// <summary>Gets the overall coverage percentage (0.0 to 100.0).</summary>
    public double CoveragePercentage { get; init; }
}

/// <summary>Analysis of role fulfillment in a crew.</summary>
public sealed record RoleFulfillmentAnalysis
{
    /// <summary>Gets a mapping of roles to assigned agent identifiers.</summary>
    public IReadOnlyDictionary<string, string> RoleAssignments { get; init; } = new Dictionary<string, string>();
    /// <summary>Gets the list of roles that have no assigned agent.</summary>
    public IReadOnlyList<string> UnfilledRoles { get; init; } = [];
    /// <summary>Gets the list of roles with more agents than needed.</summary>
    public IReadOnlyList<string> OverstaffedRoles { get; init; } = [];
    /// <summary>Gets the overall fulfillment score (0.0 to 1.0).</summary>
    public double FulfillmentScore { get; init; }
}

/// <summary>Represents a gap in crew composition.</summary>
public sealed record CompositionGap
{
    /// <summary>Gets the type of gap (e.g., "skill", "role", "tool").</summary>
    public string Type { get; init; } = string.Empty;
    /// <summary>Gets the name of the missing element.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets a description of the gap.</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>Gets the severity of this gap (0.0 to 1.0).</summary>
    public double Severity { get; init; }
    /// <summary>Gets potential solutions to address this gap.</summary>
    public IReadOnlyList<string> PotentialSolutions { get; init; } = [];
}

/// <summary>Requirement for a specific role in a crew.</summary>
public sealed record RoleRequirement
{
    /// <summary>Gets the name of the required role.</summary>
    public string RoleName { get; init; } = string.Empty;
    /// <summary>Gets the minimum number of agents required for this role.</summary>
    public int MinAgents { get; init; } = 1;
    /// <summary>Gets the maximum number of agents allowed for this role.</summary>
    public int MaxAgents { get; init; } = 1;
    /// <summary>Gets the skills required for this role.</summary>
    public IReadOnlyList<string> RequiredSkills { get; init; } = [];
    /// <summary>Gets the tools required for this role.</summary>
    public IReadOnlyList<string> RequiredTools { get; init; } = [];
    /// <summary>Gets a value indicating whether this role is critical.</summary>
    public bool IsCritical { get; init; } = true;
}
