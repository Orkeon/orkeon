namespace Orkeon.Domain.Memory;

/// <summary>
/// Represents a context built from various memory sources for task execution.
/// </summary>
public sealed class MemoryContext
{
    /// <summary>
    /// Gets the recent short-term memories.
    /// </summary>
    public IReadOnlyList<MemoryItem> RecentMemories { get; }

    /// <summary>
    /// Gets task-related memories from long-term storage.
    /// </summary>
    public IReadOnlyList<MemoryItem> TaskRelatedMemories { get; }

    /// <summary>
    /// Gets relevant entity memories.
    /// </summary>
    public IReadOnlyList<EntityMemory> RelevantEntities { get; }

    /// <summary>
    /// Gets the timestamp when this context was created.
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>Initializes a new instance of <see cref="MemoryContext"/>.</summary>
    /// <param name="recentMemories">Recent short-term memories.</param>
    /// <param name="taskRelatedMemories">Task-related long-term memories.</param>
    /// <param name="relevantEntities">Relevant entity memories.</param>
    private MemoryContext(
        IEnumerable<MemoryItem> recentMemories,
        IEnumerable<MemoryItem> taskRelatedMemories,
        IEnumerable<EntityMemory> relevantEntities)
    {
        RecentMemories = recentMemories?.ToList().AsReadOnly() ?? new List<MemoryItem>().AsReadOnly();
        TaskRelatedMemories = taskRelatedMemories?.ToList().AsReadOnly() ?? new List<MemoryItem>().AsReadOnly();
        RelevantEntities = relevantEntities?.ToList().AsReadOnly() ?? new List<EntityMemory>().AsReadOnly();
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Builds a new <see cref="MemoryContext"/> from the supplied memory collections.
    /// </summary>
    /// <param name="recentMemories">Recent short-term memories.</param>
    /// <param name="taskRelatedMemories">Task-related long-term memories.</param>
    /// <param name="relevantEntities">Relevant entity memories.</param>
    public static MemoryContext Build(
        IEnumerable<MemoryItem> recentMemories,
        IEnumerable<MemoryItem> taskRelatedMemories,
        IEnumerable<EntityMemory> relevantEntities)
    {
        return new MemoryContext(recentMemories, taskRelatedMemories, relevantEntities);
    }

    /// <summary>
    /// Gets a summary of the memory context as a string.
    /// </summary>
    public string GetSummary()
    {
        var summary = new List<string>();

        if (RecentMemories.Count > 0)
        {
            summary.Add($"Recent context ({RecentMemories.Count} items):");
            summary.AddRange(RecentMemories.Select(m => $"- {m.Content}"));
        }

        if (TaskRelatedMemories.Count > 0)
        {
            summary.Add($"\nTask-related memories ({TaskRelatedMemories.Count} items):");
            summary.AddRange(TaskRelatedMemories.Select(m => $"- {m.Content}"));
        }

        if (RelevantEntities.Count > 0)
        {
            summary.Add($"\nRelevant entities ({RelevantEntities.Count}):");
            summary.AddRange(RelevantEntities.Select(e => $"- {e.Name} ({e.Type}): {e.Description}"));
        }

        return string.Join("\n", summary);
    }

    /// <summary>
    /// Checks if the context contains any memories.
    /// </summary>
    public bool HasContent =>
        RecentMemories.Count > 0 ||
        TaskRelatedMemories.Count > 0 ||
        RelevantEntities.Count > 0;

    /// <summary>
    /// Gets the total number of memory items in this context.
    /// </summary>
    public int TotalItems =>
        RecentMemories.Count +
        TaskRelatedMemories.Count +
        RelevantEntities.Count;
}
