using Microsoft.Extensions.AI;

namespace Orkeon.Tools.Embeddings.Local;

/// <summary>
/// Optional bridge exposing a <see cref="LocalEmbeddingProvider"/> as the standard
/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> contract from
/// <c>Microsoft.Extensions.AI</c>.
/// </summary>
/// <remarks>
/// <para>
/// This bridge is provided as a convenience for consumers of the Microsoft.Extensions.AI
/// ecosystem (generic vector memory, M.E.AI samples, third-party integrations) so they
/// can plug the on-device BGE-micro-v2 embedder in without taking a hard dependency on
/// Orkeon's <c>IEmbeddingProvider</c> abstraction.
/// </para>
/// <para>
/// Ownership of the wrapped <see cref="LocalEmbeddingProvider"/> remains with whoever
/// constructed it (typically the DI container). For that reason <see cref="Dispose"/>
/// is intentionally a no-op — disposing the bridge must NEVER tear down the underlying
/// ONNX session held by the provider, since other consumers may still hold the same
/// <see cref="LocalEmbeddingProvider"/> reference. Always dispose the provider directly.
/// </para>
/// <para>
/// The bridge is purely additive and is not required by RaggableTree; the canonical
/// flow goes through <c>IEmbeddingProvider.EmbedBatchAsync</c>.
/// </para>
/// </remarks>
public sealed class LocalEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly LocalEmbeddingProvider _inner;

    /// <summary>
    /// Creates a new bridge wrapping the supplied Orkeon provider. The same ONNX session
    /// is reused — no double instantiation.
    /// </summary>
    /// <param name="inner">The Orkeon provider. Required.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="inner"/> is <see langword="null"/>.</exception>
    public LocalEmbeddingGenerator(LocalEmbeddingProvider inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>
    /// Provider metadata exposed both directly and via
    /// <see cref="GetService(System.Type, object?)"/> per the Microsoft.Extensions.AI
    /// convention (the M.E.AI <see cref="IEmbeddingGenerator"/> interface no longer carries
    /// a <c>Metadata</c> property — consumers query metadata through <c>GetService</c>).
    /// </summary>
    public EmbeddingGeneratorMetadata Metadata { get; } =
        new EmbeddingGeneratorMetadata(
            providerName: "local-bge-micro-v2",
            providerUri: null,
            defaultModelId: "bge-micro-v2",
            defaultModelDimensions: null);

    /// <inheritdoc />
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        var list = values as IReadOnlyList<string> ?? values.ToList();
        return GenerateCoreAsync(list, cancellationToken);
    }

    private async Task<GeneratedEmbeddings<Embedding<float>>> GenerateCoreAsync(
        IReadOnlyList<string> list,
        CancellationToken cancellationToken)
    {
        var vectors = await _inner.EmbedBatchAsync(list, cancellationToken).ConfigureAwait(false);

        var embeddings = new List<Embedding<float>>(vectors.Count);
        for (var i = 0; i < vectors.Count; i++)
        {
            embeddings.Add(new Embedding<float>(vectors[i]));
        }

        return new GeneratedEmbeddings<Embedding<float>>(embeddings);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is not null) return null;

        if (serviceType == typeof(EmbeddingGeneratorMetadata))
            return Metadata;

        if (serviceType.IsInstanceOfType(this))
            return this;

        return null;
    }

    /// <summary>
    /// Intentionally empty — ownership of the wrapped <see cref="LocalEmbeddingProvider"/>
    /// belongs to its constructor (typically the DI container). Disposing the bridge MUST
    /// NOT tear down the inner ONNX session.
    /// </summary>
    public void Dispose()
    {
        // No-op by design. See class remarks.
    }
}
