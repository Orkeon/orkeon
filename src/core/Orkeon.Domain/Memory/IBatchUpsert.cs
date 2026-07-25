namespace Orkeon.Domain.Memory;

/// <summary>
/// Optional capability of an <see cref="IMemoryProvider"/>: batched upsert of memory items
/// (key + item + optional embedding) in a single operation (RAG-02/C4, plan §4.2).
/// </summary>
/// <remarks>
/// <para>
/// Capability interfaces are opt-in: a provider implements this interface only when it can
/// honour the contract natively. Consumers discover capabilities with the
/// <see cref="MemoryCapabilityExtensions.TryGetCapability{TCapability}"/> extension (which is
/// decorator-aware) or, on a concrete provider, plain pattern matching. Providers without
/// this capability are handled by consumers with per-item
/// <see cref="IMemoryProvider.StoreAsync"/> loops.
/// </para>
/// <para>
/// Atomicity contract ("reasonable atomicity"): implementations validate all entries before
/// writing anything, so an invalid entry never results in a partially applied batch.
/// Transactional stores (e.g. SQLite) additionally apply the whole batch in a single
/// transaction; non-transactional stores may be observed mid-batch by concurrent readers.
/// </para>
/// </remarks>
public interface IBatchUpsert
{
    /// <summary>
    /// Inserts or updates all <paramref name="entries"/> (upsert-by-key).
    /// </summary>
    /// <param name="entries">The batch entries; every key must be non-empty and every item non-null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">An entry has a null/whitespace key or invalid item — nothing is written.</exception>
    System.Threading.Tasks.Task UpsertBatchAsync(
        IReadOnlyList<MemoryUpsertEntry> entries,
        CancellationToken cancellationToken = default);
}
