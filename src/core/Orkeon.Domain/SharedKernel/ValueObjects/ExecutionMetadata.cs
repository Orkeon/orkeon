using System.Collections.Immutable;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Groups timing-related execution metadata.
/// </summary>
public sealed record ExecutionTimingInfo(
    DateTime StartedAt,
    DateTime? CompletedAt,
    TimeSpan? MaxExecutionTime);

/// <summary>
/// Groups identity-related execution metadata.
/// </summary>
public sealed record ExecutionIdentityInfo(
    string ExecutionId,
    string? ParentExecutionId);

/// <summary>
/// Strongly typed execution metadata.
/// </summary>
public sealed record ExecutionMetadata : ValueObjectRecord
{
    /// <summary>Timing information for this execution.</summary>
    public ExecutionTimingInfo Timing { get; init; }
    /// <summary>Identity information for this execution.</summary>
    public ExecutionIdentityInfo Identity { get; init; }
    /// <summary>Number of retries so far.</summary>
    public int RetryCount { get; init; }
    /// <summary>Last error message, if any.</summary>
    public string? LastError { get; init; }
    /// <summary>Arbitrary string tags.</summary>
    public ImmutableDictionary<string, string> Tags { get; init; }
    /// <summary>Arbitrary custom properties.</summary>
    public ImmutableDictionary<string, object> CustomProperties { get; init; }

    /// <summary>
    /// Primary constructor with validation.
    /// </summary>
    public ExecutionMetadata(
        ExecutionTimingInfo Timing,
        ExecutionIdentityInfo Identity,
        int RetryCount,
        string? LastError,
        ImmutableDictionary<string, string> Tags,
        ImmutableDictionary<string, object> CustomProperties)
    {
        ArgumentNullException.ThrowIfNull(Timing);
        ArgumentNullException.ThrowIfNull(Identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(Identity.ExecutionId);

        if (RetryCount < 0)
            throw new ArgumentOutOfRangeException(nameof(RetryCount), "RetryCount cannot be negative.");

        if (Timing.CompletedAt.HasValue && Timing.CompletedAt.Value < Timing.StartedAt)
            throw new ArgumentException("CompletedAt cannot be earlier than StartedAt.", nameof(Timing));

        this.Timing = Timing;
        this.Identity = Identity;
        this.RetryCount = RetryCount;
        this.LastError = LastError;
        this.Tags = Tags;
        this.CustomProperties = CustomProperties;
    }

    /// <summary>
    /// Convenience constructor for backward compatibility.
    /// </summary>
#pragma warning disable S107 // Backward-compatible overload; use primary constructor with ExecutionTimingInfo and ExecutionIdentityInfo instead
    public ExecutionMetadata(
        DateTime StartedAt,
        DateTime? CompletedAt,
        TimeSpan? MaxExecutionTime,
        string ExecutionId,
        string? ParentExecutionId,
        int RetryCount,
        string? LastError,
        ImmutableDictionary<string, string> Tags,
        ImmutableDictionary<string, object> CustomProperties)
        : this(
            new ExecutionTimingInfo(StartedAt, CompletedAt, MaxExecutionTime),
            new ExecutionIdentityInfo(ExecutionId, ParentExecutionId),
            RetryCount, LastError, Tags, CustomProperties)
    {
    }
#pragma warning restore S107

    // Backward-compatible accessors
    /// <summary>When the execution started.</summary>
    public DateTime StartedAt => Timing.StartedAt;
    /// <summary>When the execution completed (null if still running).</summary>
    public DateTime? CompletedAt => Timing.CompletedAt;
    /// <summary>Maximum allowed execution time.</summary>
    public TimeSpan? MaxExecutionTime => Timing.MaxExecutionTime;
    /// <summary>Unique execution identifier.</summary>
    public string ExecutionId => Identity.ExecutionId;
    /// <summary>Parent execution identifier for nested executions.</summary>
    public string? ParentExecutionId => Identity.ParentExecutionId;

    /// <summary>
    /// Gets the execution duration.
    /// </summary>
    public TimeSpan? Duration => CompletedAt?.Subtract(StartedAt);

    /// <summary>
    /// Gets whether the execution is complete.
    /// </summary>
    public bool IsComplete => CompletedAt.HasValue;

    /// <summary>
    /// Gets whether the execution has exceeded max time.
    /// </summary>
    public bool IsTimedOut => MaxExecutionTime.HasValue &&
        Duration.HasValue &&
        Duration.Value > MaxExecutionTime.Value;

    /// <summary>
    /// Creates metadata for a new execution.
    /// </summary>
    public static ExecutionMetadata CreateNew(
        TimeSpan? maxExecutionTime = null,
        string? parentExecutionId = null,
        ImmutableDictionary<string, string>? tags = null)
    {
        return new ExecutionMetadata(
            new ExecutionTimingInfo(DateTime.UtcNow, null, maxExecutionTime),
            new ExecutionIdentityInfo(Guid.NewGuid().ToString(), parentExecutionId),
            RetryCount: 0,
            LastError: null,
            Tags: tags ?? [],
            CustomProperties: []);
    }

    /// <summary>
    /// Marks the execution as complete.
    /// </summary>
    public ExecutionMetadata Complete(string? error = null)
    {
        return this with
        {
            Timing = Timing with { CompletedAt = DateTime.UtcNow },
            LastError = error
        };
    }

    /// <summary>
    /// Increments the retry count.
    /// </summary>
    public ExecutionMetadata WithRetry(string error)
    {
        return this with
        {
            RetryCount = RetryCount + 1,
            LastError = error
        };
    }

    /// <summary>
    /// Adds or updates a tag.
    /// </summary>
    public ExecutionMetadata WithTag(string key, string value)
    {
        return this with { Tags = Tags.SetItem(key, value) };
    }

    /// <summary>
    /// Adds or updates a custom property.
    /// </summary>
    public ExecutionMetadata WithCustomProperty(string key, object value)
    {
        return this with { CustomProperties = CustomProperties.SetItem(key, value) };
    }

}
