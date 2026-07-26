using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Rag.Abstractions;

namespace Orkeon.Rag.QueryTransform;

/// <summary>
/// Multi-Query transformer (guide §6.6): one LLM call produces N alternative
/// phrasings of the question (default <c>VariantCount = 3</c>); retrieval runs
/// once per returned query and the result lists are merged by
/// <see cref="QueryTransformKind.Union"/> + dedup. The returned list is
/// <c>[original, v1…vN]</c> — the original query always comes first. An
/// unusable or failing LLM response degrades to <c>[original]</c> with a
/// warning, never an exception.
/// </summary>
public sealed class MultiQueryTransformer : LlmQueryVariantTransformerBase
{
    /// <summary>Canonical factory name (<c>multiquery</c> / <c>multi_query</c> are the registered aliases).</summary>
    public const string TransformerName = "multi-query";

    /// <summary>Creates the transformer over the host's chat client.</summary>
    public MultiQueryTransformer(IChatClient chatClient, ILogger<MultiQueryTransformer>? logger = null)
        : base(chatClient, logger)
    {
    }

    /// <inheritdoc />
    public override string Name => TransformerName;

    /// <inheritdoc />
    public override QueryTransformKind Kind => QueryTransformKind.Union;
}
