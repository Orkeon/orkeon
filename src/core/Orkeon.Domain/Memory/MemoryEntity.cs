namespace Orkeon.Domain.Memory;

/// <summary>
/// Entity in memory.
/// </summary>
public record MemoryEntity
{
    /// <summary>Gets the entity name.</summary>
    public string Name { get; init; }
    /// <summary>Gets the entity type.</summary>
    public EntityType Type { get; init; }
    /// <summary>Gets the entity attributes.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; }
    /// <summary>Gets when the entity was last updated.</summary>
    public DateTime LastUpdated { get; init; }

    /// <summary>Initializes a new instance of <see cref="MemoryEntity"/>.</summary>
    /// <param name="name">The entity name.</param>
    /// <param name="type">The entity type.</param>
    /// <param name="attributes">The entity attributes.</param>
    /// <param name="lastUpdated">When the entity was last updated.</param>
    private MemoryEntity(
        string name,
        EntityType type,
        IDictionary<string, string>? attributes,
        DateTime lastUpdated)
    {
        Name = name;
        Type = type;
        // Create a defensive copy to ensure immutability
        Attributes = new Dictionary<string, string>(attributes ?? new Dictionary<string, string>());
        LastUpdated = lastUpdated;
    }

    /// <summary>
    /// Creates a new <see cref="MemoryEntity"/> with validated parameters.
    /// </summary>
    /// <param name="name">The entity name (must not be null or whitespace).</param>
    /// <param name="type">The entity type.</param>
    /// <param name="attributes">Optional entity attributes.</param>
    public static MemoryEntity Create(
        string name,
        EntityType type,
        IDictionary<string, string>? attributes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new MemoryEntity(name, type, attributes, DateTime.UtcNow);
    }

    /// <summary>Deconstructs the entity into its components.</summary>
    /// <param name="name">The entity name.</param>
    /// <param name="type">The entity type.</param>
    /// <param name="attributes">The entity attributes.</param>
    /// <param name="lastUpdated">When the entity was last updated.</param>
    public void Deconstruct(out string name, out EntityType type, out IReadOnlyDictionary<string, string> attributes, out DateTime lastUpdated)
    {
        name = Name;
        type = Type;
        attributes = Attributes;
        lastUpdated = LastUpdated;
    }

    /// <inheritdoc />
    public virtual bool Equals(MemoryEntity? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Name == other.Name &&
               Type == other.Type &&
               LastUpdated == other.LastUpdated &&
               Attributes.Count == other.Attributes.Count &&
               Attributes.All(kvp => other.Attributes.TryGetValue(kvp.Key, out var value) && value == kvp.Value);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Type);
        hash.Add(LastUpdated);

        // Add attributes count for consistency
        hash.Add(Attributes.Count);

        return hash.ToHashCode();
    }
}

/// <summary>
/// Entity types.
/// </summary>
public enum EntityType
{
    /// <summary>A person entity.</summary>
    Person,
    /// <summary>A place entity.</summary>
    Place,
    /// <summary>An organization entity.</summary>
    Organization,
    /// <summary>A concept entity.</summary>
    Concept,
    /// <summary>A tool entity.</summary>
    Tool,
    /// <summary>Other entity type.</summary>
    Other
}
