using Orkeon.Rag.Abstractions.Interfaces;

namespace Orkeon.Rag.Factories;

/// <summary>
/// Resolves <see cref="IReranker"/> implementations by name
/// (<c>rerank.kind: onnx</c>…). The built-in rerankers register here at
/// composition time (<c>llm</c>/<c>none</c> via <c>AddOrkeonRag</c>, <c>onnx</c>
/// via the opt-in <c>AddOrkeonOnnxReranker()</c>); third parties register their
/// own via <see cref="NamedRagComponentFactory{TComponent}.Register"/>.
/// </summary>
public sealed class RerankerFactory : NamedRagComponentFactory<IReranker>
{
    /// <inheritdoc />
    protected override string ComponentKind => "reranker";
}
