using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// CRAG retrieval evaluator (guide §8): grades a retrieved chunk set for a query
/// as Correct, Incorrect, or Ambiguous, optionally with per-chunk relevance.
/// </summary>
public interface IRetrievalEvaluator
{
    /// <summary>Evaluates how well <paramref name="chunks"/> answer <paramref name="query"/>.</summary>
    Task<RetrievalVerdict> EvaluateAsync(
        string query,
        IReadOnlyList<ScoredChunk> chunks,
        CancellationToken cancellationToken = default);
}
