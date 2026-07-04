using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Execution;
using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Application.Tests.Services;

public class CrewExecutionStateManagerTests
{
    // ══════════════ ExecutionId ══════════════

    [Fact]
    public void ShouldGenerateUniqueId_WhenCallingNew()
    {
        // Act
        var id1 = ExecutionId.New();
        var id2 = ExecutionId.New();

        // Assert
        Assert.NotEqual(id1, id2);
        Assert.NotEqual(Ulid.Empty, id1.Value);
    }

    [Fact]
    public void ShouldReturnValue_WhenCallingToString()
    {
        // Arrange
        var ulid = Ulid.NewUlid();
        var id = ExecutionId.From(ulid);

        // Assert
        Assert.Equal(ulid.ToString(), id.ToString());
    }

    [Fact]
    public void ShouldRejectNonUlidString_WhenCallingFromString()
    {
        Assert.Throws<ArgumentException>(() => ExecutionId.From("exec-42"));
    }

    // ══════════════ CrewExecutionState — Progress clamping ══════════════

    [Fact]
    public void ShouldClampProgressToZero_WhenSettingNegativeValue()
    {
        // Arrange
        var state = CreateState();

        // Act
        state.Progress = -0.5;

        // Assert
        Assert.Equal(0.0, state.Progress);
    }

    [Fact]
    public void ShouldClampProgressToOne_WhenSettingValueAboveOne()
    {
        // Arrange
        var state = CreateState();

        // Act
        state.Progress = 1.5;

        // Assert
        Assert.Equal(1.0, state.Progress);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void ShouldAcceptValidProgress_WhenInRange(double progress)
    {
        // Arrange
        var state = CreateState();

        // Act
        state.Progress = progress;

        // Assert
        Assert.Equal(progress, state.Progress);
    }

    // ══════════════ EndTime on status change ══════════════

    [Fact]
    public void ShouldSetEndTime_WhenStatusChangesToCompleted()
    {
        // Arrange
        var state = CreateState();
        Assert.Null(state.EndTime);

        // Act
        state.Status = ExecutionState.Completed;

        // Assert
        Assert.NotNull(state.EndTime);
    }

    [Fact]
    public void ShouldSetEndTime_WhenStatusChangesToFailed()
    {
        // Arrange
        var state = CreateState();

        // Act
        state.Status = ExecutionState.Failed;

        // Assert
        Assert.NotNull(state.EndTime);
    }

    [Fact]
    public void ShouldNotSetEndTime_WhenStatusChangesToRunning()
    {
        // Arrange
        var state = CreateState();

        // Act
        state.Status = ExecutionState.Running;

        // Assert
        Assert.Null(state.EndTime);
    }

    [Fact]
    public void ShouldNotSetEndTime_WhenStatusChangesToPending()
    {
        // Arrange
        var state = CreateState();

        // Act
        state.Status = ExecutionState.Pending;

        // Assert
        Assert.Null(state.EndTime);
    }

    // ══════════════ Metadata store/retrieve ══════════════

    [Fact]
    public void ShouldStoreAndRetrieveMetadata_WhenUsingSetAndGetMetadata()
    {
        // Arrange
        var state = CreateState();

        // Act
        state.SetMetadata("key1", "value1");

        // Assert
        Assert.Equal("value1", state.MetadataValue<string>("key1"));
    }

    [Fact]
    public void ShouldReturnNull_WhenMetadataKeyNotFound()
    {
        // Arrange
        var state = CreateState();

        // Act & Assert
        Assert.Null(state.MetadataValue<string>("nonexistent"));
    }

    [Fact]
    public void ShouldOverwriteMetadata_WhenSettingSameKeyTwice()
    {
        // Arrange
        var state = CreateState();
        state.SetMetadata("k", "first");

        // Act
        state.SetMetadata("k", "second");

        // Assert
        Assert.Equal("second", state.MetadataValue<string>("k"));
    }

    // ══════════════ CreateSnapshot ══════════════

    [Fact]
    public void ShouldCreateSnapshot_WhenCalled()
    {
        // Arrange
        var crewId = CrewId.Create();
        var execId = ExecutionId.New();
        var input = CrewInput.Empty("test context");
        var state = new CrewExecutionState(crewId, execId, input);
        state.Status = ExecutionState.Running;
        state.Progress = 0.75;
        state.CurrentTask = TaskId1;
        state.SetMetadata("mode", "sequential");

        // Act
        var snapshot = state.CreateSnapshot();

        // Assert
        Assert.Equal(execId, snapshot.Id);
        Assert.Equal(crewId, snapshot.CrewId);
        Assert.Equal(ExecutionState.Running, snapshot.Status);
        Assert.Equal(0.75, snapshot.Progress);
        Assert.Equal(TaskId1, snapshot.CurrentTask);
        Assert.Null(snapshot.Error);
        Assert.NotEqual(default, snapshot.StartTime);
        Assert.Null(snapshot.EndTime);
        Assert.True(snapshot.Metadata.ContainsKey("mode"));
    }

    [Fact]
    public void ShouldCaptureEndTimeInSnapshot_WhenCompleted()
    {
        // Arrange
        var state = CreateState();
        state.Status = ExecutionState.Completed;

        // Act
        var snapshot = state.CreateSnapshot();

        // Assert
        Assert.NotNull(snapshot.EndTime);
        Assert.Equal(ExecutionState.Completed, snapshot.Status);
    }

    // ══════════════ GetStatus ══════════════

    [Fact]
    public void ShouldReturnStatus_WhenCallingGetStatus()
    {
        // Arrange
        var state = CreateState();
        state.Status = ExecutionState.Running;
        state.Progress = 0.5;
        state.CurrentTask = "analyze";
        state.Error = null;

        // Act
        var status = state.ToStatus();

        // Assert
        Assert.Equal(state.Id.Value, status.Id.Value);
        Assert.Equal(ExecutionState.Running, status.State);
        Assert.Equal(0.5, status.Progress);
        Assert.Equal("analyze", status.CurrentTask);
        Assert.Null(status.Error);
    }

    [Fact]
    public void ShouldIncludeErrorInStatus_WhenErrorIsSet()
    {
        // Arrange
        var state = CreateState();
        state.Status = ExecutionState.Failed;
        state.Error = "timeout";

        // Act
        var status = state.ToStatus();

        // Assert
        Assert.Equal(ExecutionState.Failed, status.State);
        Assert.Equal("timeout", status.Error);
    }

    // ══════════════ CurrentTask / Error properties ══════════════

    [Fact]
    public void ShouldSetAndGetCurrentTask_WhenAssigned()
    {
        // Arrange
        var state = CreateState();

        // Act
        state.CurrentTask = "research";

        // Assert
        Assert.Equal("research", state.CurrentTask);
    }

    [Fact]
    public void ShouldSetAndGetError_WhenAssigned()
    {
        // Arrange
        var state = CreateState();

        // Act
        state.Error = "connection lost";

        // Assert
        Assert.Equal("connection lost", state.Error);
    }

    // ══════════════ ExecutionSnapshot record ══════════════

    [Fact]
    public void ShouldExposeBackwardCompatibleAccessors_WhenUsingSnapshot()
    {
        // Arrange
        var id = ExecutionId.New();
        var crewId = CrewId.Create();
        var snapshot = new ExecutionSnapshot(
            id, crewId,
            ExecutionState.Running, 0.6, "task-A", null,
            DateTime.UtcNow, null, ExecutionMetadata.Empty);

        // Assert
        Assert.Equal(id, snapshot.Id);
        Assert.Equal(crewId, snapshot.CrewId);
        Assert.Equal(ExecutionState.Running, snapshot.Status);
        Assert.Equal(0.6, snapshot.Progress);
        Assert.Equal("task-A", snapshot.CurrentTask);
        Assert.Null(snapshot.Error);
        Assert.Null(snapshot.EndTime);
    }

    // ── Helpers ──

    private static CrewExecutionState CreateState()
    {
        var crewId = CrewId.Create();
        var execId = ExecutionId.New();
        var input = CrewInput.Empty();
        return new CrewExecutionState(crewId, execId, input);
    }
}
