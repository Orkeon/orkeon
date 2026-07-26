using Orkeon.Domain.Constants.Rag;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.QueryTransform;

/// <summary>
/// Identity transformer (<c>none</c>): returns the original query unchanged as
/// the single retrieval text. Registered so <c>QueryTransform.Mode: none</c>
/// resolves through the factory like every other mode — no LLM call involved.
/// </summary>
public sealed class IdentityQueryTransformer : IQueryTransformer
{
    /// <summary>Canonical factory name (<c>none</c>).</summary>
    public const string TransformerName = RagDefaults.QueryTransformNone;

    /// <inheritdoc />
    public string Name => TransformerName;

    /// <inheritdoc />
    public QueryTransformKind Kind => QueryTransformKind.Union;

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> TransformAsync(
        string query,
        QueryTransformContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return Task.FromResult<IReadOnlyList<string>>([query]);
    }
}
