using Orkeon.Domain.Common;

namespace Orkeon.Domain.Memory.ValueObjects;

/// <summary>
/// Groups access-tracking fields for memory metadata.
/// </summary>
public sealed record MemoryAccessInfo(
    DateTime CreatedAt,
    DateTime? LastAccessedAt,
    int AccessCount = 0);

/// <summary>
/// Strongly typed memory metadata.
/// </summary>
public sealed record MemoryMetadata : ValueObjectRecord
{
    /// <summary>Gets the access information.</summary>
    public MemoryAccessInfo Access { get; init; }
    /// <summary>Gets the source of the memory.</summary>
    public string Source { get; init; }
    /// <summary>Gets the relevance score.</summary>
    public double Relevance { get; init; }
    /// <summary>Gets the tags, or <see langword="null"/> if none.</summary>
    public IReadOnlyList<string>? Tags { get; init; }
    /// <summary>Gets the identifier of the agent that created this memory.</summary>
    public AgentId? CreatedBy { get; init; }
    /// <summary>Gets custom properties, or <see langword="null"/> if none.</summary>
    public Dictionary<string, string>? CustomProperties { get; init; }

    // Backward-compatible accessors
    /// <summary>When the memory was created.</summary>
    public DateTime CreatedAt => Access.CreatedAt;
    /// <summary>When the memory was last accessed.</summary>
    public DateTime? LastAccessedAt => Access.LastAccessedAt;
    /// <summary>Number of times the memory has been accessed.</summary>
    public int AccessCount => Access.AccessCount;

    /// <summary>Primary private constructor.</summary>
    private MemoryMetadata(
        MemoryAccessInfo access,
        string source,
        double relevance,
        IReadOnlyList<string>? tags,
        AgentId? createdBy,
        Dictionary<string, string>? customProperties)
    {
        ArgumentNullException.ThrowIfNull(access);
        Access = access;
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        Relevance = relevance;
        Tags = tags;
        CreatedBy = createdBy;
        CustomProperties = customProperties;
    }

    /// <summary>
    /// Creates a new <see cref="MemoryMetadata"/> instance.
    /// </summary>
#pragma warning disable S107 // Backward-compatible overload; prefer Create(MemoryAccessInfo, ...) instead
    public static MemoryMetadata Create(
        DateTime createdAt,
        DateTime? lastAccessedAt,
        string source,
        double relevance,
        IReadOnlyList<string>? tags = null,
        AgentId? createdBy = null,
        int accessCount = 0,
        Dictionary<string, string>? customProperties = null)
        => new(
            new MemoryAccessInfo(createdAt, lastAccessedAt, accessCount),
            source, relevance, tags, createdBy, customProperties);
#pragma warning restore S107

    /// <summary>
    /// Creates a new <see cref="MemoryMetadata"/> instance with <see cref="MemoryAccessInfo"/>.
    /// </summary>
    public static MemoryMetadata Create(
        MemoryAccessInfo access,
        string source,
        double relevance,
        IReadOnlyList<string>? tags = null,
        AgentId? createdBy = null,
        Dictionary<string, string>? customProperties = null)
        => new(access, source, relevance, tags, createdBy, customProperties);

    /// <summary>
    /// Increments the access count and updates last accessed time.
    /// </summary>
    public MemoryMetadata IncrementAccess() =>
        Create(
            Access with
            {
                AccessCount = Access.AccessCount + 1,
                LastAccessedAt = DateTime.UtcNow
            },
            Source, Relevance, Tags, CreatedBy, CustomProperties);
}
