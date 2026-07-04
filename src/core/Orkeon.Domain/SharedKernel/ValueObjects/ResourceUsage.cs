using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Resource usage metrics value object.
/// </summary>
public sealed record ResourceUsage : ValueObjectRecord
{
    /// <summary>Gets the memory usage in bytes.</summary>
    public long MemoryBytes { get; }
    /// <summary>Gets the CPU time consumed.</summary>
    public TimeSpan CpuTime { get; }
    /// <summary>Gets the number of threads in use.</summary>
    public int ThreadCount { get; }
    /// <summary>Gets the timestamp when this measurement was taken.</summary>
    public DateTime MeasuredAt { get; }

    /// <summary>Initializes a new <see cref="ResourceUsage"/> snapshot.</summary>
    /// <param name="memoryBytes">The memory usage in bytes (non-negative).</param>
    /// <param name="cpuTime">The CPU time consumed (non-negative).</param>
    /// <param name="threadCount">The number of active threads (non-negative).</param>
    /// <param name="measuredAt">The measurement timestamp (defaults to <see cref="DateTime.UtcNow"/>).</param>
    private ResourceUsage(long memoryBytes, TimeSpan cpuTime, int threadCount, DateTime? measuredAt = null)
    {
        MemoryBytes = memoryBytes >= 0 ? memoryBytes : throw new ArgumentException("Memory usage cannot be negative", nameof(memoryBytes));
        CpuTime = cpuTime >= TimeSpan.Zero ? cpuTime : throw new ArgumentException("CPU time cannot be negative", nameof(cpuTime));
        ThreadCount = threadCount >= 0 ? threadCount : throw new ArgumentException("Thread count cannot be negative", nameof(threadCount));
        MeasuredAt = measuredAt ?? DateTime.UtcNow;
    }

    /// <summary>Creates a new <see cref="ResourceUsage"/> snapshot.</summary>
    /// <param name="memoryBytes">The memory usage in bytes (non-negative).</param>
    /// <param name="cpuTime">The CPU time consumed (non-negative).</param>
    /// <param name="threadCount">The number of active threads (non-negative).</param>
    /// <param name="measuredAt">The measurement timestamp (defaults to <see cref="DateTime.UtcNow"/>).</param>
    /// <returns>A new <see cref="ResourceUsage"/>.</returns>
    public static ResourceUsage Create(long memoryBytes, TimeSpan cpuTime, int threadCount, DateTime? measuredAt = null) =>
        new(memoryBytes, cpuTime, threadCount, measuredAt);

    /// <summary>Gets the memory usage in megabytes.</summary>
    public double MemoryMB => MemoryBytes / (1024.0 * 1024.0);
    /// <summary>Gets the memory usage in gigabytes.</summary>
    public double MemoryGB => MemoryMB / 1024.0;

    /// <summary>Captures the current process resource usage.</summary>
    /// <returns>A <see cref="ResourceUsage"/> snapshot of the current process.</returns>
    public static ResourceUsage Current() => Create(
        GC.GetTotalMemory(false),
        System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime,
        System.Diagnostics.Process.GetCurrentProcess().Threads.Count);

    /// <inheritdoc />
    public override string ToString() =>
        $"Memory: {MemoryMB:F1} MB, CPU: {CpuTime.TotalSeconds:F1}s, Threads: {ThreadCount}";
}
