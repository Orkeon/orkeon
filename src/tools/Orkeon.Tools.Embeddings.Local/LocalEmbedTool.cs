using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.DependencyInjection;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Embeddings.Local;

/// <summary>
/// Tool agent exposing the local on-device embedding provider through the standard
/// Orkeon <see cref="ToolBase{TRequest, TResponse}"/> pipeline.
/// </summary>
/// <remarks>
/// <para>
/// YAML name: <c>local_embed_text</c>. Wraps an <see cref="IEmbeddingProvider"/> bound to the
/// embedded BGE-micro-v2 ONNX model (384 dims, CPU, zero network). Truncation is applied
/// internally by the provider per <see cref="LocalEmbeddingOptions.MaxTextChars"/>; the tool
/// only counts the affected inputs and reports the count via
/// <see cref="LocalEmbedResponse.TruncatedCount"/>.
/// </para>
/// <para>
/// <see cref="LocalEmbedRequest.RequestedDimensions"/> is treated as a hint and ignored
/// for BGE-micro-v2 — the response always advertises the actual dimensionality (384) so
/// callers can detect a mismatch deterministically without an exception.
/// </para>
/// </remarks>
public sealed class LocalEmbedTool : ToolBase<LocalEmbedRequest, LocalEmbedResponse>
{
    /// <summary>Logical model identifier reported in <see cref="LocalEmbedResponse.Model"/>.</summary>
    private const string ModelId = "bge-micro-v2";

    private readonly IEmbeddingProvider _provider;
    private readonly LocalEmbeddingOptions _options;

    /// <inheritdoc />
    public override string Name => "local_embed_text";

    /// <inheritdoc />
    public override string Description =>
        "Generate embeddings for one or more texts using the on-device BGE-micro-v2 model.";

    /// <summary>
    /// Initializes a new instance of <see cref="LocalEmbedTool"/>.
    /// </summary>
    /// <param name="provider">The local embedding provider (typically a singleton).</param>
    /// <param name="options">
    /// Options used for diagnostics only — currently <see cref="LocalEmbeddingOptions.MaxTextChars"/>
    /// is consulted to compute <see cref="LocalEmbedResponse.TruncatedCount"/>. The provider applies
    /// truncation itself; the tool merely reports it.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public LocalEmbedTool(
        IEmbeddingProvider provider,
        LocalEmbeddingOptions options,
        ILogger<LocalEmbedTool>? logger = null)
        : base(logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    protected override Task<LocalEmbedResponse> ExecuteTypedAsync(
        LocalEmbedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Inputs is null || request.Inputs.Count == 0)
        {
            return Task.FromResult(new LocalEmbedResponse
            {
                Dimensions = _provider.Dimensions,
                Embeddings = [],
                Model = ModelId,
                TruncatedCount = 0,
            });
        }

        return ExecuteTypedCoreAsync(request, cancellationToken);
    }

    private async Task<LocalEmbedResponse> ExecuteTypedCoreAsync(
        LocalEmbedRequest request, CancellationToken cancellationToken)
    {
        var truncatedCount = CountTruncated(request.Inputs, _options.MaxTextChars);

        // The provider applies truncation internally per LE-03; the tool only counts.
        var vectors = await _provider
            .EmbedBatchAsync(request.Inputs, cancellationToken)
            .ConfigureAwait(false);

        // Defensive copy: even though the provider already returns owned arrays, materializing
        // float[] guarantees the wire format and shields callers from any future ReadOnlyMemory
        // backed by a shared buffer.
        var embeddings = new float[vectors.Count][];
        for (var i = 0; i < vectors.Count; i++)
        {
            embeddings[i] = vectors[i].ToArray();
        }

        return new LocalEmbedResponse
        {
            Dimensions = _provider.Dimensions,
            Embeddings = embeddings,
            Model = ModelId,
            TruncatedCount = truncatedCount,
        };
    }

    /// <summary>
    /// Counts the number of inputs that exceed the per-text character cap.
    /// Returns 0 when the cap is disabled (<paramref name="cap"/> null or non-positive).
    /// </summary>
    private static int CountTruncated(IReadOnlyList<string> inputs, int? cap)
    {
        if (cap is not { } c || c <= 0) return 0;

        var n = 0;
        for (var i = 0; i < inputs.Count; i++)
        {
            var text = inputs[i];
            if (text is not null && text.Length > c)
            {
                n++;
            }
        }
        return n;
    }
}
