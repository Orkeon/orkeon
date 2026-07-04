using Orkeon.Infrastructure.Flows.Visualization;

namespace Orkeon.Infrastructure.Tests.Flows.Visualization;

public class FlowExecutionTrackerTestsFixture
{
    private readonly FlowExecutionTracker _tracker = new();

    public FlowExecutionState StartTracking(string flowId)
        => _tracker.StartTracking(flowId);

    public void UpdateStepState(string flowId, string stepId, StepState state)
        => _tracker.UpdateStepState(flowId, stepId, state);

    public void CompleteTracking(string flowId)
        => _tracker.CompleteTracking(flowId);

    public FlowExecutionState? GetExecutionState(string flowId)
        => _tracker.GetExecutionState(flowId);

    public FlowExecutionTracker GetTracker() => _tracker;
}
