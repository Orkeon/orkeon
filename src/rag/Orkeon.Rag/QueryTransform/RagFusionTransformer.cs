using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Rag.Abstractions;

namespace Orkeon.Rag.QueryTransform;

/// <summary>
/// RAG-Fusion transformer (guide §6.6): generates the same variants as
/// <see cref="MultiQueryTransformer"/> (shared base — <c>[original, v1…vN]</c>,
/// original first), but its retrieve-stage CONTRACT differs:
/// <see cref="Kind"/> is <see cref="QueryTransformKind.Fusion"/>, meaning the
/// per-variant result lists MUST be fused by Reciprocal Rank Fusion
/// (<c>score(d) = Σ 1/(k + rank_i(d))</c>, k = 60) instead of a plain union.
/// The fusion itself is the pipeline's responsibility (RAG-05 integration lot
/// wires the retrieve stage on <see cref="Kind"/>) — this class only produces
/// the queries and exposes the fusion semantics.
/// </summary>
public sealed class RagFusionTransformer : LlmQueryVariantTransformerBase
{
    /// <summary>Canonical factory name (<c>ragfusion</c> / <c>rag_fusion</c> are the registered aliases).</summary>
    public const string TransformerName = "rag-fusion";

    /// <summary>Creates the transformer over the host's chat client.</summary>
    public RagFusionTransformer(IChatClient chatClient, ILogger<RagFusionTransformer>? logger = null)
        : base(chatClient, logger)
    {
    }

    /// <inheritdoc />
    public override string Name => TransformerName;

    /// <inheritdoc />
    public override QueryTransformKind Kind => QueryTransformKind.Fusion;
}
