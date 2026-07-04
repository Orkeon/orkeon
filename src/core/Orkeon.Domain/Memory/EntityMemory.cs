namespace Orkeon.Domain.Memory;

/// <summary>
/// Represents memory about a specific entity (person, place, concept, etc.).
/// </summary>
public sealed class EntityMemory
{
    /// <summary>
    /// Gets the entity name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the entity type (e.g., "person", "organization", "location", "concept").
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Gets the entity description.
    /// </summary>
    public string Description { get; private set; }

    /// <summary>
    /// Gets the entity attributes.
    /// </summary>
    public IReadOnlyDictionary<string, string> Attributes => _attributes;

    private readonly Dictionary<string, string> _attributes = [];

    /// <summary>
    /// Gets the relationships with other entities.
    /// </summary>
    public IReadOnlyList<EntityRelationship> Relationships => _relationships.AsReadOnly();

    private readonly List<EntityRelationship> _relationships;

    /// <summary>
    /// Gets when this entity was first encountered.
    /// </summary>
    public DateTime FirstEncountered { get; }

    /// <summary>
    /// Gets when this entity was last updated.
    /// </summary>
    public DateTime LastUpdated { get; private set; }

    /// <summary>
    /// Gets the confidence score for this entity information (0-1).
    /// </summary>
    public float Confidence { get; private set; }

    /// <summary>Initializes a new instance of <see cref="EntityMemory"/>.</summary>
    /// <param name="name">The entity name.</param>
    /// <param name="type">The entity type.</param>
    /// <param name="description">The entity description.</param>
    /// <param name="confidence">The confidence score (0.0 to 1.0).</param>
    internal EntityMemory(
        string name,
        string type,
        string description = "",
        float confidence = 0.8f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        if (confidence < 0 || confidence > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0 and 1");

        Name = name;
        Type = type;
        Description = description ?? string.Empty;
        Confidence = confidence;
        _attributes = [];
        _relationships = [];
        FirstEncountered = DateTime.UtcNow;
        LastUpdated = FirstEncountered;
    }

    /// <summary>
    /// Updates the entity description.
    /// </summary>
    internal void UpdateDescription(string description)
    {
        Description = description ?? string.Empty;
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds or updates an attribute.
    /// </summary>
    internal void SetAttribute(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _attributes[key] = value ?? string.Empty;
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds a relationship to another entity.
    /// </summary>
    internal void AddRelationship(EntityRelationship relationship)
    {
        ArgumentNullException.ThrowIfNull(relationship);

        _relationships.Add(relationship);
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the confidence score.
    /// </summary>
    internal void UpdateConfidence(float confidence)
    {
        if (confidence < 0 || confidence > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0 and 1");

        Confidence = confidence;
        LastUpdated = DateTime.UtcNow;
    }
}

/// <summary>
/// Represents a relationship between entities.
/// </summary>
public sealed class EntityRelationship
{
    /// <summary>
    /// Gets the related entity name.
    /// </summary>
    public string RelatedEntityName { get; }

    /// <summary>
    /// Gets the relationship type (e.g., "works_for", "located_in", "knows").
    /// </summary>
    public string RelationType { get; }

    /// <summary>
    /// Gets additional context about the relationship.
    /// </summary>
    public string? Context { get; }

    /// <summary>
    /// Gets when this relationship was established.
    /// </summary>
    public DateTime EstablishedAt { get; }

    /// <summary>Initializes a new instance of <see cref="EntityRelationship"/>.</summary>
    /// <param name="relatedEntityName">The related entity name.</param>
    /// <param name="relationType">The relationship type.</param>
    /// <param name="context">Optional context about the relationship.</param>
    internal EntityRelationship(
        string relatedEntityName,
        string relationType,
        string? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relatedEntityName);

        ArgumentException.ThrowIfNullOrWhiteSpace(relationType);

        RelatedEntityName = relatedEntityName;
        RelationType = relationType;
        Context = context;
        EstablishedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates a new <see cref="EntityRelationship"/> with validated parameters.
    /// </summary>
    /// <param name="relatedEntity">The related entity name.</param>
    /// <param name="relationType">The relationship type.</param>
    /// <param name="context">Optional context about the relationship.</param>
    public static EntityRelationship Create(
        string relatedEntity,
        string relationType,
        string? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relatedEntity);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationType);

        return new EntityRelationship(relatedEntity, relationType, context);
    }
}
