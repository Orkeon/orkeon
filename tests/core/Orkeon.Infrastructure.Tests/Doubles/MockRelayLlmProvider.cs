using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An <see cref="ILlmProvider"/> that answers by calling another one and handing its answer back,
/// usage included — what a Microsoft Agent Framework agent built over Orkeon's own model does behind
/// the bridge (GAP-34). <see cref="BeforeRelay"/> and <see cref="AfterRelay"/> let a test order two
/// calls in flight at once.
/// </summary>
public sealed class MockRelayLlmProvider(string name, ILlmProvider inner) : ILlmProvider
{
    /// <inheritdoc />
    public string Name { get; } = name;

    /// <summary>What the relay declares — <c>RunsOwnTools</c> for a bridge that runs its own tools (GAP-38).</summary>
    public LlmProviderCapabilities Capabilities { get; init; } = LlmProviderCapabilities.Unknown;

    /// <summary>Awaited before the inner call; null awaits nothing.</summary>
    public Func<CancellationToken, Task>? BeforeRelay { get; set; }

    /// <summary>Awaited after the inner call answered; null awaits nothing.</summary>
    public Func<CancellationToken, Task>? AfterRelay { get; set; }

    /// <inheritdoc />
    public async Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        if (BeforeRelay is not null)
            await BeforeRelay(cancellationToken);
        var response = await inner.GenerateAsync(prompt, config, cancellationToken);
        if (AfterRelay is not null)
            await AfterRelay(cancellationToken);
        return response;
    }

    /// <inheritdoc />
    public async Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        if (BeforeRelay is not null)
            await BeforeRelay(cancellationToken);
        var response = await inner.ChatAsync(messages, config, cancellationToken);
        if (AfterRelay is not null)
            await AfterRelay(cancellationToken);
        return response;
    }
}
