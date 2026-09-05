using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.LLMs.Embeddings;

/// <summary>
/// Fail-fast implementation of the Application port <see cref="IEmbeddingProvider"/>,
/// registered as the last resort of the default resolution chain
/// (see <see cref="DefaultEmbeddingProviderResolver"/>) when no semantic provider
/// is available.
/// </summary>
/// <remarks>
/// <para>
/// RAG-01/C4 ("death of the silent hash stub"): instead of silently degrading to
/// hash-based pseudo-embeddings, every embedding request throws an
/// <see cref="InvalidOperationException"/> with an actionable message. The throw
/// happens at the <b>first use</b> (an embed call), never at container build time —
/// hosts that wire the port but never embed anything are unaffected.
/// </para>
/// <para>
/// <see cref="Name"/>, <see cref="Model"/> and <see cref="Dimensions"/> stay
/// non-throwing so diagnostic/logging paths can inspect the provider safely.
/// </para>
/// </remarks>
public sealed class UnconfiguredEmbeddingProvider : IEmbeddingProvider
{
    /// <summary>
    /// Actionable default error message (RAG-01 fiche wording, kept verbatim so hosts
    /// and tests can rely on it).
    /// </summary>
    public const string DefaultMessage =
        "no semantic embedding provider is configured; add AddOrkeonLocalEmbeddings() " +
        "or configure Orkeon:Embeddings";

    private readonly string _message;

    /// <summary>
    /// Creates a fail-fast provider.
    /// </summary>
    /// <param name="message">
    /// Optional custom error message thrown at first use; defaults to
    /// <see cref="DefaultMessage"/>.
    /// </param>
    public UnconfiguredEmbeddingProvider(string? message = null)
    {
        _message = string.IsNullOrWhiteSpace(message) ? DefaultMessage : message;
    }

    /// <inheritdoc />
    public string Name => "unconfigured";

    /// <inheritdoc />
    public string Model => "none";

    /// <inheritdoc />
    public int Dimensions => 0;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Always — no semantic provider is configured.</exception>
    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(_message);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Always — no semantic provider is configured.</exception>
    public Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(_message);
}
