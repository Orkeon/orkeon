using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Telemetry.HealthChecks;

/// <summary>
/// Health check that verifies memory provider connectivity by reading a key that does not exist —
/// what all six providers serve, without walking the key space. It probed by text search, which
/// Pinecone does not have and ChromaDB does not serve over HTTP (GAP-30).
/// </summary>
public class MemoryProviderHealthCheck : IHealthCheck
{
    /// <summary>The key the probe reads, which no entry is stored under.</summary>
    private const string ProbeKey = "__health_check__";

    private readonly IMemoryProvider _memoryProvider;

    /// <summary>Initializes a new instance of <see cref="MemoryProviderHealthCheck"/>.</summary>
    /// <param name="memoryProvider">The memory provider to check.</param>
    public MemoryProviderHealthCheck(IMemoryProvider memoryProvider)
    {
        ArgumentNullException.ThrowIfNull(memoryProvider);
        _memoryProvider = memoryProvider;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Health-check barrier: any failure probing the memory provider is captured and reported as a Degraded HealthCheckResult rather than propagated to the health-check host.")]
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["provider"] = _memoryProvider.GetType().Name
        };

        try
        {
            var stopwatch = Stopwatch.StartNew();
            // A read of an absent key: one round trip, whatever the provider
            await _memoryProvider.GetAsync(ProbeKey, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            data["latency_ms"] = stopwatch.ElapsedMilliseconds;

            return HealthCheckResult.Healthy(
                $"Memory provider '{_memoryProvider.GetType().Name}' is healthy (latency: {stopwatch.ElapsedMilliseconds}ms)",
                data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded(
                $"Memory provider health check failed: {ex.Message}",
                exception: ex,
                data: data);
        }
    }
}
