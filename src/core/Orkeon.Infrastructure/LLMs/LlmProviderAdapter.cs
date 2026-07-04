using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// Adapter that converts ILlmProvider to IBasicLlmProvider.
/// Exposes the underlying provider for consumers that need the full ILlmProvider interface.
/// </summary>
public sealed class LlmProviderAdapter : IBasicLlmProvider
{
    private readonly ILlmProvider _llmProvider;

    /// <summary>Initializes a new instance of <see cref="LlmProviderAdapter"/>.</summary>
    /// <param name="llmProvider">The underlying LLM provider.</param>
    public LlmProviderAdapter(ILlmProvider llmProvider)
    {
        ArgumentNullException.ThrowIfNull(llmProvider);
        _llmProvider = llmProvider;
    }

    /// <summary>Gets the underlying <see cref="ILlmProvider"/> instance.</summary>
    public ILlmProvider UnderlyingProvider => _llmProvider;

    /// <inheritdoc />
    public string Name => _llmProvider.Name;

    /// <inheritdoc />
    public async Task<string> ChatAsync(
        string message,
        LlmConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new[] { LlmMessage.User(message) };
        var response = await _llmProvider.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        return response.Content;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Availability probe: any failure pinging the provider is reported as 'not available' (false) rather than propagated.")]
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try a simple ping with empty message
            var response = await _llmProvider.GenerateAsync("ping", null, cancellationToken).ConfigureAwait(false);
            return !string.IsNullOrEmpty(response.Content);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
