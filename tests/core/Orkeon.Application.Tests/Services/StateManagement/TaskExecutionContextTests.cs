using Orkeon.Application.Services.StateManagement;

namespace Orkeon.Application.Tests.Services.StateManagement;

/// <summary>
/// Focused tests for the <see cref="TaskExecutionContext"/> record's grouped-record surface:
/// the <see cref="TaskExecutionContext.Readiness"/>/<see cref="TaskExecutionContext.StatusFlags"/>
/// computed projections and the grouped-flags constructor overload. The positional constructor is
/// already exercised by TaskStateManagerTests; these cases cover the record sugar that the state
/// machine tests never touch.
/// </summary>
public class TaskExecutionContextTests
{
    private static TaskExecutionContext BuildPositional() => new(
        AgentAvailable: true,
        PrerequisitesMet: true,
        CanResume: false,
        CanRetry: true,
        OutputValid: true,
        RequiresReview: false,
        ShouldPause: true,
        IsNearCompletion: false,
        IsNearDeadline: true,
        RetryCount: 3,
        ExecutionTime: TimeSpan.FromMinutes(15));

    [Fact]
    public void Readiness_ProjectsTheFourReadinessFlags()
    {
        var ctx = BuildPositional();

        var readiness = ctx.Readiness;

        Assert.Equal(new TaskReadinessFlags(true, true, false, true), readiness);
    }

    [Fact]
    public void StatusFlags_ProjectsTheFiveStatusFlags()
    {
        var ctx = BuildPositional();

        var status = ctx.StatusFlags;

        Assert.Equal(new TaskStatusFlags(true, false, true, false, true), status);
    }

    [Fact]
    public void GroupedConstructor_ProducesSamePositionalState()
    {
        var readiness = new TaskReadinessFlags(true, false, true, false);
        var status = new TaskStatusFlags(false, true, false, true, false);

        var ctx = new TaskExecutionContext(readiness, status, retryCount: 7, executionTime: TimeSpan.FromHours(2));

        Assert.True(ctx.AgentAvailable);
        Assert.False(ctx.PrerequisitesMet);
        Assert.True(ctx.CanResume);
        Assert.False(ctx.CanRetry);
        Assert.False(ctx.OutputValid);
        Assert.True(ctx.RequiresReview);
        Assert.False(ctx.ShouldPause);
        Assert.True(ctx.IsNearCompletion);
        Assert.False(ctx.IsNearDeadline);
        Assert.Equal(7, ctx.RetryCount);
        Assert.Equal(TimeSpan.FromHours(2), ctx.ExecutionTime);
    }

    [Fact]
    public void GroupedConstructor_RoundTripsThroughProjections()
    {
        var readiness = new TaskReadinessFlags(false, true, true, false);
        var status = new TaskStatusFlags(true, true, false, false, true);

        var ctx = new TaskExecutionContext(readiness, status);

        Assert.Equal(readiness, ctx.Readiness);
        Assert.Equal(status, ctx.StatusFlags);
    }

    [Fact]
    public void GroupedConstructor_DefaultsRetryCountAndExecutionTime()
    {
        var ctx = new TaskExecutionContext(
            new TaskReadinessFlags(true, true, true, true),
            new TaskStatusFlags(true, true, true, true, true));

        Assert.Equal(0, ctx.RetryCount);
        Assert.Equal(default, ctx.ExecutionTime);
    }

    [Fact]
    public void WithExpression_PreservesBackwardCompatibility()
    {
        var ctx = BuildPositional();

        var mutated = ctx with { RetryCount = 99, IsNearDeadline = false };

        Assert.Equal(99, mutated.RetryCount);
        Assert.False(mutated.IsNearDeadline);
        // Untouched fields survive the copy.
        Assert.True(mutated.AgentAvailable);
        Assert.Equal(TimeSpan.FromMinutes(15), mutated.ExecutionTime);
    }
}
