using Orkeon.Infrastructure.Flows.Visualization;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Infrastructure.Tests.Flows.Visualization;

public class FlowExecutionTrackerTests
{
    private readonly FlowExecutionTrackerTestsFixture _fixture = new();

    [Fact]
    public void StartTracking_CreatesStateWithTimestamp()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var state = _fixture.StartTracking(FlowId1);

        // Assert
        Assert.Equal(FlowId1, state.FlowId);
        Assert.NotNull(state.StepStates);
        Assert.Empty(state.StepStates);
        Assert.True(state.StartedAt >= before);
        Assert.True(state.StartedAt <= DateTime.UtcNow);
        Assert.Null(state.CompletedAt);
    }

    [Fact]
    public void UpdateStepState_ChangesStepState()
    {
        // Arrange
        _fixture.StartTracking(FlowId1);

        // Act
        _fixture.UpdateStepState(FlowId1, StepId1, StepState.Running);

        // Assert
        var state = _fixture.GetExecutionState(FlowId1);
        Assert.NotNull(state);
        Assert.True(state.StepStates.ContainsKey(StepId1));
        Assert.Equal(StepState.Running, state.StepStates[StepId1]);
    }

    [Fact]
    public void UpdateStepState_ThrowsForUntrackedFlow()
    {
        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _fixture.UpdateStepState("unknown-flow", StepId1, StepState.Running));

        Assert.Contains("unknown-flow", ex.Message);
        Assert.Contains("not being tracked", ex.Message);
    }

    [Fact]
    public void CompleteTracking_SetsCompletedAt()
    {
        // Arrange
        _fixture.StartTracking(FlowId1);
        var before = DateTime.UtcNow;

        // Act
        _fixture.CompleteTracking(FlowId1);

        // Assert
        var state = _fixture.GetExecutionState(FlowId1);
        Assert.NotNull(state);
        Assert.NotNull(state.CompletedAt);
        Assert.True(state.CompletedAt >= before);
        Assert.True(state.CompletedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void GetExecutionState_ReturnsNullForUnknownFlow()
    {
        // Act
        var state = _fixture.GetExecutionState("nonexistent");

        // Assert
        Assert.Null(state);
    }

    [Fact]
    public void MultipleFlows_TrackedIndependently()
    {
        // Arrange
        _fixture.StartTracking("flow-a");
        _fixture.StartTracking("flow-b");

        // Act
        _fixture.UpdateStepState("flow-a", StepId1, StepState.Completed);
        _fixture.UpdateStepState("flow-b", StepId1, StepState.Failed);

        // Assert
        var stateA = _fixture.GetExecutionState("flow-a");
        var stateB = _fixture.GetExecutionState("flow-b");

        Assert.NotNull(stateA);
        Assert.NotNull(stateB);
        Assert.Equal(StepState.Completed, stateA.StepStates[StepId1]);
        Assert.Equal(StepState.Failed, stateB.StepStates[StepId1]);
    }

    [Fact]
    public void UpdateStepState_CanTransitionThroughStates()
    {
        // Arrange
        _fixture.StartTracking(FlowId1);

        // Act
        _fixture.UpdateStepState(FlowId1, StepId1, StepState.Pending);
        _fixture.UpdateStepState(FlowId1, StepId1, StepState.Running);
        _fixture.UpdateStepState(FlowId1, StepId1, StepState.Completed);

        // Assert
        var state = _fixture.GetExecutionState(FlowId1);
        Assert.NotNull(state);
        Assert.Equal(StepState.Completed, state.StepStates[StepId1]);
    }

    [Fact]
    public void StartTracking_ThrowsOnNullOrWhitespace()
    {
        // null throws ArgumentNullException (a subclass of ArgumentException)
        Assert.ThrowsAny<ArgumentException>(() => _fixture.StartTracking(null!));
        Assert.ThrowsAny<ArgumentException>(() => _fixture.StartTracking(""));
        Assert.ThrowsAny<ArgumentException>(() => _fixture.StartTracking("   "));
    }

    [Fact]
    public void UpdateStepState_ThrowsOnNullOrWhitespaceArguments()
    {
        _fixture.StartTracking(FlowId1);

        // null throws ArgumentNullException (a subclass of ArgumentException)
        Assert.ThrowsAny<ArgumentException>(() => _fixture.UpdateStepState(null!, "step", StepState.Running));
        Assert.ThrowsAny<ArgumentException>(() => _fixture.UpdateStepState(FlowId1, null!, StepState.Running));
        Assert.ThrowsAny<ArgumentException>(() => _fixture.UpdateStepState(FlowId1, "", StepState.Running));
    }
}
