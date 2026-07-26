using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IQueryComplexityClassifier"/>: always
/// returns <see cref="Route"/> and records every classified query — forces each
/// Adaptive-RAG path deterministically.
/// </summary>
public sealed class StubQueryComplexityClassifier : IQueryComplexityClassifier
{
    /// <summary>Route returned for every query.</summary>
    public QueryRoute Route { get; set; } = QueryRoute.SingleShot;

    /// <summary>Queries received, in order.</summary>
    public List<string> Queries { get; } = [];

    public Task<QueryRoute> ClassifyAsync(string query, CancellationToken cancellationToken = default)
    {
        Queries.Add(query);
        return Task.FromResult(Route);
    }
}
