using Orkeon.Rag.Factories;

namespace Orkeon.Rag.Chunking;

/// <summary>
/// Registration of the built-in chunking strategies into a
/// <see cref="ChunkingStrategyFactory"/>: <c>recursive</c> (aliases
/// <c>recursive_text</c>, <c>default</c>), <c>sentence</c> (alias <c>sentences</c>),
/// <c>structural</c> (aliases <c>markdown</c>, <c>headings</c>) and <c>semantic</c>.
/// Resolution of an unknown name still fails loudly (never a silent fallback).
/// </summary>
public static class ChunkingStrategyFactoryDefaults
{
    /// <summary>
    /// Creates a factory pre-populated with the four built-in strategies.
    /// </summary>
    /// <param name="semanticEmbedding">
    /// Optional embedding function wired into the <c>semantic</c> strategy. When
    /// <c>null</c>, <c>semantic</c> degrades to structural chunking (see
    /// <see cref="SemanticChunkingStrategy"/>).
    /// </param>
    public static ChunkingStrategyFactory CreateDefault(Func<string, float[]>? semanticEmbedding = null)
    {
        var factory = new ChunkingStrategyFactory();
        return factory.RegisterDefaultStrategies(semanticEmbedding);
    }

    /// <summary>
    /// Registers the four built-in strategies (and their aliases) into
    /// <paramref name="factory"/>. Third-party strategies can be registered alongside.
    /// </summary>
    /// <param name="factory">The factory to populate.</param>
    /// <param name="semanticEmbedding">See <see cref="CreateDefault"/>.</param>
    /// <exception cref="ArgumentException">A built-in name is already registered.</exception>
    public static ChunkingStrategyFactory RegisterDefaultStrategies(
        this ChunkingStrategyFactory factory,
        Func<string, float[]>? semanticEmbedding = null)
    {
        ArgumentNullException.ThrowIfNull(factory);

        factory.Register("recursive", static () => new RecursiveChunkingStrategy(), "recursive_text", "default");
        factory.Register("sentence", static () => new SentenceChunkingStrategy(), "sentences");
        factory.Register("structural", static () => new StructuralChunkingStrategy(), "markdown", "headings");
        factory.Register("semantic", () => new SemanticChunkingStrategy(semanticEmbedding));

        return factory;
    }
}
