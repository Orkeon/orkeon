namespace Orkeon.Domain.Memory;

/// <summary>
/// Result of LLM-based analysis of memory content, providing importance scoring,
/// categorization, entity extraction, and summarization.
/// </summary>
public sealed record MemoryAnalysis
{
    /// <summary>Importance score from 0.0 (trivial) to 1.0 (critical).</summary>
    public float Importance { get; init; }

    /// <summary>Category of the memory content (e.g., "fact", "decision", "observation").</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Key entities mentioned in the content.</summary>
    public IReadOnlyList<string> KeyEntities { get; init; } = Array.Empty<string>();

    /// <summary>A concise summary of the content.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Tags suggested by the LLM for categorization.</summary>
    public IReadOnlyList<string> SuggestedTags { get; init; } = Array.Empty<string>();

    /// <summary>The LLM's reasoning for its analysis decisions.</summary>
    public string Reasoning { get; init; } = string.Empty;
}

/// <summary>
/// Result of checking new content against existing memories for contradictions.
/// </summary>
public sealed record ContradictionCheck
{
    /// <summary>Whether a contradiction was detected.</summary>
    public bool HasContradiction { get; init; }

    /// <summary>IDs of existing memories that conflict with the new content.</summary>
    public IReadOnlyList<string> ConflictingMemoryIds { get; init; } = Array.Empty<string>();

    /// <summary>Description of the contradiction, if any.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Suggested resolution for the contradiction.</summary>
    public string Resolution { get; init; } = string.Empty;

    /// <summary>Recommended action to resolve the contradiction.</summary>
    public ConflictResolution RecommendedAction { get; init; } = ConflictResolution.KeepBoth;
}

/// <summary>
/// Strategy for resolving a conflict between new and existing memories.
/// </summary>
public enum ConflictResolution
{
    /// <summary>Replace existing memories with the new content.</summary>
    KeepNew,

    /// <summary>Keep the existing memories and discard the new content.</summary>
    KeepExisting,

    /// <summary>Merge the new content with existing memories.</summary>
    Merge,

    /// <summary>Keep both the new and existing memories.</summary>
    KeepBoth
}

/// <summary>
/// A memory item enriched with composite scoring from multiple dimensions.
/// </summary>
public sealed record ScoredMemory
{
    /// <summary>The underlying memory item.</summary>
    public MemoryItem Item { get; init; } = null!;

    /// <summary>Final composite score combining all dimensions.</summary>
    public float CompositeScore { get; init; }

    /// <summary>Score from semantic similarity search.</summary>
    public float SemanticScore { get; init; }

    /// <summary>Score based on temporal recency (exponential decay).</summary>
    public float RecencyScore { get; init; }

    /// <summary>Score based on the memory's importance rating.</summary>
    public float ImportanceScore { get; init; }
}

/// <summary>
/// Options for controlling how memories are recalled (retrieved and ranked).
/// </summary>
public sealed record RecallOptions
{
    /// <summary>Weight for semantic similarity in composite scoring. Default: 0.5.</summary>
    public float SemanticWeight { get; init; } = 0.5f;

    /// <summary>Weight for temporal recency in composite scoring. Default: 0.3.</summary>
    public float RecencyWeight { get; init; } = 0.3f;

    /// <summary>Weight for importance in composite scoring. Default: 0.2.</summary>
    public float ImportanceWeight { get; init; } = 0.2f;

    /// <summary>Maximum number of results to return. Default: 10.</summary>
    public int TopK { get; init; } = 10;

    /// <summary>Minimum composite score threshold. Default: 0.1.</summary>
    public float MinScore { get; init; } = 0.1f;

    /// <summary>Optional filter by memory type.</summary>
    public MemoryType? TypeFilter { get; init; }

    /// <summary>Optional filter by tags (any match).</summary>
    public IReadOnlyList<string>? TagFilter { get; init; }
}

/// <summary>
/// Result of a memory consolidation operation.
/// </summary>
public sealed record ConsolidationResult
{
    /// <summary>Number of memory clusters that were merged.</summary>
    public int MergedCount { get; init; }

    /// <summary>Number of memories pruned (removed as low value).</summary>
    public int PrunedCount { get; init; }

    /// <summary>Number of memories that remained unchanged.</summary>
    public int UnchangedCount { get; init; }

    /// <summary>IDs of newly created merged memories.</summary>
    public IReadOnlyList<string> CreatedMemoryIds { get; init; } = Array.Empty<string>();
}
