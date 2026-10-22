using System.Collections.Concurrent;
using Orkeon.Rag.Ingestion;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>Thread-safe in-memory <see cref="IIngestionManifestStore"/> for concurrency tests.</summary>
public sealed class InMemoryIngestionManifestStore : IIngestionManifestStore
{
    private readonly ConcurrentDictionary<string, IngestionManifest> _manifests = new(StringComparer.Ordinal);

    public Task<IngestionManifest?> LoadAsync(string collection, CancellationToken cancellationToken = default) =>
        Task.FromResult(_manifests.TryGetValue(collection, out var manifest) ? manifest : null);

    public Task SaveAsync(IngestionManifest manifest, CancellationToken cancellationToken = default)
    {
        _manifests[manifest.Collection] = manifest;
        return Task.CompletedTask;
    }
}
