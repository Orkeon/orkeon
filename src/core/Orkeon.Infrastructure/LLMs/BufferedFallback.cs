using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>What a buffered fallback of a text stream does with an answer that carries no content.</summary>
internal static class BufferedFallback
{
    /// <summary>Metadata key every provider writes its refusal under (see <c>LlmResponseMetadata</c>).</summary>
    private const string ProviderErrorMetadataKey = "error";

    /// <summary>
    /// The exception a buffered-fallback stream must fail with when the buffered answer is a
    /// refusal rather than an answer. Returns <see langword="null"/> when the answer is merely
    /// empty: a model with nothing to say still ends its stream normally.
    /// </summary>
    /// <param name="response">The buffered answer the fallback was going to yield.</param>
    internal static HttpRequestException? Refusal(LlmResponse response)
        => response.Metadata.TryGetValue(ProviderErrorMetadataKey, out var value)
           && value?.ToString() is { Length: > 0 } error
            ? new HttpRequestException(
                $"{error}: the buffered fallback answered with no content, so the stream carries nothing.")
            : null;
}
