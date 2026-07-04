using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Default fallback <see cref="ILlmProvider"/> used when no real provider is configured.
/// Returns the prompt as-is (echo) so scripts can run end-to-end without requiring an
/// API key, useful for local development and tests. Embeddings are not supported.
/// </summary>
public sealed class UndefinedLlmProvider : ILlmProvider
{
    /// <inheritdoc />
    public string Name => "undefined";

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(
        string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new LlmResponse { Content = prompt, TokensUsed = 0 });

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(
        LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        var lastUser = messages.LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(new LlmResponse { Content = lastUser?.Content ?? string.Empty, TokensUsed = 0 });
    }
}
