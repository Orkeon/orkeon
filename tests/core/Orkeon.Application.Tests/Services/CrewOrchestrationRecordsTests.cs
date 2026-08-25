using Orkeon.Application.Interfaces.Services;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Services;

public class CrewOrchestrationRecordsTests
{
    // ══════════════ CrewInput factories ══════════════

    [Fact]
    public void ShouldCreateEmpty_WhenCallingEmptyFactory()
    {
        // Act
        var input = CrewInput.Empty();

        // Assert
        Assert.Null(input.InitialContext);
        Assert.Empty(input.Variables);
    }

    [Fact]
    public void ShouldCreateEmptyWithContext_WhenCallingEmptyFactoryWithContext()
    {
        // Act
        var input = CrewInput.Empty("some context");

        // Assert
        Assert.Equal("some context", input.InitialContext);
        Assert.Empty(input.Variables);
    }

    [Fact]
    public void ShouldCreateWithStringVariables_WhenCallingWithStringVariablesFactory()
    {
        // Arrange
        var vars = new Dictionary<string, string>
        {
            ["topic"] = "AI safety",
            ["depth"] = "deep"
        };

        // Act
        var input = CrewInput.WithStringVariables("research context", vars);

        // Assert
        Assert.Equal("research context", input.InitialContext);
        Assert.Equal(2, input.Variables.Count);
        Assert.Equal("AI safety", input.Variables["topic"]?.ToString());
        Assert.Equal("deep", input.Variables["depth"]?.ToString());
    }

    [Fact]
    public void ShouldCreateWithNullContext_WhenCallingWithStringVariablesFactoryWithNull()
    {
        // Act
        var input = CrewInput.WithStringVariables(null, new Dictionary<string, string>());

        // Assert
        Assert.Null(input.InitialContext);
    }

    // ══════════════ CrewInput.GetStringVariables ══════════════

    [Fact]
    public void ShouldConvertAllVariablesToStrings_WhenCallingGetStringVariables()
    {
        // Arrange
        var vars = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["count"] = 42,
            ["flag"] = true
        };
        var input = new CrewInput(null, vars);

        // Act
        var stringVars = input.GetStringVariables();

        // Assert
        Assert.Equal("Alice", stringVars["name"]);
        Assert.Equal("42", stringVars["count"]);
        Assert.Equal("True", stringVars["flag"]);
    }

    [Fact]
    public void ShouldReturnEmptyString_WhenVariableValueIsNull()
    {
        // Arrange
        var vars = new Dictionary<string, object>
        {
            ["nullKey"] = null!
        };
        var input = new CrewInput(null, vars);

        // Act
        var stringVars = input.GetStringVariables();

        // Assert
        Assert.Equal(string.Empty, stringVars["nullKey"]);
    }

    [Fact]
    public void ShouldReturnEmptyDictionary_WhenNoVariablesExist()
    {
        // Arrange
        var input = CrewInput.Empty();

        // Act
        var stringVars = input.GetStringVariables();

        // Assert
        Assert.Empty(stringVars);
    }

    // ══════════════ TokenUsage (from ICrewOrchestrationService) ══════════════

    [Fact]
    public void ShouldStoreTokenCounts_WhenCreatingTokenUsage()
    {
        // Act
        var usage = new TokenUsage(100, 50, 150);

        // Assert
        Assert.Equal(100, usage.PromptTokens);
        Assert.Equal(50, usage.CompletionTokens);
        Assert.Equal(150, usage.TotalTokens);
    }

    [Fact]
    public void ShouldSupportRecordEquality_WhenComparingTokenUsage()
    {
        // Arrange
        var u1 = new TokenUsage(10, 20, 30);
        var u2 = new TokenUsage(10, 20, 30);

        // Assert
        Assert.Equal(u1, u2);
    }

    // ══════════════ BatchOutput counts ══════════════

    [Fact]
    public void ShouldTrackCounts_WhenCreatingBatchOutput()
    {
        // Arrange
        var outputs = new List<CrewOutput>
        {
            new("result1", Array.Empty<Orkeon.Application.Execution.TaskOutput>(),
                TimeSpan.FromSeconds(10), new TokenUsage(10, 5, 15)),
            new("result2", Array.Empty<Orkeon.Application.Execution.TaskOutput>(),
                TimeSpan.FromSeconds(20), new TokenUsage(20, 10, 30))
        };

        // Act
        var batch = new BatchOutput(outputs, SuccessCount: 2, FailureCount: 0, TotalDuration: TimeoutQuick);

        // Assert
        Assert.Equal(2, batch.Results.Count);
        Assert.Equal(2, batch.SuccessCount);
        Assert.Equal(0, batch.FailureCount);
        Assert.Equal(TimeoutQuick, batch.TotalDuration);
    }

    [Fact]
    public void ShouldTrackFailures_WhenBatchHasFailures()
    {
        // Act
        var batch = new BatchOutput(
            Array.Empty<CrewOutput>(),
            SuccessCount: 3,
            FailureCount: 2,
            TotalDuration: TimeSpan.FromMinutes(1));

        // Assert
        Assert.Equal(3, batch.SuccessCount);
        Assert.Equal(2, batch.FailureCount);
    }

    // ══════════════ CrewOutput ══════════════

    [Fact]
    public void ShouldStoreAllFields_WhenCreatingCrewOutput()
    {
        // Act
        var output = new CrewOutput(
            "final answer",
            Array.Empty<Orkeon.Application.Execution.TaskOutput>(),
            TimeSpan.FromSeconds(45),
            new TokenUsage(500, 200, 700));

        // Assert
        Assert.Equal("final answer", output.FinalOutput);
        Assert.Empty(output.TaskOutputs);
        Assert.Equal(TimeSpan.FromSeconds(45), output.Duration);
        Assert.NotNull(output.TokensUsed);
        Assert.Equal(700, output.TokensUsed.TotalTokens);
    }

    [Fact]
    public void ShouldAllowNullTokenUsage_WhenTelemetryWasNotMeasured()
    {
        // R10.8 — null means "not measured" and must stay distinguishable from a
        // genuine zero-cost execution (no fabricated TokenUsage(0,0,0)).
        var output = new CrewOutput(
            "final answer",
            Array.Empty<Orkeon.Application.Execution.TaskOutput>(),
            TimeSpan.FromSeconds(1),
            TokensUsed: null);

        Assert.Null(output.TokensUsed);
    }

    // ══════════════ CrewExecutionId ══════════════

    [Fact]
    public void ShouldStoreValue_WhenCreatingCrewExecutionId()
    {
        // Arrange
        var ulid = Ulid.NewUlid();

        // Act
        var id = CrewExecutionId.From(ulid);

        // Assert
        Assert.Equal(ulid, id.Value);
        Assert.Equal(ulid.ToString(), id.AsString());
    }

    [Fact]
    public void ShouldRejectNonUlidString_WhenCreatingCrewExecutionIdFromString()
    {
        Assert.Throws<ArgumentException>(() => CrewExecutionId.From("exec-1"));
    }

    // ══════════════ CrewExecutionStatus ══════════════

    [Fact]
    public void ShouldStoreAllFields_WhenCreatingCrewExecutionStatus()
    {
        // Arrange
        var execId = CrewExecutionId.New();

        // Act
        var status = new CrewExecutionStatus(
            execId,
            ExecutionState.Running,
            0.75,
            "current-task",
            null);

        // Assert
        Assert.Equal(execId.Value, status.Id.Value);
        Assert.Equal(ExecutionState.Running, status.State);
        Assert.Equal(0.75, status.Progress);
        Assert.Equal("current-task", status.CurrentTask);
        Assert.Null(status.Error);
    }

    // ══════════════ TokenUsage cache dimension (W-08) ══════════════

    [Fact]
    public void CacheHitRatio_IsComputedFromTheMeasuredPartition()
    {
        var usage = new TokenUsage(PromptTokens: 12_000, CompletionTokens: 840, TotalTokens: 12_840)
        {
            CacheHitTokens = 7_980,
            CacheMissTokens = 4_020,
        };

        Assert.Equal(0.665, usage.CacheHitRatio!.Value, precision: 3);
    }

    [Fact]
    public void CacheHitRatio_IsNullWhenUnmeasured_NeverAFabricatedZero()
    {
        var unmeasured = new TokenUsage(100, 50, 150);
        Assert.Null(unmeasured.CacheHitRatio);

        // A measured pair summing to zero is equally "nothing to ratio".
        var empty = new TokenUsage(100, 50, 150) { CacheHitTokens = 0, CacheMissTokens = 0 };
        Assert.Null(empty.CacheHitRatio);
    }

    // ══════════════ ExecutionState enum ══════════════

    [Theory]
    [InlineData(ExecutionState.Pending)]
    [InlineData(ExecutionState.Running)]
    [InlineData(ExecutionState.Completed)]
    [InlineData(ExecutionState.Failed)]
    [InlineData(ExecutionState.Cancelled)]
    public void ShouldContainExpectedValue_WhenCheckingExecutionStateEnum(ExecutionState state)
    {
        // Assert — just verifying enum values exist
        Assert.True(Enum.IsDefined<ExecutionState>(state));
    }
}
