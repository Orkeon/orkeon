using Orkeon.Domain.Common;
using Orkeon.Domain.Memory.ValueObjects;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Domain.Memory;

/// <summary>
/// Represents a single memory item stored in the memory system.
/// </summary>
public sealed class MemoryItem : Entity<MemoryItemId>
{
    /// <summary>
    /// Gets the content of the memory.
    /// </summary>
    public string Content { get; }

    private float[]? _embedding;

    /// <summary>
    /// Gets the embedding vector for similarity search.
    /// </summary>
    public IReadOnlyList<float>? Embedding => _embedding;

    /// <summary>
    /// Gets the importance score of this memory (0-1).
    /// </summary>
    public float Importance { get; private set; }

    /// <summary>
    /// Gets the memory metadata.
    /// </summary>
    public MemoryMetadata Metadata { get; private set; }

    /// <summary>
    /// Gets the number of times this memory has been accessed.
    /// </summary>
    public int AccessCount => Metadata.AccessCount;

    /// <summary>
    /// Gets the timestamp when this memory was created.
    /// </summary>
    public DateTime Timestamp => Metadata.CreatedAt;

    /// <summary>
    /// Gets the last access timestamp.
    /// </summary>
    public DateTime LastAccessed => Metadata.LastAccessedAt ?? Metadata.CreatedAt;

    /// <summary>
    /// Gets the tags associated with this memory.
    /// </summary>
    public IReadOnlyList<string> Tags => Metadata.Tags ?? [];

    /// <summary>
    /// Gets the source of this memory.
    /// </summary>
    public string Source => Metadata.Source;

    private MemoryItem(
        string content,
        IReadOnlyList<float>? embedding,
        float importance,
        string? source,
        IReadOnlyList<string>? tags,
        AgentId? createdBy,
        Dictionary<string, string>? customProperties)
        : base(MemoryItemId.Create())
    {
        Content = content;
        _embedding = embedding switch
        {
            null => null,
            float[] array => array,
            _ => [.. embedding],
        };
        Importance = importance;

        Metadata = MemoryMetadata.Create(
            createdAt: DateTime.UtcNow,
            lastAccessedAt: null,
            source: source ?? "unknown",
            relevance: importance,
            tags: tags,
            createdBy: createdBy,
            accessCount: 0,
            customProperties: customProperties
        );
    }

    private MemoryItem(
        MemoryItemId id,
        string content,
        IReadOnlyList<float>? embedding,
        float importance,
        MemoryMetadata metadata)
        : base(id)
    {
        Content = content;
        _embedding = embedding switch
        {
            null => null,
            float[] array => array,
            _ => [.. embedding],
        };
        Importance = importance;
        Metadata = metadata;
    }

    /// <summary>
    /// Creates a new <see cref="MemoryItem"/> with validated parameters.
    /// </summary>
    /// <param name="content">The memory content text.</param>
    /// <param name="embedding">The optional embedding vector.</param>
    /// <param name="importance">The importance score (0.0 to 1.0).</param>
    /// <param name="source">The optional source identifier.</param>
    /// <param name="tags">Optional tags for categorization.</param>
    /// <param name="createdBy">The optional agent that created this memory.</param>
    /// <param name="customProperties">Optional custom properties.</param>
    public static MemoryItem Create(
        string content,
        IReadOnlyList<float>? embedding = null,
        float importance = MemoryDefaults.DefaultImportance,
        string? source = null,
        IReadOnlyList<string>? tags = null,
        AgentId? createdBy = null,
        Dictionary<string, string>? customProperties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        if (importance < 0 || importance > 1)
            throw new ArgumentOutOfRangeException(nameof(importance), "Importance must be between 0 and 1");

        return new MemoryItem(content, embedding, importance, source, tags, createdBy, customProperties);
    }

    /// <summary>
    /// Rehydrates a <see cref="MemoryItem"/> from persistence without raising domain events.
    /// Use this factory when loading an existing item from a database or external store.
    /// </summary>
    internal static MemoryItem Restore(
        MemoryItemId id,
        string content,
        IReadOnlyList<float>? embedding,
        float importance,
        MemoryMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentNullException.ThrowIfNull(metadata);

        return new MemoryItem(id, content, embedding, importance, metadata);
    }

    /// <summary>
    /// Sets the embedding vector for this memory item.
    /// </summary>
    /// <param name="embedding">The embedding vector to set.</param>
    internal void SetEmbedding(float[]? embedding)
    {
        _embedding = embedding;
    }

    /// <summary>
    /// Increments the access count and updates last accessed time.
    /// </summary>
    internal void IncrementAccessCount()
    {
        Metadata = Metadata.IncrementAccess();
    }

    /// <summary>
    /// Updates the importance score.
    /// </summary>
    internal void UpdateImportance(float newImportance)
    {
        if (newImportance < 0 || newImportance > 1)
            throw new ArgumentOutOfRangeException(nameof(newImportance), "Importance must be between 0 and 1");

        Importance = newImportance;
    }

    /// <summary>
    /// Adds a tag.
    /// </summary>
    internal void AddTag(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        var currentTags = Metadata.Tags?.ToList() ?? [];
        if (!currentTags.Contains(tag))
        {
            currentTags.Add(tag);
            Metadata = Metadata with { Tags = currentTags.ToArray() };
        }
    }

    /// <summary>
    /// Determines whether this memory item has high enough importance to be promoted to long-term storage.
    /// </summary>
    internal bool ShouldPromoteToLongTerm()
    {
        return Importance > (float)SearchDefaults.DefaultSimilarityThreshold;
    }

    /// <summary>
    /// Adds or updates custom metadata property.
    /// </summary>
    internal void AddCustomProperty(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var props = Metadata.CustomProperties != null
            ? new Dictionary<string, string>(Metadata.CustomProperties)
            : [];

        props[key] = value ?? string.Empty;
        Metadata = Metadata with { CustomProperties = props };
    }
}
