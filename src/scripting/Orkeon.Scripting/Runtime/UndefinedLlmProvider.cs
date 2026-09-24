using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Default fallback <see cref="ILlmProvider"/> used when no real provider is configured.
/// Returns the prompt as-is (echo) so scripts can run end-to-end without requiring an
/// API key, useful for local development and tests. Embeddings are not supported.
/// </summary>
/// <remarks>
/// It calls no model, so it spends nothing — and says so: every answer counts zero tokens
/// on both sides. Leaving the counts out would read as "the provider reported nothing", which
/// the token meter estimates rather than takes for zero.
/// </remarks>
public sealed class UndefinedLlmProvider : ILlmProvider
{
    /// <inheritdoc />
    public string Name => "undefined";

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(
        string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        => Task.FromResult(Echo(prompt));

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(
        LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        var lastUser = messages.LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(Echo(lastUser?.Content ?? string.Empty));
    }

    private static LlmResponse Echo(string content) =>
        new() { Content = content, TokensUsed = 0, PromptTokens = 0, CompletionTokens = 0 };
}
