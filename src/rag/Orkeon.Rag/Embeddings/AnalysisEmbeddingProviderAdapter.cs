using Orkeon.Application.Interfaces.Ports;
using AnalysisEmbeddingProvider = Orkeon.Analysis.Abstractions.Interfaces.IEmbeddingProvider;

namespace Orkeon.Rag.Embeddings;

/// <summary>
/// Bridges the Analysis embedding abstraction
/// (<see cref="Orkeon.Analysis.Abstractions.Interfaces.IEmbeddingProvider"/>, batch-only
/// <c>EmbedBatchAsync</c>) to the Application port
/// (<see cref="Orkeon.Application.Interfaces.Ports.IEmbeddingProvider"/>,
/// <c>GetEmbeddingAsync</c>/<c>GetEmbeddingsAsync</c> + <c>Name</c>/<c>Model</c>/<c>Dimensions</c>).
/// </summary>
/// <remarks>
/// <para>
/// This makes any Analysis-side provider — notably the on-device
/// <c>LocalEmbeddingProvider</c> (BGE-micro-v2 ONNX, 384 dims, zero API key) — consumable
/// everywhere the Application port is expected (RAG, knowledge, agent selection).
/// Introduced in RAG-01/C3 with a provisional home in <c>Orkeon.Infrastructure</c>;
/// moved to <c>Orkeon.Rag</c> in RAG-02/C3 per the plan (§4.1 — it belongs in Orkeon.Rag,
/// the one project that references both worlds) and the ADR-006 amendment.
/// </para>
/// <para>
/// Both the unary and the batch port methods route through the inner provider's
/// <c>EmbedBatchAsync</c>. The Analysis abstraction only exposes <c>Dimensions</c>, so
/// <c>Name</c> and <c>Model</c> are constructor-injectable with sensible defaults
/// (inner type name / <see cref="DefaultModel"/>).
/// </para>
/// </remarks>
public sealed class AnalysisEmbeddingProviderAdapter : IEmbeddingProvider
{
    /// <summary>Default <see cref="Model"/> when none is supplied — the Analysis abstraction does not expose one.</summary>
    public const string DefaultModel = "analysis-embedding";

    private readonly AnalysisEmbeddingProvider _inner;

    /// <summary>
    /// Creates an adapter around an Analysis embedding provider.
    /// </summary>
    /// <param name="inner">The Analysis-side provider to adapt (e.g. <c>LocalEmbeddingProvider</c>).</param>
    /// <param name="name">
    /// Optional provider name exposed on the Application port.
    /// Defaults to the inner provider's type name (e.g. <c>"LocalEmbeddingProvider"</c>).
    /// </param>
    /// <param name="model">
    /// Optional model identifier exposed on the Application port.
    /// Defaults to <see cref="DefaultModel"/>; pass e.g. <c>"bge-micro-v2"</c> when known.
    /// </param>
    public AnalysisEmbeddingProviderAdapter(
        AnalysisEmbeddingProvider inner,
        string? name = null,
        string? model = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        Name = string.IsNullOrWhiteSpace(name) ? inner.GetType().Name : name;
        Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Model { get; }

    /// <inheritdoc />
    public int Dimensions => _inner.Dimensions;

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<float[]> GetEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var vectors = await _inner.EmbedBatchAsync([text], cancellationToken).ConfigureAwait(false);
        return vectors.Count > 0 ? vectors[0].ToArray() : [];
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<IList<float[]>> GetEmbeddingsAsync(
        IList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
        {
            return [];
        }

        IReadOnlyList<string> batch = texts as IReadOnlyList<string> ?? [.. texts];
        var vectors = await _inner.EmbedBatchAsync(batch, cancellationToken).ConfigureAwait(false);

        var result = new List<float[]>(vectors.Count);
        foreach (var vector in vectors)
        {
            result.Add(vector.ToArray());
        }

        return result;
    }
}
