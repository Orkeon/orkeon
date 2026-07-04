namespace Orkeon.Domain.Memory;

/// <summary>
/// A memory item paired with its similarity score from a vector search.
/// </summary>
public record ScoredMemoryItem(MemoryItem Item, float Score);
