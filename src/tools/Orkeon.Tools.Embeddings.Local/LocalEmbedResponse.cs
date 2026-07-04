using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Embeddings.Local;

/// <summary>
/// Typed response from the <see cref="LocalEmbedTool"/>.
/// </summary>
/// <remarks>
/// One <see cref="Embeddings"/> entry is produced per input, preserving input order.
/// <see cref="Dimensions"/> reports the <em>effective</em> dimensionality of every returned
/// vector — for BGE-micro-v2 this is always 384, regardless of any
/// <c>LocalEmbedRequest.RequestedDimensions</c> hint.
/// </remarks>
public sealed record LocalEmbedResponse
{
    /// <summary>
    /// Gets the effective dimensionality of every returned vector.
    /// </summary>
    [ReturnSchema(Description = "Effective dimensionality of every returned vector.")]
    public int Dimensions { get; init; }

    /// <summary>
    /// Gets the embedding vectors — one per input, preserving input order.
    /// </summary>
    [ReturnSchema(Description = "Embedding vectors — one per input, preserving input order.")]
    public IReadOnlyList<float[]> Embeddings { get; init; } = [];

    /// <summary>
    /// Gets the identifier of the model that produced the vectors.
    /// </summary>
    [ReturnSchema(Description = "Identifier of the model that produced the vectors.")]
    public string Model { get; init; } = "";

    /// <summary>
    /// Gets the number of inputs that were truncated by <c>MaxTextChars</c> before embedding
    /// (0 if none).
    /// </summary>
    [ReturnSchema(Description = "Number of inputs that were truncated by MaxTextChars before embedding (0 if none).")]
    public int TruncatedCount { get; init; }
}
