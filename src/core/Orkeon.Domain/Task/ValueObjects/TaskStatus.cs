namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>Represents the execution status of a task.</summary>
public sealed record TaskStatus
{
    /// <summary>Gets the string value of this status.</summary>
    public string Value { get; }
    private TaskStatus(string value) => Value = value;

    /// <summary>The task is queued but not yet started.</summary>
    public static readonly TaskStatus Pending = new("Pending");
    /// <summary>The task is currently being executed.</summary>
    public static readonly TaskStatus InProgress = new("InProgress");
    /// <summary>The task has been successfully completed.</summary>
    public static readonly TaskStatus Completed = new("Completed");
    /// <summary>The task execution has failed.</summary>
    public static readonly TaskStatus Failed = new("Failed");
    /// <summary>The task was cancelled before completion.</summary>
    public static readonly TaskStatus Cancelled = new("Cancelled");
    /// <summary>The task is blocked by an unresolved dependency.</summary>
    public static readonly TaskStatus Blocked = new("Blocked");
    private static readonly Dictionary<string, TaskStatus> s_all = new(StringComparer.OrdinalIgnoreCase)
    { [nameof(Pending)] = Pending, [nameof(InProgress)] = InProgress, [nameof(Completed)] = Completed, [nameof(Failed)] = Failed, [nameof(Cancelled)] = Cancelled, [nameof(Blocked)] = Blocked };
    /// <summary>Gets all known task statuses.</summary>
    public static IReadOnlyCollection<TaskStatus> All => s_all.Values;
    /// <summary>Returns the <see cref="TaskStatus"/> matching <paramref name="value"/>, or throws if unknown.</summary>
    public static TaskStatus From(string value) => s_all.TryGetValue(value, out var s) ? s : throw new ArgumentException($"Unknown TaskStatus: '{value}'", nameof(value));
    /// <summary>Tries to parse <paramref name="value"/> into a known <see cref="TaskStatus"/>.</summary>
    public static bool TryFrom(string? value, out TaskStatus? result) { if (value is not null && s_all.TryGetValue(value, out var f)) { result = f; return true; } result = null; return false; }
    /// <inheritdoc />
    public override string ToString() => Value;
    /// <summary>Implicitly converts a <see cref="TaskStatus"/> to its string value.</summary>
    public static implicit operator string(TaskStatus s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Value;
    }
}
