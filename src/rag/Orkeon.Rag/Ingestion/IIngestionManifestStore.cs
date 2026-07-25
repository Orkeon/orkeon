namespace Orkeon.Rag.Ingestion;

/// <summary>
/// Persistence of per-collection <see cref="IngestionManifest"/>s (RAG-03/C1).
/// The default implementation writes one JSON file per collection through the
/// VFS (<see cref="FileIngestionManifestStore"/>).
/// </summary>
public interface IIngestionManifestStore
{
    /// <summary>
    /// Loads the manifest of <paramref name="collection"/>. Returns <c>null</c>
    /// when the manifest is absent, unreadable, or corrupt — a <c>null</c>
    /// manifest means "no incremental state": the pipeline falls back to a full
    /// ingestion, never a crash.
    /// </summary>
    Task<IngestionManifest?> LoadAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>Persists <paramref name="manifest"/> for its collection, overwriting any previous version.</summary>
    Task SaveAsync(IngestionManifest manifest, CancellationToken cancellationToken = default);
}
