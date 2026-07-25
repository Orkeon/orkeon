namespace Orkeon.Rag.Abstractions;

/// <summary>
/// CRAG retrieval verdict grade (guide §8): quality of a retrieved chunk set for a query.
/// </summary>
public enum RetrievalGrade
{
    /// <summary>The retrieved chunks answer the query; proceed to refinement/generation.</summary>
    Correct,

    /// <summary>The retrieved chunks do not answer the query; rewrite the query (or fall back).</summary>
    Incorrect,

    /// <summary>The verdict is uncertain; refine and optionally augment with a fallback source.</summary>
    Ambiguous,
}

/// <summary>
/// Adaptive-RAG routing decision (guide §8.4) produced by
/// <see cref="Interfaces.IQueryComplexityClassifier"/>.
/// </summary>
public enum QueryRoute
{
    /// <summary>The query needs no retrieval; answer directly from the model.</summary>
    NoRetrieval,

    /// <summary>A single retrieval pass suffices (linear pipeline).</summary>
    SingleShot,

    /// <summary>The query needs iterative/corrective retrieval (graph engine).</summary>
    Iterative,
}
