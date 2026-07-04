using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Orchestration;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>
/// Round-trip and namespacing tests for <see cref="CrewExecutionStateMapper"/> (R3.8):
/// CrewExecutionState ↔ SessionState so execution states can ride any IStateStore.
/// </summary>
public class CrewExecutionStateMapperTests
{
    private static CrewExecutionState CreateState(
        out CrewId crewId,
        out ExecutionId executionId)
    {
        crewId = CrewId.Create();
        executionId = ExecutionId.New();
        var input = new CrewInput(
            "ctx",
            new Dictionary<string, object> { ["lang"] = "fr", ["retries"] = "3" });
        return new CrewExecutionState(crewId, executionId, input);
    }

    [Fact]
    public void ShouldRoundTripCoreFields_WhenMappingToAndFromSessionState()
    {
        // Arrange
        var state = CreateState(out var crewId, out var executionId);
        state.Status = ExecutionState.Running;
        state.Progress = 0.42;
        state.CurrentTask = "step-3";
        state.SetMetadata("totalTokens", 1234);

        // Act
        var session = CrewExecutionStateMapper.ToSessionState(state);
        var restored = CrewExecutionStateMapper.FromSessionState(session);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(executionId, restored.Id);
        Assert.Equal(crewId, restored.CrewId);
        Assert.Equal(ExecutionState.Running, restored.Status);
        Assert.Equal(0.42, restored.Progress);
        Assert.Equal("step-3", restored.CurrentTask);
        Assert.Equal("ctx", restored.Input.InitialContext);
        Assert.Equal("fr", restored.Input.GetStringVariables()["lang"]);
        // Metadata round-trips as invariant strings; primitive reads still convert.
        Assert.Equal("1234", restored.Metadata.Get<string>("totalTokens"));
    }

    [Fact]
    public void ShouldRoundTripMeasuredTokenUsage_WhenOutputCarriesTelemetry()
    {
        // Arrange
        var state = CreateState(out _, out _);
        state.Output = new CrewOutput(
            "answer", [], TimeSpan.FromSeconds(2), new TokenUsage(10, 20, 30));

        // Act
        var restored = CrewExecutionStateMapper.FromSessionState(
            CrewExecutionStateMapper.ToSessionState(state));

        // Assert
        Assert.NotNull(restored);
        Assert.NotNull(restored.Output);
        Assert.NotNull(restored.Output.TokensUsed);
        Assert.Equal(10, restored.Output.TokensUsed.PromptTokens);
        Assert.Equal(20, restored.Output.TokensUsed.CompletionTokens);
        Assert.Equal(30, restored.Output.TokensUsed.TotalTokens);
    }

    [Fact]
    public void ShouldRoundTripNullTokenUsage_WhenTelemetryWasNotMeasured()
    {
        // Arrange — "not measured" (R10.8) must survive persistence instead of being
        // flattened into a fabricated TokenUsage(0,0,0).
        var state = CreateState(out _, out _);
        state.Output = new CrewOutput(
            "answer", [], TimeSpan.FromSeconds(2), TokensUsed: null);

        // Act
        var restored = CrewExecutionStateMapper.FromSessionState(
            CrewExecutionStateMapper.ToSessionState(state));

        // Assert
        Assert.NotNull(restored);
        Assert.NotNull(restored.Output);
        Assert.Null(restored.Output.TokensUsed);
    }

    [Fact]
    public void ShouldRoundTripErrorAndEndTime_WhenStateIsTerminal()
    {
        // Arrange
        var state = CreateState(out _, out _);
        state.Error = "exploded";
        state.Status = ExecutionState.Failed;

        // Act
        var restored = CrewExecutionStateMapper.FromSessionState(
            CrewExecutionStateMapper.ToSessionState(state));

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(ExecutionState.Failed, restored.Status);
        Assert.Equal("exploded", restored.Error);
        Assert.NotNull(restored.EndTime);
        Assert.Equal(state.StartTime, restored.StartTime, precision: TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void ShouldNamespaceSessionAndCrewIds_WhenMappingToSessionState()
    {
        // Arrange
        var state = CreateState(out var crewId, out var executionId);

        // Act
        var session = CrewExecutionStateMapper.ToSessionState(state);

        // Assert — both ids carry the namespace so a shared store never confuses
        // execution states with CheckpointManager sessions (GetLatestForCrewAsync).
        Assert.StartsWith(CrewExecutionStateMapper.SessionNamespace, session.SessionId, StringComparison.Ordinal);
        Assert.StartsWith(CrewExecutionStateMapper.SessionNamespace, session.CrewId, StringComparison.Ordinal);
        Assert.Equal(CrewExecutionStateMapper.SessionIdFor(executionId), session.SessionId);
        Assert.Equal(CrewExecutionStateMapper.CrewScopeFor(crewId), session.CrewId);
    }

    [Fact]
    public void ShouldKeepDocumentCheckpointPending_SoItNeverCountsAsCompletedTask()
    {
        // Arrange
        var state = CreateState(out _, out _);

        // Act
        var session = CrewExecutionStateMapper.ToSessionState(state);

        // Assert
        var checkpoint = Assert.Single(session.TaskCheckpoints);
        Assert.Equal(CrewExecutionStateMapper.StateDocumentKey, checkpoint.Key);
        Assert.Equal(CheckpointStatus.Pending, checkpoint.Value.Status);
        Assert.False(string.IsNullOrWhiteSpace(checkpoint.Value.Output));
    }

    [Fact]
    public void ShouldReturnNull_WhenSessionIsNotAnExecutionStateSession()
    {
        // Arrange — a plain CheckpointManager-style session (no namespace, no document)
        var foreignSession = new SessionState
        {
            SessionId = Guid.NewGuid().ToString(),
            CrewId = CrewId.Create().AsString(),
            Phase = SessionPhase.Running
        };

        // Act & Assert
        Assert.Null(CrewExecutionStateMapper.FromSessionState(foreignSession));
    }

    [Fact]
    public void ShouldReturnNull_WhenDocumentPayloadIsCorrupt()
    {
        // Arrange — namespaced session whose document is not valid JSON
        var session = new SessionState
        {
            SessionId = CrewExecutionStateMapper.SessionNamespace + ExecutionId.New().AsString(),
            CrewId = CrewExecutionStateMapper.SessionNamespace + CrewId.Create().AsString(),
            Phase = SessionPhase.Running,
            TaskCheckpoints = new Dictionary<string, TaskCheckpoint>
            {
                [CrewExecutionStateMapper.StateDocumentKey] = new TaskCheckpoint
                {
                    TaskId = CrewExecutionStateMapper.StateDocumentKey,
                    Status = CheckpointStatus.Pending,
                    Output = "{ not-json"
                }
            }
        };

        // Act & Assert
        Assert.Null(CrewExecutionStateMapper.FromSessionState(session));
    }

    [Theory]
    [InlineData(ExecutionState.Pending, SessionPhase.Pending)]
    [InlineData(ExecutionState.Running, SessionPhase.Running)]
    [InlineData(ExecutionState.Completed, SessionPhase.Completed)]
    [InlineData(ExecutionState.Failed, SessionPhase.Failed)]
    [InlineData(ExecutionState.Cancelled, SessionPhase.Failed)] // terminal — never resumable
    public void ShouldMapStatusToTerminalAwarePhase(ExecutionState status, SessionPhase expected)
    {
        Assert.Equal(expected, CrewExecutionStateMapper.ToPhase(status));
    }

    [Fact]
    public void ShouldPreserveCancelledStatusInDocument_EvenThoughPhaseIsTerminal()
    {
        // Arrange
        var state = CreateState(out _, out _);
        state.Status = ExecutionState.Cancelled;

        // Act
        var session = CrewExecutionStateMapper.ToSessionState(state);
        var restored = CrewExecutionStateMapper.FromSessionState(session);

        // Assert — phase degrades to Failed (no Cancelled phase) but the exact
        // status survives via the document.
        Assert.Equal(SessionPhase.Failed, session.Phase);
        Assert.NotNull(restored);
        Assert.Equal(ExecutionState.Cancelled, restored.Status);
    }
}
