using System.Collections.Immutable;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written retrieval-capable <see cref="IRagPipeline"/>: <see cref="RetrieveAsync"/>
/// returns the citations scripted in <see cref="Citations"/> and records every query;
/// <see cref="QueryAsync"/> (generation) is counted so a test can assert it was never
/// called.
/// </summary>
public sealed class FakeRetrievalPipeline : IRagPipeline, IRagRetrievalCapable
{
    /// <summary>Citations returned by every retrieval.</summary>
    public ImmutableList<Citation> Citations { get; init; } = ImmutableList<Citation>.Empty;

    /// <summary>Retrieval-only queries received, in order.</summary>
    public List<RagQuery> RetrieveCalls { get; } = [];

    /// <summary>Number of generating queries received.</summary>
    public int QueryCalls { get; private set; }

    public Task<RagAnswer> QueryAsync(RagQuery query, CancellationToken cancellationToken = default)
    {
        QueryCalls++;
        return Task.FromResult(new RagAnswer { Text = "generated", Citations = Citations });
    }

    public Task<RagAnswer> RetrieveAsync(RagQuery query, CancellationToken cancellationToken = default)
    {
        RetrieveCalls.Add(query);
        return Task.FromResult(new RagAnswer { Text = string.Empty, Citations = Citations });
    }

    /// <summary>Builds a citation whose full passage is <paramref name="content"/>.</summary>
    public static Citation Cite(int marker, string chunkId, string content, double score = 0.9) => new()
    {
        Marker = marker,
        ChunkId = chunkId,
        SourceId = chunkId + ".md",
        Snippet = content,
        Content = content,
        Score = score,
        EndOffset = content.Length,
    };
}
