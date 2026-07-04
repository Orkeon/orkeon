using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Tools;

namespace Orkeon.Application.Tests.Interfaces;

public class AgentExitReasonTests
{
    // ══════════════ AgentExitReason enum ══════════════

    [Fact]
    public void AgentExitReason_Enum_HasExpectedValues()
    {
        // Assert — all 5 values exist and have expected integer values
        Assert.Equal(0, (int)AgentExitReason.Completed);
        Assert.Equal(1, (int)AgentExitReason.MaxIterationsReached);
        Assert.Equal(2, (int)AgentExitReason.CircuitBreakerTripped);
        Assert.Equal(3, (int)AgentExitReason.Cancelled);
        Assert.Equal(4, (int)AgentExitReason.BudgetExhausted);

        // There should be exactly 5 values
        var values = Enum.GetValues<AgentExitReason>();
        Assert.Equal(5, values.Length);
    }

    [Theory]
    [InlineData(AgentExitReason.Completed, "Completed")]
    [InlineData(AgentExitReason.MaxIterationsReached, "MaxIterationsReached")]
    [InlineData(AgentExitReason.CircuitBreakerTripped, "CircuitBreakerTripped")]
    [InlineData(AgentExitReason.Cancelled, "Cancelled")]
    [InlineData(AgentExitReason.BudgetExhausted, "BudgetExhausted")]
    public void AgentExitReason_ToString_ReturnsExpectedName(AgentExitReason reason, string expectedName)
    {
        Assert.Equal(expectedName, reason.ToString());
    }

    // ══════════════ TaskResult defaults ══════════════

    [Fact]
    public void TaskResult_DefaultExitReason_IsCompleted()
    {
        // Arrange & Act
        var result = new TaskResult(
            Success: true,
            Output: "some output",
            StructuredOutput: null,
            ToolsUsed: Array.Empty<ToolUsage>(),
            ExecutionTime: TimeSpan.FromSeconds(1));

        // Assert — new properties should have sensible defaults
        Assert.Equal(AgentExitReason.Completed, result.ExitReason);
        Assert.Equal(0, result.IterationsUsed);
        Assert.Null(result.LastError);
    }

    [Fact]
    public void TaskResult_DefaultTokensUsed_IsZero()
    {
        // Verify the existing default parameter still works
        var result = new TaskResult(
            Success: true,
            Output: "ok",
            StructuredOutput: null,
            ToolsUsed: Array.Empty<ToolUsage>(),
            ExecutionTime: TimeSpan.Zero);

        Assert.Equal(0, result.TokensUsed);
    }

    // ══════════════ TaskResult with each exit reason ══════════════

    [Theory]
    [InlineData(AgentExitReason.Completed)]
    [InlineData(AgentExitReason.MaxIterationsReached)]
    [InlineData(AgentExitReason.CircuitBreakerTripped)]
    [InlineData(AgentExitReason.Cancelled)]
    [InlineData(AgentExitReason.BudgetExhausted)]
    public void TaskResult_CanSetAllExitReasons(AgentExitReason exitReason)
    {
        // Arrange & Act
        var result = new TaskResult(
            Success: exitReason == AgentExitReason.Completed,
            Output: "output",
            StructuredOutput: null,
            ToolsUsed: Array.Empty<ToolUsage>(),
            ExecutionTime: TimeSpan.FromMilliseconds(500))
        {
            ExitReason = exitReason,
            IterationsUsed = 5,
            LastError = exitReason == AgentExitReason.Completed ? null : "some error"
        };

        // Assert
        Assert.Equal(exitReason, result.ExitReason);
        Assert.Equal(5, result.IterationsUsed);
        if (exitReason == AgentExitReason.Completed)
            Assert.Null(result.LastError);
        else
            Assert.Equal("some error", result.LastError);
    }

    [Fact]
    public void TaskResult_MaxIterationsReached_CarriesIterationCount()
    {
        // Arrange & Act
        var result = new TaskResult(
            Success: false,
            Output: "Max iterations reached...",
            StructuredOutput: null,
            ToolsUsed: Array.Empty<ToolUsage>(),
            ExecutionTime: TimeSpan.FromSeconds(30),
            TokensUsed: 1500)
        {
            ExitReason = AgentExitReason.MaxIterationsReached,
            IterationsUsed = 25,
            LastError = "Agent did not produce a final answer within the allowed iterations"
        };

        // Assert
        Assert.False(result.Success);
        Assert.Equal(AgentExitReason.MaxIterationsReached, result.ExitReason);
        Assert.Equal(25, result.IterationsUsed);
        Assert.Equal(1500, result.TokensUsed);
        Assert.Contains("final answer", result.LastError);
    }

    [Fact]
    public void TaskResult_CircuitBreakerTripped_CarriesLastError()
    {
        // Arrange & Act
        var result = new TaskResult(
            Success: false,
            Output: string.Empty,
            StructuredOutput: null,
            ToolsUsed: Array.Empty<ToolUsage>(),
            ExecutionTime: TimeSpan.FromSeconds(10))
        {
            ExitReason = AgentExitReason.CircuitBreakerTripped,
            IterationsUsed = 3,
            LastError = "Repeated identical errors: NullReferenceException"
        };

        // Assert
        Assert.False(result.Success);
        Assert.Equal(AgentExitReason.CircuitBreakerTripped, result.ExitReason);
        Assert.Equal(3, result.IterationsUsed);
        Assert.Contains("NullReferenceException", result.LastError);
    }

    [Fact]
    public void TaskResult_WithRecord_CanUseWithExpression()
    {
        // Arrange — start with a completed result
        var completed = new TaskResult(
            Success: true,
            Output: "good output",
            StructuredOutput: null,
            ToolsUsed: Array.Empty<ToolUsage>(),
            ExecutionTime: TimeSpan.FromSeconds(2))
        {
            ExitReason = AgentExitReason.Completed,
            IterationsUsed = 3
        };

        // Act — mutate via with expression
        var exhausted = completed with
        {
            Success = false,
            ExitReason = AgentExitReason.MaxIterationsReached,
            IterationsUsed = 25,
            LastError = "exhausted"
        };

        // Assert — original unchanged
        Assert.Equal(AgentExitReason.Completed, completed.ExitReason);
        Assert.Equal(3, completed.IterationsUsed);
        Assert.Null(completed.LastError);

        // Assert — copy has new values
        Assert.Equal(AgentExitReason.MaxIterationsReached, exhausted.ExitReason);
        Assert.Equal(25, exhausted.IterationsUsed);
        Assert.Equal("exhausted", exhausted.LastError);
        Assert.False(exhausted.Success);
    }

    [Fact]
    public void TaskResult_BackwardCompatibility_ExistingConstructorStillWorks()
    {
        // This test ensures that code creating TaskResult with the existing positional
        // constructor (without the new init-only properties) still compiles and works.
        var result = new TaskResult(
            Success: true,
            Output: "backward compatible",
            StructuredOutput: new { Key = "value" },
            ToolsUsed: Array.Empty<ToolUsage>(),
            ExecutionTime: TimeSpan.FromSeconds(1),
            Error: null,
            TokensUsed: 42);

        Assert.True(result.Success);
        Assert.Equal("backward compatible", result.Output);
        Assert.Equal(42, result.TokensUsed);
        // New properties should be at their defaults
        Assert.Equal(AgentExitReason.Completed, result.ExitReason);
        Assert.Equal(0, result.IterationsUsed);
        Assert.Null(result.LastError);
    }
}
