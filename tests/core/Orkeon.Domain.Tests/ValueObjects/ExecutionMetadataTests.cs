using System.Collections.Immutable;
using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;
using Orkeon.Domain.Tests.Fixtures;

namespace Orkeon.Domain.Tests.ValueObjects;

public class ExecutionMetadataTests
{
    [Fact]
    public void ShouldCreateCorrectly_WhenConstructingWithAllParameters()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var completedAt = startedAt.AddMinutes(5);
        var maxExecutionTime = TimeoutExtended;
        var executionId = Guid.NewGuid().ToString();
        var parentExecutionId = Guid.NewGuid().ToString();
        var retryCount = 3;
        var lastError = "Connection timeout";
        var tags = ImmutableDictionary<string, string>.Empty
            .Add("environment", "production")
            .Add("version", "1.0");
        var customProperties = ImmutableDictionary<string, object>.Empty
            .Add("userId", "user123")
            .Add("priority", 5);

        // Act
        var metadata = new ExecutionMetadata(
            startedAt,
            completedAt,
            maxExecutionTime,
            executionId,
            parentExecutionId,
            retryCount,
            lastError,
            tags,
            customProperties);

        // Assert
        Assert.Equal(startedAt, metadata.StartedAt);
        Assert.Equal(completedAt, metadata.CompletedAt);
        Assert.Equal(maxExecutionTime, metadata.MaxExecutionTime);
        Assert.Equal(executionId, metadata.ExecutionId);
        Assert.Equal(parentExecutionId, metadata.ParentExecutionId);
        Assert.Equal(retryCount, metadata.RetryCount);
        Assert.Equal(lastError, metadata.LastError);
        Assert.Equal(tags, metadata.Tags);
        Assert.Equal(customProperties, metadata.CustomProperties);
    }

    [Fact]
    public void ShouldCalculateCorrectly_WhenUsingDurationWithCompletedExecution()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var completedAt = startedAt.AddMinutes(5.5);
        var metadata = new ExecutionMetadata(
            startedAt,
            completedAt,
            null,
            "exec-123",
            null,
            0,
            null,
            [],
            []);

        // Act
        var duration = metadata.Duration;

        // Assert
        Assert.NotNull(duration);
        Assert.Equal(TimeSpan.FromMinutes(5.5), duration.Value);
    }

    [Fact]
    public void ShouldReturnNull_WhenUsingDurationWithIncompleteExecution()
    {
        // Arrange
        var metadata = new ExecutionMetadata(
            DateTime.UtcNow,
            null, // Not completed
            null,
            "exec-123",
            null,
            0,
            null,
            [],
            []);

        // Act
        var duration = metadata.Duration;

        // Assert
        Assert.Null(duration);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsCompleteWithCompletedAt()
    {
        // Arrange
        var metadata = new ExecutionMetadata(
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(1),
            null,
            "exec-123",
            null,
            0,
            null,
            [],
            []);

        // Act & Assert
        Assert.True(metadata.IsComplete);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsCompleteWithoutCompletedAt()
    {
        // Arrange
        var metadata = new ExecutionMetadata(
            DateTime.UtcNow,
            null,
            null,
            "exec-123",
            null,
            0,
            null,
            [],
            []);

        // Act & Assert
        Assert.False(metadata.IsComplete);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsTimedOutWhenDurationExceedsMaxTime()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var completedAt = startedAt.AddMinutes(15);
        var maxExecutionTime = TimeoutExtended;

        var metadata = new ExecutionMetadata(
            startedAt,
            completedAt,
            maxExecutionTime,
            "exec-123",
            null,
            0,
            null,
            [],
            []);

        // Act & Assert
        Assert.True(metadata.IsTimedOut);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsTimedOutWhenDurationWithinMaxTime()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var completedAt = startedAt.AddMinutes(5);
        var maxExecutionTime = TimeoutExtended;

        var metadata = new ExecutionMetadata(
            startedAt,
            completedAt,
            maxExecutionTime,
            "exec-123",
            null,
            0,
            null,
            [],
            []);

        // Act & Assert
        Assert.False(metadata.IsTimedOut);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsTimedOutWithoutMaxExecutionTime()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var completedAt = startedAt.AddHours(1);

        var metadata = new ExecutionMetadata(
            startedAt,
            completedAt,
            null, // No max execution time
            "exec-123",
            null,
            0,
            null,
            [],
            []);

        // Act & Assert
        Assert.False(metadata.IsTimedOut);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsTimedOutWithoutCompletedAt()
    {
        // Arrange
        var metadata = new ExecutionMetadata(
            DateTime.UtcNow,
            null, // Not completed
            TimeoutExtended,
            "exec-123",
            null,
            0,
            null,
            [],
            []);

        // Act & Assert
        Assert.False(metadata.IsTimedOut);
    }

    [Fact]
    public void ShouldCreateCorrectly_WhenCreatingNewWithDefaults()
    {
        // Act
        var metadata = ExecutionMetadata.CreateNew();

        // Assert
        Assert.True(metadata.StartedAt <= DateTime.UtcNow);
        Assert.True(metadata.StartedAt > DateTime.UtcNow.AddSeconds(-1));
        Assert.Null(metadata.CompletedAt);
        Assert.Null(metadata.MaxExecutionTime);
        Assert.NotNull(metadata.ExecutionId);
        Assert.NotEmpty(metadata.ExecutionId);
        Assert.True(Guid.TryParse(metadata.ExecutionId, out _));
        Assert.Null(metadata.ParentExecutionId);
        Assert.Equal(0, metadata.RetryCount);
        Assert.Null(metadata.LastError);
        Assert.Empty(metadata.Tags);
        Assert.Empty(metadata.CustomProperties);
    }

    [Fact]
    public void ShouldSetCorrectly_WhenCreatingNewWithParameters()
    {
        // Arrange
        var maxExecutionTime = TimeSpan.FromMinutes(30);
        var parentExecutionId = "parent-123";
        var tags = ImmutableDictionary<string, string>.Empty
            .Add("service", "api")
            .Add("region", "us-west");

        // Act
        var metadata = ExecutionMetadata.CreateNew(maxExecutionTime, parentExecutionId, tags);

        // Assert
        Assert.Equal(maxExecutionTime, metadata.MaxExecutionTime);
        Assert.Equal(parentExecutionId, metadata.ParentExecutionId);
        Assert.Equal(tags, metadata.Tags);
        Assert.Equal("api", metadata.Tags["service"]);
        Assert.Equal("us-west", metadata.Tags["region"]);
    }

    [Fact]
    public void ShouldMarkAsCompleted_WhenCompletingWithoutError()
    {
        // Arrange
        var metadata = ExecutionMetadata.CreateNew();
        ClockAdvance.UntilStrictlyAfter(metadata.StartedAt); // Ensure time difference (R5.6)

        // Act
        var completed = metadata.Complete();

        // Assert
        Assert.NotNull(completed.CompletedAt);
        Assert.True(completed.CompletedAt > metadata.StartedAt);
        Assert.True(completed.IsComplete);
        Assert.Null(completed.LastError);
        Assert.NotNull(completed.Duration);
    }

    [Fact]
    public void ShouldMarkAsCompletedWithError_WhenCompletingWithError()
    {
        // Arrange
        var metadata = ExecutionMetadata.CreateNew();
        var error = "Task failed due to invalid input";

        // Act
        var completed = metadata.Complete(error);

        // Assert
        Assert.NotNull(completed.CompletedAt);
        Assert.True(completed.IsComplete);
        Assert.Equal(error, completed.LastError);
    }

    [Fact]
    public void ShouldIncrementRetryCountAndSetError_WhenUsingWithRetry()
    {
        // Arrange
        var metadata = ExecutionMetadata.CreateNew();
        var error1 = "First attempt failed";
        var error2 = "Second attempt failed";

        // Act
        var afterFirstRetry = metadata.WithRetry(error1);
        var afterSecondRetry = afterFirstRetry.WithRetry(error2);

        // Assert
        Assert.Equal(0, metadata.RetryCount);
        Assert.Null(metadata.LastError);

        Assert.Equal(1, afterFirstRetry.RetryCount);
        Assert.Equal(error1, afterFirstRetry.LastError);

        Assert.Equal(2, afterSecondRetry.RetryCount);
        Assert.Equal(error2, afterSecondRetry.LastError);
    }

    [Fact]
    public void ShouldAddOrUpdateTag_WhenUsingWithTag()
    {
        // Arrange
        var metadata = ExecutionMetadata.CreateNew();

        // Act
        var withFirstTag = metadata.WithTag("env", "dev");
        var withSecondTag = withFirstTag.WithTag("version", "2.0");
        var withUpdatedTag = withSecondTag.WithTag("env", "prod");

        // Assert
        Assert.Empty(metadata.Tags);

        Assert.Single(withFirstTag.Tags);
        Assert.Equal("dev", withFirstTag.Tags["env"]);

        Assert.Equal(2, withSecondTag.Tags.Count);
        Assert.Equal("dev", withSecondTag.Tags["env"]);
        Assert.Equal("2.0", withSecondTag.Tags["version"]);

        Assert.Equal(2, withUpdatedTag.Tags.Count);
        Assert.Equal("prod", withUpdatedTag.Tags["env"]); // Updated
        Assert.Equal("2.0", withUpdatedTag.Tags["version"]);
    }

    [Fact]
    public void ShouldAddOrUpdateProperty_WhenUsingWithCustomProperty()
    {
        // Arrange
        var metadata = ExecutionMetadata.CreateNew();

        // Act
        var withFirstProp = metadata.WithCustomProperty("userId", "user123");
        var withSecondProp = withFirstProp.WithCustomProperty("priority", 5);
        var withComplexProp = withSecondProp.WithCustomProperty("settings", new { debug = true, timeout = 30 });

        // Assert
        Assert.Empty(metadata.CustomProperties);

        Assert.Single(withFirstProp.CustomProperties);
        Assert.Equal("user123", withFirstProp.CustomProperties["userId"]);

        Assert.Equal(2, withSecondProp.CustomProperties.Count);
        Assert.Equal("user123", withSecondProp.CustomProperties["userId"]);
        Assert.Equal(5, withSecondProp.CustomProperties["priority"]);

        Assert.Equal(3, withComplexProp.CustomProperties.Count);
        Assert.NotNull(withComplexProp.CustomProperties["settings"]);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;
        var tags = ImmutableDictionary<string, string>.Empty.Add("key", "value");
        var props = ImmutableDictionary<string, object>.Empty.Add("prop", 123);

        var metadata1 = new ExecutionMetadata(
            startedAt, null, null, "exec-123", null, 0, null, tags, props);
        var metadata2 = new ExecutionMetadata(
            startedAt, null, null, "exec-123", null, 0, null, tags, props);

        // Act & Assert
        Assert.Equal(metadata1, metadata2);
        Assert.True(metadata1 == metadata2);
        Assert.False(metadata1 != metadata2);
        Assert.Equal(metadata1.GetHashCode(), metadata2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var metadata1 = ExecutionMetadata.CreateNew();
        var metadata2 = ExecutionMetadata.CreateNew();

        // Act & Assert
        Assert.NotEqual(metadata1, metadata2); // Different ExecutionIds
        Assert.False(metadata1 == metadata2);
        Assert.True(metadata1 != metadata2);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithModifiedProperty_WhenRecordingWith()
    {
        // Arrange
        var original = ExecutionMetadata.CreateNew();

        // Act
        var modified = original with { RetryCount = 5 };

        // Assert
        Assert.NotSame(original, modified);
        Assert.Equal(0, original.RetryCount);
        Assert.Equal(5, modified.RetryCount);

        // Other properties should be the same
        Assert.Equal(original.ExecutionId, modified.ExecutionId);
        Assert.Equal(original.StartedAt, modified.StartedAt);
    }

    [Fact]
    public void ShouldFullExecutionLifecycle_WhenUsingComplexScenario()
    {
        // Arrange
        var maxExecutionTime = TimeoutStandard;
        var parentId = "parent-exec-456";
        var tags = ImmutableDictionary<string, string>.Empty
            .Add("service", "worker")
            .Add("environment", "staging");

        // Act - Create new execution
        var metadata = ExecutionMetadata.CreateNew(maxExecutionTime, parentId, tags);

        // Add custom properties
        metadata = metadata
            .WithCustomProperty("workerId", "worker-001")
            .WithCustomProperty("batchSize", 100);

        // First attempt fails
        metadata = metadata.WithRetry("Network error");

        // Second attempt fails
        metadata = metadata.WithRetry("Database connection lost");

        // Add debug tag
        metadata = metadata.WithTag("debug", "true");

        // Third attempt succeeds
        ClockAdvance.UntilStrictlyAfter(metadata.StartedAt);
        metadata = metadata.Complete();

        // Assert
        Assert.Equal(maxExecutionTime, metadata.MaxExecutionTime);
        Assert.Equal(parentId, metadata.ParentExecutionId);
        Assert.Equal(2, metadata.RetryCount);
        Assert.Null(metadata.LastError); // No error on successful completion
        Assert.True(metadata.IsComplete);
        Assert.NotNull(metadata.Duration);
        Assert.False(metadata.IsTimedOut);

        Assert.Equal(3, metadata.Tags.Count);
        Assert.Equal("worker", metadata.Tags["service"]);
        Assert.Equal("staging", metadata.Tags["environment"]);
        Assert.Equal("true", metadata.Tags["debug"]);

        Assert.Equal(2, metadata.CustomProperties.Count);
        Assert.Equal("worker-001", metadata.CustomProperties["workerId"]);
        Assert.Equal(100, metadata.CustomProperties["batchSize"]);
    }

    [Fact]
    public void ShouldTimedOutExecution_WhenUsingComplexScenario()
    {
        // Arrange
        var startedAt = DateTime.UtcNow.AddMinutes(-10);
        var completedAt = DateTime.UtcNow;
        var maxExecutionTime = TimeoutStandard;

        var metadata = new ExecutionMetadata(
            startedAt,
            completedAt,
            maxExecutionTime,
            "exec-timeout",
            null,
            3,
            "Execution timed out",
            [],
            []);

        // Act & Assert
        Assert.True(metadata.IsComplete);
        Assert.True(metadata.IsTimedOut);
        Assert.True(Math.Abs((TimeoutExtended - metadata.Duration!.Value).TotalMilliseconds) < 1);
        Assert.Equal("Execution timed out", metadata.LastError);
    }
}
