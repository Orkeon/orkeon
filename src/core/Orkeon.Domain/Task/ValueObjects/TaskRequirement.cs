namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>Represents requirements for task execution.</summary>
public record TaskRequirement
{
    /// <summary>Gets the name of this requirement.</summary>
    public string Name { get; }
    /// <summary>Gets the type of this requirement.</summary>
    public RequirementType Type { get; }
    /// <summary>Gets the description of this requirement.</summary>
    public string Description { get; }
    /// <summary>Gets the skills required to satisfy this requirement.</summary>
    public IReadOnlyList<string> RequiredSkills { get; }
    /// <summary>Gets the tools required to satisfy this requirement.</summary>
    public IReadOnlyList<string> RequiredTools { get; }
    /// <summary>Gets the constraints that must be met.</summary>
    public IReadOnlyDictionary<string, object> Constraints { get; }
    /// <summary>Gets a value indicating whether this requirement is mandatory.</summary>
    public bool IsMandatory { get; }

    /// <summary>Initializes a new instance of <see cref="TaskRequirement"/>.</summary>
    private TaskRequirement(
        string name,
        RequirementType type,
        string description,
        IReadOnlyList<string> requiredSkills,
        IReadOnlyList<string> requiredTools,
        IReadOnlyDictionary<string, object> constraints,
        bool isMandatory)
    {
        Name = name;
        Type = type;
        Description = description;
        RequiredSkills = requiredSkills;
        RequiredTools = requiredTools;
        Constraints = constraints;
        IsMandatory = isMandatory;
    }

    /// <summary>Creates a new <see cref="TaskRequirement"/> instance.</summary>
    /// <param name="name">The requirement name.</param>
    /// <param name="type">The requirement type.</param>
    /// <param name="description">The requirement description.</param>
    /// <param name="requiredSkills">Required skills.</param>
    /// <param name="requiredTools">Required tools.</param>
    /// <param name="constraints">Constraints to be satisfied.</param>
    /// <param name="isMandatory">Whether this requirement is mandatory.</param>
    /// <returns>A new <see cref="TaskRequirement"/> instance.</returns>
    public static TaskRequirement Create(
        string name,
        RequirementType type,
        string description,
        IReadOnlyList<string>? requiredSkills = null,
        IReadOnlyList<string>? requiredTools = null,
        Dictionary<string, object>? constraints = null,
        bool isMandatory = true)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);

        return new TaskRequirement(
            name,
            type,
            description,
            requiredSkills is not null ? new List<string>(requiredSkills).AsReadOnly() : Array.Empty<string>(),
            requiredTools is not null ? new List<string>(requiredTools).AsReadOnly() : Array.Empty<string>(),
            constraints is not null
                ? new Dictionary<string, object>(constraints).AsReadOnly()
                : new Dictionary<string, object>().AsReadOnly(),
            isMandatory);
    }

    /// <inheritdoc />
    public virtual bool Equals(TaskRequirement? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Name == other.Name
            && Type == other.Type
            && Description == other.Description
            && IsMandatory == other.IsMandatory
            && RequiredSkills.SequenceEqual(other.RequiredSkills)
            && RequiredTools.SequenceEqual(other.RequiredTools)
            && Constraints.Count == other.Constraints.Count
            && Constraints.All(kvp => other.Constraints.TryGetValue(kvp.Key, out var v) && Equals(kvp.Value, v));
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Type);
        hash.Add(Description);
        hash.Add(IsMandatory);
        foreach (var s in RequiredSkills)
            hash.Add(s);
        foreach (var t in RequiredTools)
            hash.Add(t);
        hash.Add(Constraints.Count);
        return hash.ToHashCode();
    }

    /// <summary>Determines whether this requirement is satisfied given the available skills and tools.</summary>
    /// <param name="availableSkills">The available skill names.</param>
    /// <param name="availableTools">The available tool names.</param>
    /// <returns><see langword="true"/> if the requirement is satisfied; otherwise <see langword="false"/>.</returns>
    public bool IsSatisfiedBy(IReadOnlyList<string> availableSkills, IReadOnlyList<string> availableTools)
    {
        ArgumentNullException.ThrowIfNull(availableSkills);
        ArgumentNullException.ThrowIfNull(availableTools);
        foreach (var skill in RequiredSkills)
        {
            if (!availableSkills.Contains(skill))
                return false;
        }

        foreach (var tool in RequiredTools)
        {
            if (!availableTools.Contains(tool))
                return false;
        }

        return true;
    }
}

/// <summary>Types of task requirements.</summary>
public sealed record RequirementType
{
    /// <summary>Gets the string value of this requirement type.</summary>
    public string Value { get; }
    private RequirementType(string value) => Value = value;

    /// <summary>Requires a specific agent skill.</summary>
    public static readonly RequirementType Skill = new("Skill");
    /// <summary>Requires a specific tool to be available.</summary>
    public static readonly RequirementType Tool = new("Tool");
    /// <summary>Requires a specific resource (e.g. memory, storage).</summary>
    public static readonly RequirementType Resource = new("Resource");
    /// <summary>Requires a specific permission or access right.</summary>
    public static readonly RequirementType Permission = new("Permission");
    /// <summary>Requires a general agent capability.</summary>
    public static readonly RequirementType Capability = new("Capability");
    /// <summary>A custom requirement type defined by the caller.</summary>
    public static readonly RequirementType Custom = new("Custom");

    private static readonly Dictionary<string, RequirementType> s_all = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(Skill)] = Skill,
        [nameof(Tool)] = Tool,
        [nameof(Resource)] = Resource,
        [nameof(Permission)] = Permission,
        [nameof(Capability)] = Capability,
        [nameof(Custom)] = Custom,
    };

    /// <summary>Gets all known requirement types.</summary>
    public static IReadOnlyCollection<RequirementType> All => s_all.Values;

    /// <summary>Returns the <see cref="RequirementType"/> matching <paramref name="value"/>, or throws if unknown.</summary>
    public static RequirementType From(string value) =>
        s_all.TryGetValue(value, out var s)
            ? s
            : throw new ArgumentException($"Unknown RequirementType: '{value}'", nameof(value));

    /// <summary>Tries to parse <paramref name="value"/> into a known <see cref="RequirementType"/>.</summary>
    public static bool TryFrom(string? value, out RequirementType? result)
    {
        if (value is not null && s_all.TryGetValue(value, out var f)) { result = f; return true; }
        result = null; return false;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
    /// <summary>Implicitly converts a <see cref="RequirementType"/> to its string value.</summary>
    public static implicit operator string(RequirementType s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Value;
    }
}
