namespace Orkeon.Domain.Memory;

/// <summary>
/// A single entry of a batch upsert (<see cref="IBatchUpsert"/>): the storage key, the item,
/// and an optional embedding vector to associate with the item at write time.
/// </summary>
/// <param name="Key">The storage key (non-empty).</param>
/// <param name="Item">The memory item to upsert.</param>
/// <param name="Embedding">
/// Optional embedding vector. When provided it replaces the item's embedding before
/// persistence (same contract as <see cref="IMemoryProvider.StoreWithEmbeddingAsync"/>);
/// when <see langword="null"/> the item's existing <see cref="MemoryItem.Embedding"/> is kept.
/// </param>
public sealed record MemoryUpsertEntry(string Key, MemoryItem Item, ReadOnlyMemory<float>? Embedding = null);
