using Orkeon.Application.Interfaces;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for ICodeSandbox with call tracking and configurable results.
/// </summary>
public class MockCodeSandbox : ICodeSandbox
{
    private SandboxExecutionResult _executeResult = new()
    {
        Success = true,
        Output = "",
        ExitCode = 0,
        Duration = TimeSpan.FromMilliseconds(10)
    };

    private Func<SandboxExecutionRequest, SandboxExecutionResult>? _executeFunc;
    private bool _isAvailable = true;

    // --- Tracking ---
    public int ExecuteCallCount { get; private set; }
    public SandboxExecutionRequest? LastExecuteRequest { get; private set; }
    public List<SandboxExecutionRequest> AllExecuteRequests { get; } = [];

    public int IsAvailableCallCount { get; private set; }
    public int DisposeCallCount { get; private set; }

    // --- Configuration ---
    public SandboxCapabilities Capabilities { get; set; } = new()
    {
        SandboxType = "mock",
        SupportsMemoryLimits = true,
        SupportsCpuLimits = true,
        SupportsNetworkIsolation = true,
        SupportsFileSystemIsolation = true
    };

    public void SetExecuteResult(SandboxExecutionResult result) => _executeResult = result;

    public void SetExecuteSuccess(string output) =>
        _executeResult = new SandboxExecutionResult
        {
            Success = true,
            Output = output,
            ExitCode = 0,
            Duration = TimeSpan.FromMilliseconds(10)
        };

    public void SetExecuteError(string error) =>
        _executeResult = new SandboxExecutionResult
        {
            Success = false,
            Error = error,
            ExitCode = 1,
            Duration = TimeSpan.FromMilliseconds(10)
        };

    public void SetAvailable(bool available) => _isAvailable = available;

    public void SetExecuteFunc(Func<SandboxExecutionRequest, SandboxExecutionResult> func) =>
        _executeFunc = func;

    // --- ICodeSandbox ---
    public Task<SandboxExecutionResult> ExecuteAsync(SandboxExecutionRequest request, CancellationToken ct = default)
    {
        ExecuteCallCount++;
        LastExecuteRequest = request;
        AllExecuteRequests.Add(request);
        var result = _executeFunc != null ? _executeFunc(request) : _executeResult;
        return Task.FromResult(result);
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        IsAvailableCallCount++;
        return Task.FromResult(_isAvailable);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCallCount++;
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
