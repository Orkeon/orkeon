namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Optional capability on an <see cref="IDocumentStore"/>: answers whether a
/// collection currently holds anything. Implementing it is what lets the
/// ingestion pipeline notice that its incremental manifest has outlived the
/// data the manifest describes.
/// </summary>
/// <remarks>
/// <para>
/// The problem this exists to solve, measured on 2026-08-03: the ingestion
/// manifest is persisted through the virtual file system
/// (<c>/output/rag/manifests/&lt;collection&gt;.json</c>) while the default
/// document store is backed by the host's <c>IMemoryProvider</c>, which for a
/// CLI run is in-memory. The two therefore have DIFFERENT LIFETIMES. A second
/// process ingesting the same collection loads the manifest, concludes that
/// every source is unchanged, embeds nothing — and reports success against an
/// empty store. Every subsequent query returns zero citations, with no error
/// anywhere: the ingestion report reads <c>documentsLoaded &gt; 0</c> and
/// <c>chunksCreated == 0</c>, which is also what a legitimately-incremental run
/// looks like.
/// </para>
/// <para>
/// A store that implements this interface lets the pipeline distinguish the two:
/// manifest says "unchanged" AND the store is empty ⇒ the manifest is stale, so
/// ingest in full. A store that does NOT implement it keeps the previous
/// behaviour exactly, which is correct for durable stores where the mismatch
/// cannot arise.
/// </para>
/// <para>
/// Same opt-in shape as <c>IHybridSearchCapable</c>: a capability discovered by
/// type test, never a required member on <see cref="IDocumentStore"/>.
/// </para>
/// </remarks>
public interface IDocumentStoreCollectionProbe
{
    /// <summary>
    /// True when <paramref name="collection"/> holds at least one chunk.
    /// </summary>
    /// <param name="collection">Collection name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if the collection has content, <c>false</c> if it is empty or
    /// unknown. Implementations MUST NOT throw when the collection was never
    /// created — that is the normal first-ingestion state.
    /// </returns>
    Task<bool> HasContentAsync(string collection, CancellationToken cancellationToken = default);
}
