using Orkeon.Rag.Abstractions.Interfaces;

namespace Orkeon.Rag.Factories;

/// <summary>
/// Resolves <see cref="IChunkingStrategy"/> implementations by name
/// (<c>chunking.strategy: recursive</c>…). The built-in strategies
/// (recursive, sentence, structural, semantic) register here when they land
/// (RAG-02 C3); third parties register their own via <see cref="NamedRagComponentFactory{TComponent}.Register"/>.
/// </summary>
public sealed class ChunkingStrategyFactory : NamedRagComponentFactory<IChunkingStrategy>
{
    /// <inheritdoc />
    protected override string ComponentKind => "chunking strategy";
}
