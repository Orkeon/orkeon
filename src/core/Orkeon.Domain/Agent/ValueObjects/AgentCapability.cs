namespace Orkeon.Domain.Agent.ValueObjects;

/// <summary>Represents the capabilities of an agent.</summary>
public record AgentCapability
{
    /// <summary>Gets the name of this capability.</summary>
    public string Name { get; }
    /// <summary>Gets the description of this capability.</summary>
    public string Description { get; }
    /// <summary>Gets the list of skills required to exercise this capability.</summary>
    public IReadOnlyList<string> RequiredSkills { get; }
    /// <summary>Gets the list of tools required to exercise this capability.</summary>
    public IReadOnlyList<string> RequiredTools { get; }
    /// <summary>Gets the confidence level for this capability (0.0 to 1.0).</summary>
    public double ConfidenceLevel { get; }
    /// <summary>Gets additional parameters for this capability.</summary>
    public IReadOnlyDictionary<string, object> Parameters { get; }

    /// <summary>Initializes a new instance of <see cref="AgentCapability"/>.</summary>
    /// <param name="name">The name of the capability.</param>
    /// <param name="description">The description of the capability.</param>
    /// <param name="requiredSkills">The skills required to exercise this capability.</param>
    /// <param name="requiredTools">The tools required to exercise this capability.</param>
    /// <param name="confidenceLevel">The confidence level (0.0 to 1.0).</param>
    /// <param name="parameters">Additional parameters.</param>
    private AgentCapability(
        string name,
        string description,
        IEnumerable<string>? requiredSkills = null,
        IEnumerable<string>? requiredTools = null,
        double confidenceLevel = 1.0,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        ArgumentNullException.ThrowIfNull(description);
        Description = description;
        RequiredSkills = requiredSkills != null
            ? requiredSkills.ToList().AsReadOnly()
            : Array.Empty<string>();
        RequiredTools = requiredTools != null
            ? requiredTools.ToList().AsReadOnly()
            : Array.Empty<string>();
        ConfidenceLevel = double.IsNaN(confidenceLevel) ? 0.0 : Math.Max(0, Math.Min(1.0, confidenceLevel));
        Parameters = parameters != null
            ? new Dictionary<string, object>(parameters)
            : [];
    }

    /// <summary>Creates a new <see cref="AgentCapability"/> with the specified parameters.</summary>
    /// <param name="name">The name of the capability.</param>
    /// <param name="description">The description of the capability.</param>
    /// <param name="confidenceLevel">The confidence level (0.0 to 1.0).</param>
    /// <param name="requiredSkills">The skills required to exercise this capability.</param>
    /// <param name="requiredTools">The tools required to exercise this capability.</param>
    /// <param name="parameters">Additional parameters.</param>
    /// <returns>A new <see cref="AgentCapability"/> instance.</returns>
    public static AgentCapability Create(
        string name,
        string description,
        double confidenceLevel = 1.0,
        IReadOnlyList<string>? requiredSkills = null,
        IReadOnlyList<string>? requiredTools = null,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        return new AgentCapability(name, description, requiredSkills, requiredTools, confidenceLevel, parameters);
    }

    /// <summary>Determines whether this capability can be executed given the available skills and tools.</summary>
    /// <param name="availableSkills">The list of available skill names.</param>
    /// <param name="availableTools">The list of available tool names.</param>
    /// <returns><see langword="true"/> if all required skills and tools are available; otherwise <see langword="false"/>.</returns>
    public bool CanExecute(IEnumerable<string> availableSkills, IEnumerable<string> availableTools)
    {
        var skillSet = availableSkills as ICollection<string> ?? availableSkills.ToList();
        var toolSet = availableTools as ICollection<string> ?? availableTools.ToList();

        foreach (var skill in RequiredSkills)
        {
            if (!skillSet.Contains(skill))
                return false;
        }

        foreach (var tool in RequiredTools)
        {
            if (!toolSet.Contains(tool))
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public virtual bool Equals(AgentCapability? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Name == other.Name &&
               Description == other.Description &&
               ConfidenceLevel == other.ConfidenceLevel &&
               RequiredSkills.SequenceEqual(other.RequiredSkills) &&
               RequiredTools.SequenceEqual(other.RequiredTools) &&
               Parameters.Count == other.Parameters.Count &&
               Parameters.All(kvp => other.Parameters.TryGetValue(kvp.Key, out var value) &&
                              Equals(kvp.Value, value));
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Description);
        hash.Add(ConfidenceLevel);

        // Add count of collections to ensure consistent hashing
        hash.Add(RequiredSkills.Count);
        hash.Add(RequiredTools.Count);
        hash.Add(Parameters.Count);

        return hash.ToHashCode();
    }
}
