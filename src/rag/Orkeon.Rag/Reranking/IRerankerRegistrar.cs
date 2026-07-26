using Orkeon.Rag.Factories;

namespace Orkeon.Rag.Reranking;

/// <summary>
/// Contribution point for optional reranker packages (e.g. <c>Orkeon.Rag.Onnx</c>):
/// implementations registered in DI are applied when the singleton
/// <see cref="RerankerFactory"/> is built, after the built-in rerankers
/// (<c>none</c>/<c>noop</c>, <c>llm</c>/<c>listwise</c>) — registration order in
/// the service collection is irrelevant.
/// </summary>
public interface IRerankerRegistrar
{
    /// <summary>Registers this contributor's rerankers on <paramref name="factory"/>.</summary>
    /// <param name="factory">The factory being populated.</param>
    /// <param name="serviceProvider">Root provider for resolving the reranker's collaborators.</param>
    void Register(RerankerFactory factory, IServiceProvider serviceProvider);
}
