using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Domain.Agent;

/// <summary>
/// Represents a memory entry for an agent.
/// </summary>
public sealed class AgentMemory : Entity<MemoryId>
{
    /// <summary>
    /// Gets the type of memory.
    /// </summary>
    public MemoryType Type { get; }

    /// <summary>
    /// Gets the memory content.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Gets the context associated with this memory.
    /// </summary>
    public string? Context { get; }

    /// <summary>
    /// Gets the relevance score of this memory.
    /// </summary>
    public double RelevanceScore { get; }

    /// <summary>
    /// Gets when this memory was last accessed.
    /// </summary>
    public DateTime LastAccessedAt { get; private set; }

    /// <summary>
    /// Gets the number of times this memory has been accessed.
    /// </summary>
    public int AccessCount { get; private set; }

    /// <summary>
    /// Initializes a new instance of AgentMemory.
    /// </summary>
    private AgentMemory(
        MemoryType type,
        string content,
        string? context,
        double relevanceScore)
        : base(MemoryId.Create())
    {
        Type = type;
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
        Context = context;
        RelevanceScore = relevanceScore >= 0 && relevanceScore <= 1.0
            ? relevanceScore
            : throw new ArgumentException("Relevance score must be between 0 and 1.", nameof(relevanceScore));
        LastAccessedAt = CreatedAt;
        AccessCount = 0;
    }

    /// <summary>
    /// Creates a new short-term memory.
    /// </summary>
    public static AgentMemory CreateShortTerm(string content, string? context = null, double relevanceScore = 0.5)
    {
        return new AgentMemory(MemoryType.ShortTerm, content, context, relevanceScore);
    }

    /// <summary>
    /// Creates a new long-term memory.
    /// </summary>
    public static AgentMemory CreateLongTerm(string content, string? context = null, double relevanceScore = 0.8)
    {
        return new AgentMemory(MemoryType.LongTerm, content, context, relevanceScore);
    }

    /// <summary>
    /// Creates a new episodic memory.
    /// </summary>
    public static AgentMemory CreateEpisodic(string content, string? context = null, double relevanceScore = SearchDefaults.DefaultSimilarityThreshold)
    {
        return new AgentMemory(MemoryType.Episodic, content, context, relevanceScore);
    }

    /// <summary>
    /// Records that this memory was accessed.
    /// </summary>
    internal void RecordAccess()
    {
        LastAccessedAt = DateTime.UtcNow;
        AccessCount++;
    }

    /// <summary>
    /// Determines if this memory should be retained based on age and usage.
    /// </summary>
    internal bool ShouldRetain(TimeSpan maxAge, int minAccessCount = 1)
    {
        var age = DateTime.UtcNow - CreatedAt;

        // Long-term memories are always retained
        if (Type == MemoryType.LongTerm)
            return true;

        // Keep if accessed frequently enough
        if (AccessCount >= minAccessCount)
            return true;

        // Keep if recent enough
        if (age < maxAge)
            return true;

        // Keep if highly relevant
        if (RelevanceScore >= 0.8)
            return true;

        return false;
    }
}

/// <summary>
/// Types of agent memory.
/// </summary>
public enum MemoryType
{
    /// <summary>
    /// Short-term working memory.
    /// </summary>
    ShortTerm,

    /// <summary>
    /// Long-term persistent memory.
    /// </summary>
    LongTerm,

    /// <summary>
    /// Episodic memory of specific events.
    /// </summary>
    Episodic
}
