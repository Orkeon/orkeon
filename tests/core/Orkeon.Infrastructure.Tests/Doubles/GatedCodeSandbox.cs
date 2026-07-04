using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual double for ICodeSandbox whose availability probe blocks until explicitly
/// released via <see cref="ReleaseProbe"/>. Counters use Interlocked so concurrent
/// probe/memoization tests (LazyProbingCodeSandbox, R10.3) stay deterministic.
/// </summary>
public sealed class GatedCodeSandbox : ICodeSandbox
{
    private readonly TaskCompletionSource<bool> _probeGate =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _isAvailableCallCount;
    private int _executeCallCount;

    // --- Tracking ---
    public int IsAvailableCallCount => Volatile.Read(ref _isAvailableCallCount);
    public int ExecuteCallCount => Volatile.Read(ref _executeCallCount);

    // --- Configuration ---
    public SandboxCapabilities Capabilities { get; set; } = new()
    {
        SandboxType = "gated",
        SupportsMemoryLimits = true,
        SupportsCpuLimits = true,
        SupportsNetworkIsolation = true,
        SupportsFileSystemIsolation = true
    };

    /// <summary>Unblocks every pending (and future) availability probe with the given outcome.</summary>
    public void ReleaseProbe(bool available) => _probeGate.TrySetResult(available);

    // --- ICodeSandbox ---
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _isAvailableCallCount);
        return await _probeGate.Task;
    }

    public Task<SandboxExecutionResult> ExecuteAsync(SandboxExecutionRequest request, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _executeCallCount);
        return Task.FromResult(new SandboxExecutionResult
        {
            Success = true,
            Output = "gated",
            ExitCode = 0,
            Duration = TimeSpan.FromMilliseconds(1)
        });
    }

    public ValueTask DisposeAsync()
    {
        // Unblock any leftover waiter so a test teardown can never hang on the gate.
        _probeGate.TrySetResult(false);
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
