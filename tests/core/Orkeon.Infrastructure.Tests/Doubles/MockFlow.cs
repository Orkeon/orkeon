using Orkeon.Domain.Common;
using Orkeon.Domain.Flows;
using Orkeon.Domain.Flows.ValueObjects;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IFlow with call tracking and configurable results.
/// </summary>
public class MockFlow : IFlow
{
    private FlowResult _executeResult = FlowResult.CreateSuccess();

    // --- Tracking ---
    public int ExecuteCallCount { get; private set; }

    // --- Configuration ---
    public FlowId Id { get; set; } = FlowId.Create();
    public string Name { get; set; } = "Mock Flow";
    public FlowState State { get; set; } = FlowState.Empty;

    public void SetExecuteResult(FlowResult result) => _executeResult = result;

    public void SetExecuteSuccess(object? output = null) =>
        _executeResult = FlowResult.CreateSuccess(output);

    public void SetExecuteFailure(string error) =>
        _executeResult = FlowResult.CreateFailure(error);

    // --- IFlow ---
    public Task<FlowResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        ExecuteCallCount++;
        return Task.FromResult(_executeResult);
    }
}
