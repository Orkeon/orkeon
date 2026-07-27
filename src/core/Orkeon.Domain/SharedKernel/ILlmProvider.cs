using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.SharedKernel;

/// <summary>
/// Domain interface for language model providers.
/// This is a pure domain interface without infrastructure concerns.
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    /// Gets the provider name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The provider's base configuration (model, key, base URL) when it exposes one — used by
    /// callers that need to add per-call fields (e.g. tool schemas) without discarding the
    /// configured model/credentials, since <c>ChatAsync</c>/<c>GenerateAsync</c> treat a passed
    /// config as a full replacement (<c>config ?? _config</c>). Default <see langword="null"/>.
    /// </summary>
    LlmConfig? BaseConfig => null;

    /// <summary>
    /// What this provider's API actually supports. Drives the translation of cross-cutting
    /// options (<c>response_format</c>, <c>thinking</c>, vision content) into the provider's
    /// own dialect, and lets an unsupported option be reported instead of silently dropped.
    /// </summary>
    /// <remarks>
    /// A default implementation, like <see cref="BaseConfig"/> above: third-party providers
    /// and test doubles keep compiling, and a provider that declares nothing is assumed to
    /// support nothing — nothing is written to the wire on its behalf.
    /// </remarks>
    LlmProviderCapabilities Capabilities => LlmProviderCapabilities.Unknown;

    /// <summary>
    /// Generates a response from the language model.
    /// </summary>
    Task<LlmResponse> GenerateAsync(
        string prompt,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chat interface for conversational interactions.
    /// </summary>
    Task<LlmResponse> ChatAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default);
}
