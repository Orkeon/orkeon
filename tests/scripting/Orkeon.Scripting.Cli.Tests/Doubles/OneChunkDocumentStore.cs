using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IDocumentStore"/> holding one passage, found by every search in
/// every collection — enough for a RAG pipeline to retrieve context and ask for a grounded
/// answer, without embeddings worth the name.
/// </summary>
internal sealed class OneChunkDocumentStore(string content) : IDocumentStore
{
    public Task UpsertAsync(string collection, IReadOnlyList<EmbeddedChunk> chunks, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<ScoredChunk>> SearchAsync(string collection, RetrievalQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ScoredChunk>>(
        [
            new ScoredChunk
            {
                Chunk = new Chunk { Id = "warranty", DocumentId = "handbook", SourceId = "/kb/handbook.md", Content = content },
                Score = 0.9,
            },
        ]);

    public Task DeleteBySourceAsync(string collection, string sourceId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
