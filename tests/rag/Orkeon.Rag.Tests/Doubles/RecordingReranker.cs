using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IReranker"/> double: records every call
/// (query, candidate count, topN) and returns the candidates in
/// <b>reversed</b> order truncated to topN — so tests can prove the pipeline
/// actually uses the reranker's ordering, not the retrieval order.
/// </summary>
public sealed class RecordingReranker : IReranker
{
    /// <summary>Recorded call.</summary>
    public sealed record Call(string Query, int CandidateCount, int TopN);

    /// <summary>Calls received, in order.</summary>
    public List<Call> Calls { get; } = [];

    /// <inheritdoc />
    public string Name => "recording";

    /// <inheritdoc />
    public Task<IReadOnlyList<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topN,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new Call(query, candidates.Count, topN));

        IReadOnlyList<ScoredChunk> reranked = candidates
            .Reverse()
            .Take(topN)
            .Select((scored, index) => scored with
            {
                Score = 1.0 - (index * 0.01),
                ScoreOrigin = "recording",
            })
            .ToList();

        return Task.FromResult(reranked);
    }
}
