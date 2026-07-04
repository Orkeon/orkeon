using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Embeddings.Local;

/// <summary>
/// Typed request for the <see cref="LocalEmbedTool"/> (YAML name <c>local_embed_text</c>).
/// </summary>
/// <remarks>
/// <para>
/// The contract is intentionally aligned with the OpenAI / <c>Microsoft.Extensions.AI</c>
/// embeddings convention: a batch of <see cref="Inputs"/> is the canonical shape (a single
/// text is a batch of size one).
/// </para>
/// <para>
/// <see cref="RequestedDimensions"/> is a <em>hint</em>. The local provider currently wraps
/// BGE-micro-v2, which produces 384-dimensional vectors and does not support projection or
/// truncation — the field is therefore ignored in this version. The actual dimensionality
/// is reported by <c>LocalEmbedResponse.Dimensions</c>; callers can compare to detect
/// hint mismatches without raising an exception.
/// </para>
/// </remarks>
[ToolContract(
    "local_embed_text",
    Name = "local_embed_text",
    Description = "Generate embeddings for one or more texts using the on-device "
                + "BGE-micro-v2 model (no network, no API key).",
    Category = "Embeddings")]
public sealed record LocalEmbedRequest
{
    /// <summary>
    /// Gets the texts to embed. One vector is returned per entry, in the same order.
    /// </summary>
    [FieldSchema(
        Description = "Texts to embed. One vector is returned per entry, in the same order.",
        IsRequired = true)]
    public IReadOnlyList<string> Inputs { get; init; } = [];

    /// <summary>
    /// Gets the optional hint for the desired vector dimensionality.
    /// </summary>
    /// <remarks>
    /// Honoured only if the underlying model supports projection or truncation.
    /// BGE-micro-v2 does <b>not</b> — the hint is therefore ignored and the model's
    /// native dimension (384) is returned. Callers can detect a mismatch by comparing
    /// this value with <c>LocalEmbedResponse.Dimensions</c>.
    /// </remarks>
    [FieldSchema(
        Description = "Hint for the desired vector dimensionality. Honoured only if the "
                    + "underlying model supports projection / truncation. Otherwise ignored "
                    + "and the model's native dimension is returned.",
        IsRequired = false)]
    public int? RequestedDimensions { get; init; }

    /// <summary>
    /// Gets the optional model alias (e.g. <c>"bge-micro-v2"</c>).
    /// Ignored when the host has a single local model registered.
    /// </summary>
    [FieldSchema(
        Description = "Optional model alias (e.g. \"bge-micro-v2\"). Ignored when the "
                    + "host has a single local model registered.",
        IsRequired = false)]
    public string? Model { get; init; }
}
