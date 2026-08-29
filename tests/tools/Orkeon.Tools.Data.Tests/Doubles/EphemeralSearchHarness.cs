using Orkeon.Application.Interfaces.Ports;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Chunking;
using Orkeon.Rag.Ingestion;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.Search;
using Orkeon.Rag.Validation;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using DomainEmbeddingService = Orkeon.Domain.Memory.IEmbeddingService;

namespace Orkeon.Tools.Data.Tests.Doubles;

/// <summary>
/// Builds a real <see cref="EphemeralCollectionSearchService"/> over in-memory
/// collaborators (<see cref="FakeDocumentStore"/>, fake VFS manifests, the
/// canonical chunking strategies) so the search-tool facades are exercised
/// end-to-end while embeddings stay under the test's control via
/// <see cref="MockEmbeddingService"/>.
/// </summary>
internal static class EphemeralSearchHarness
{
    /// <summary>Creates the shared search engine backed by <paramref name="embeddingService"/>.</summary>
    public static IEphemeralCollectionSearch Create(DomainEmbeddingService embeddingService)
    {
        var embeddings = new StubEmbeddingProvider(embeddingService);
        var store = new FakeDocumentStore();
        var manifestFs = new FakeFileSystemService().AddMount("/output");
        var options = new RagIngestionOptions();

        var pipeline = new DefaultIngestionPipeline(
            new DocumentLoaderFactory([new InlineTextLoader()]),
            ChunkingStrategyFactoryDefaults.CreateDefault(),
            embeddings,
            store,
            new DataValidationPipeline([], new InMemoryQuarantineStore(), new ProvenanceTracker()),
            new FileIngestionManifestStore(manifestFs, options),
            options);

        return new EphemeralCollectionSearchService(pipeline, store, embeddings);
    }

    /// <summary>
    /// Adapter double bridging the Application <see cref="IEmbeddingProvider"/> port
    /// onto the test's Domain <see cref="DomainEmbeddingService"/> double, with a
    /// stable model identity (manifest drift detection requires it constant).
    /// </summary>
    private sealed class StubEmbeddingProvider(DomainEmbeddingService inner) : IEmbeddingProvider
    {
        public string Name => "test-embeddings";
        public string Model => "test-embedding-model";
        public int Dimensions => 3;

        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => inner.GetEmbeddingAsync(text, cancellationToken);

        public async Task<IList<float[]>> GetEmbeddingsAsync(
            IList<string> texts, CancellationToken cancellationToken = default)
        {
            var result = new List<float[]>(texts.Count);
            foreach (var text in texts)
                result.Add(await inner.GetEmbeddingAsync(text, cancellationToken));
            return result;
        }
    }
}
