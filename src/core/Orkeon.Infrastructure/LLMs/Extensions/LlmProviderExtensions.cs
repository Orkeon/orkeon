using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Extensions;

/// <summary>
/// Non-breaking call-time overloads on <see cref="ILlmProvider"/> that take a
/// <see cref="LlmConfigOverride"/> and fuse it over a base <see cref="LlmConfig"/>
/// via <see cref="LlmConfigResolver"/> before delegating to the standard method.
/// </summary>
public static class LlmProviderExtensions
{
    /// <summary>Generates a response with a call-time <see cref="LlmConfigOverride"/> patched onto <paramref name="baseConfig"/>.</summary>
    public static Task<LlmResponse> GenerateAsync(
        this ILlmProvider provider,
        string prompt,
        LlmConfigOverride overrides,
        LlmConfig baseConfig,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(baseConfig);

        var effective = LlmConfigResolver.Resolve(baseConfig, null, overrides);
        return provider.GenerateAsync(prompt, effective, cancellationToken);
    }

    /// <summary>Chat with a call-time <see cref="LlmConfigOverride"/> patched onto <paramref name="baseConfig"/>.</summary>
    public static Task<LlmResponse> ChatAsync(
        this ILlmProvider provider,
        LlmMessage[] messages,
        LlmConfigOverride overrides,
        LlmConfig baseConfig,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(baseConfig);

        var effective = LlmConfigResolver.Resolve(baseConfig, null, overrides);
        return provider.ChatAsync(messages, effective, cancellationToken);
    }
}
