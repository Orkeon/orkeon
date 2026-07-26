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
/// Retrieval semantics of the query list returned by an
/// <see cref="Interfaces.IQueryTransformer"/> (RAG-05, guide §6): how the
/// pipeline's retrieve stage must combine the per-query result lists.
/// </summary>
public enum QueryTransformKind
{
    /// <summary>
    /// The result lists retrieved for each returned query are merged by union +
    /// dedup (Multi-Query, and the identity transformer). This is the default.
    /// </summary>
    Union,

    /// <summary>
    /// The result lists retrieved for each returned query are fused by
    /// Reciprocal Rank Fusion (RAG-Fusion — <c>score(d) = Σ 1/(k + rank_i(d))</c>).
    /// </summary>
    Fusion,

    /// <summary>
    /// The returned text replaces the original question as the embedded
    /// retrieval probe (HyDE: the hypothetical document is embedded instead of
    /// the query; the original question is not retrieved with).
    /// </summary>
    Replacement,
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
