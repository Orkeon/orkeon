namespace Orkeon.Domain.Delegation;

/// <summary>Represents a pattern for delegation behavior.</summary>
public record DelegationPattern
{
    /// <summary>Gets the name of this delegation pattern.</summary>
    public string Name { get; }
    /// <summary>Gets the description of this delegation pattern.</summary>
    public string Description { get; }
    /// <summary>Gets the scenarios where this pattern is applicable.</summary>
    public IReadOnlyList<string> ApplicableScenarios { get; }
    /// <summary>Gets the configuration parameters for this pattern.</summary>
    public Dictionary<string, object> Parameters { get; }
    /// <summary>Gets the historical success rate of this pattern (0.0 to 1.0).</summary>
    public double SuccessRate { get; }
    /// <summary>Gets the number of times this pattern has been used.</summary>
    public int UsageCount { get; }

    /// <summary>Initializes a new instance of <see cref="DelegationPattern"/>.</summary>
    /// <param name="name">The name of the pattern.</param>
    /// <param name="description">The description of the pattern.</param>
    /// <param name="applicableScenarios">Scenarios where this pattern applies.</param>
    /// <param name="parameters">Configuration parameters.</param>
    /// <param name="successRate">Historical success rate (0.0 to 1.0).</param>
    /// <param name="usageCount">Number of times used.</param>
    public DelegationPattern(
        string name,
        string description,
        IReadOnlyList<string>? applicableScenarios = null,
        Dictionary<string, object>? parameters = null,
        double successRate = 0.0,
        int usageCount = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        ArgumentNullException.ThrowIfNull(description);
        Description = description;
        ApplicableScenarios = applicableScenarios ?? Array.Empty<string>();
        Parameters = parameters ?? [];
        SuccessRate = double.IsNaN(successRate) ? 0.0 : Math.Max(0, Math.Min(1.0, successRate));
        UsageCount = Math.Max(0, usageCount);
    }

    /// <summary>Gets a value indicating whether this pattern is proven (used > 10 times with > 80% success).</summary>
    public bool IsProven => UsageCount > 10 && SuccessRate > 0.8;
    /// <summary>Gets a value indicating whether this pattern is experimental (used fewer than 5 times).</summary>
    public bool IsExperimental => UsageCount < 5;
    /// <summary>Gets a value indicating whether this pattern is reliable (success rate > 90%).</summary>
    public bool IsReliable => SuccessRate > 0.9;
}
