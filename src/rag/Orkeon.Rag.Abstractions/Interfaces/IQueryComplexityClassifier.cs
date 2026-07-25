namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Adaptive-RAG router (guide §8.4): classifies a query into a
/// <see cref="QueryRoute"/> (NoRetrieval, SingleShot, Iterative).
/// </summary>
public interface IQueryComplexityClassifier
{
    /// <summary>Classifies <paramref name="query"/> into a retrieval route.</summary>
    Task<QueryRoute> ClassifyAsync(
        string query,
        CancellationToken cancellationToken = default);
}
