namespace Orkeon.Domain.Memory;

/// <summary>
/// A memory item paired with its similarity score from a vector search, and — when the
/// backing store can recover it — the storage key the item was persisted under.
/// </summary>
/// <param name="Item">The matched memory item.</param>
/// <param name="Score">The similarity (or fused) score; higher is more relevant.</param>
/// <param name="Key">
/// The storage key of the item, or <see langword="null"/> when the store cannot recover it
/// (legacy search paths). Capability implementations (<see cref="IScoredVectorSearch"/>,
/// <see cref="IHybridSearchCapable"/>) populate it whenever possible.
/// </param>
public record ScoredMemoryItem(MemoryItem Item, float Score, string? Key = null);
