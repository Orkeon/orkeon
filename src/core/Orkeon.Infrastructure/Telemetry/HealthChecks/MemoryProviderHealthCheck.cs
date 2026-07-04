using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Telemetry.HealthChecks;

/// <summary>
/// Health check that verifies memory provider connectivity by performing a test search.
/// </summary>
public class MemoryProviderHealthCheck : IHealthCheck
{
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
            // Perform a lightweight search to verify connectivity
            await _memoryProvider.SearchAsync("__health_check__", limit: 1, cancellationToken: cancellationToken).ConfigureAwait(false);
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
