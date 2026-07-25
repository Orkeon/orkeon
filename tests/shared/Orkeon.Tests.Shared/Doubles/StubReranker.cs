using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Tests.Shared.Doubles;

/// <summary>
/// Hand-rolled <see cref="IReranker"/> stub. By default returns the candidates in
/// their incoming order (truncated to <c>topN</c>); set <see cref="ReverseOrder"/>
/// to return them reversed instead. Records every call for inspection.
/// </summary>
public sealed class StubReranker : IReranker
{
    /// <inheritdoc />
    public string Name { get; init; } = "stub";

    /// <summary>When <c>true</c>, candidates are returned in reversed order.</summary>
    public bool ReverseOrder { get; set; }

    /// <summary>Queries received by <see cref="RerankAsync"/>, in call order.</summary>
    public List<string> Queries { get; } = new();

    /// <summary>The <c>topN</c> received by the last <see cref="RerankAsync"/> call.</summary>
    public int? LastTopN { get; private set; }

    /// <summary>The candidates received by the last <see cref="RerankAsync"/> call.</summary>
    public IReadOnlyList<ScoredChunk>? LastCandidates { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topN,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(candidates);

        Queries.Add(query);
        LastTopN = topN;
        LastCandidates = candidates;

        IEnumerable<ScoredChunk> ordered = ReverseOrder ? candidates.Reverse() : candidates;
        IReadOnlyList<ScoredChunk> result = ordered.Take(topN).ToList();
        return Task.FromResult(result);
    }
}
