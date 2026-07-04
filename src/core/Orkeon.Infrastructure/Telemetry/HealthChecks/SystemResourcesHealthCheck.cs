using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Common;

namespace Orkeon.Infrastructure.Telemetry.HealthChecks;

/// <summary>
/// Health check that monitors system resources including memory usage, GC heap size, and thread count.
/// Returns Degraded if process memory exceeds the configured MaxMemoryMB threshold.
/// </summary>
public class SystemResourcesHealthCheck : IHealthCheck
{
    private readonly TelemetryOptions _options;

    /// <summary>Initializes a new instance of <see cref="SystemResourcesHealthCheck"/>.</summary>
    /// <param name="options">The telemetry options containing the MaxMemoryMB threshold.</param>
    public SystemResourcesHealthCheck(IOptions<TelemetryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var process = SysProcess.GetCurrentProcess();
        var workingSetMB = process.WorkingSet64 / (1024.0 * 1024.0);
        var gcInfo = GC.GetGCMemoryInfo();
        var heapSizeMB = gcInfo.HeapSizeBytes / (1024.0 * 1024.0);
        var threadCount = process.Threads.Count;

        var data = new Dictionary<string, object>
        {
            ["working_set_mb"] = Math.Round(workingSetMB, 2),
            ["heap_size_mb"] = Math.Round(heapSizeMB, 2),
            ["thread_count"] = threadCount,
            ["gc_gen0_collections"] = GC.CollectionCount(0),
            ["gc_gen1_collections"] = GC.CollectionCount(1),
            ["gc_gen2_collections"] = GC.CollectionCount(2),
            ["max_memory_mb"] = _options.MaxMemoryMB
        };

        if (workingSetMB > _options.MaxMemoryMB)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                Inv.Format($"Process memory ({workingSetMB:F0} MB) exceeds threshold ({_options.MaxMemoryMB} MB)"),
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            Inv.Format($"System resources healthy. Memory: {workingSetMB:F0} MB, Threads: {threadCount}"),
            data: data));
    }
}
