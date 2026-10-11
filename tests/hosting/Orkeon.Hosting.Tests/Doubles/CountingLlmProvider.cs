using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>Hand-written <see cref="ILlmProvider"/> that answers every call and counts them.</summary>
public sealed class CountingLlmProvider : ILlmProvider
{
    private int _calls;

    /// <inheritdoc />
    public string Name { get; init; } = "counting";

    /// <summary>How many Generate and Chat calls reached the provider.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(new LlmResponse { Content = "Final answer." });
    }

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(new LlmResponse { Content = "Final answer." });
    }
}
