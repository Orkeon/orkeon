using Orkeon.Rag.Abstractions.Interfaces;

namespace Orkeon.Rag.Factories;

/// <summary>
/// Resolves <see cref="IQueryTransformer"/> implementations by name
/// (<c>transform.kind: multi-query</c>…). The built-in transformers
/// (multi-query, rag-fusion, hyde) register here when they land (RAG-05);
/// third parties register their own via
/// <see cref="NamedRagComponentFactory{TComponent}.Register"/>.
/// </summary>
public sealed class QueryTransformerFactory : NamedRagComponentFactory<IQueryTransformer>
{
    /// <inheritdoc />
    protected override string ComponentKind => "query transformer";
}
