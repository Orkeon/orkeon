using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An <see cref="ILlmRateLimiter"/> that grants every request and records, in order, the provider
/// each lease was taken for — to count the leases a run takes, and to see that none is taken twice
/// for one call. Thread-safe: parallel agents call concurrently.
/// </summary>
public sealed class CountingLlmRateLimiter : ILlmRateLimiter
{
    private readonly ConcurrentQueue<string> _providers = new();

    /// <summary>The leases taken so far.</summary>
    public int Acquired => _providers.Count;

    /// <summary>The provider of each lease, oldest first.</summary>
    public IReadOnlyList<string> Providers => [.. _providers];

    /// <summary>The leases taken for <paramref name="provider"/>.</summary>
    public int AcquiredFor(string provider) => _providers.Count(p => p == provider);

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The lease (no-op Dispose) is owned by the returned RateLimitAcquisition; whoever acquired it disposes it.")]
    public Task<RateLimitAcquisition> AcquireAsync(string provider, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _providers.Enqueue(provider);
        return Task.FromResult(RateLimitAcquisition.Acquired(new Lease()));
    }

    private sealed class Lease : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
