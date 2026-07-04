namespace Orkeon.Domain.Agent.ValueObjects;

/// <summary>Represents a skill or capability that an agent possesses.</summary>
public record AgentSkill
{
    /// <summary>Gets the name of this skill.</summary>
    public string Name { get; }
    /// <summary>Gets the category this skill belongs to.</summary>
    public string Category { get; }
    /// <summary>Gets the proficiency level of this skill (0.0 to 1.0).</summary>
    public double ProficiencyLevel { get; }
    /// <summary>Gets the list of tools related to this skill.</summary>
    public IReadOnlyList<string> RelatedTools { get; }
    /// <summary>Gets additional metadata for this skill.</summary>
    public IReadOnlyDictionary<string, object> Metadata { get; }

    /// <summary>Initializes a new instance of <see cref="AgentSkill"/>.</summary>
    /// <param name="name">The name of the skill.</param>
    /// <param name="category">The category this skill belongs to.</param>
    /// <param name="proficiencyLevel">The proficiency level (0.0 to 1.0).</param>
    /// <param name="relatedTools">Tools related to this skill.</param>
    /// <param name="metadata">Additional metadata.</param>
    private AgentSkill(
        string name,
        string category,
        double proficiencyLevel = 1.0,
        IEnumerable<string>? relatedTools = null,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        ArgumentNullException.ThrowIfNull(category);
        Category = category;
        ProficiencyLevel = double.IsNaN(proficiencyLevel) ? 0.0 : Math.Max(0, Math.Min(1.0, proficiencyLevel));
        RelatedTools = relatedTools != null
            ? relatedTools.ToList().AsReadOnly()
            : Array.Empty<string>();
        Metadata = metadata != null
            ? new Dictionary<string, object>(metadata)
            : [];
    }

    /// <summary>Creates a new <see cref="AgentSkill"/> with the specified parameters.</summary>
    /// <param name="name">The name of the skill.</param>
    /// <param name="category">The category of the skill.</param>
    /// <param name="proficiencyLevel">The proficiency level (0.0 to 1.0).</param>
    /// <param name="relatedTools">Tools related to this skill.</param>
    /// <param name="metadata">Additional metadata.</param>
    /// <returns>A new <see cref="AgentSkill"/> instance.</returns>
    public static AgentSkill Create(
        string name,
        string category,
        double proficiencyLevel = 1.0,
        IReadOnlyList<string>? relatedTools = null,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        return new AgentSkill(name, category, proficiencyLevel, relatedTools, metadata);
    }

    /// <summary>Gets a value indicating whether this agent is an expert (proficiency >= 0.8).</summary>
    public bool IsExpert => ProficiencyLevel >= 0.8;
    /// <summary>Gets a value indicating whether this agent is proficient (proficiency >= 0.6).</summary>
    public bool IsProficient => ProficiencyLevel >= 0.6;
    /// <summary>Gets a value indicating whether this agent is a novice (proficiency &lt; 0.4).</summary>
    public bool IsNovice => ProficiencyLevel < 0.4;

    /// <inheritdoc />
    public virtual bool Equals(AgentSkill? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Name == other.Name &&
               Category == other.Category &&
               ProficiencyLevel == other.ProficiencyLevel &&
               RelatedTools.SequenceEqual(other.RelatedTools) &&
               Metadata.Count == other.Metadata.Count &&
               Metadata.All(kvp => other.Metadata.TryGetValue(kvp.Key, out var value) &&
                            Equals(kvp.Value, value));
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Category);
        hash.Add(ProficiencyLevel);
        hash.Add(RelatedTools.Count);
        hash.Add(Metadata.Count);
        return hash.ToHashCode();
    }
}
