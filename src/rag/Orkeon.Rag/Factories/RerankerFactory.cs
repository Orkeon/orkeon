using Orkeon.Rag.Abstractions.Interfaces;

namespace Orkeon.Rag.Factories;

/// <summary>
/// Resolves <see cref="IReranker"/> implementations by name
/// (<c>rerank.kind: onnx</c>…). The built-in rerankers (onnx, llm, none)
/// register here when they land (RAG-04); third parties register their own via
/// <see cref="NamedRagComponentFactory{TComponent}.Register"/>.
/// </summary>
public sealed class RerankerFactory : NamedRagComponentFactory<IReranker>
{
    /// <inheritdoc />
    protected override string ComponentKind => "reranker";
}
