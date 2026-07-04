namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>Represents the priority level of a task, with an ordering for comparison.</summary>
public sealed record TaskPriority
{
    /// <summary>Gets the string value of this priority.</summary>
    public string Value { get; }
    /// <summary>Gets the numeric ordering of this priority (higher value = higher priority).</summary>
    public int Order { get; }
    private TaskPriority(string value, int order) { Value = value; Order = order; }

    /// <summary>Low priority (order 1).</summary>
    public static readonly TaskPriority Low = new("Low", 1);
    /// <summary>Normal priority (order 2).</summary>
    public static readonly TaskPriority Normal = new("Normal", 2);
    /// <summary>High priority (order 3).</summary>
    public static readonly TaskPriority High = new("High", 3);
    /// <summary>Critical priority (order 4).</summary>
    public static readonly TaskPriority Critical = new("Critical", 4);
    /// <summary>Urgent priority, the highest level (order 5).</summary>
    public static readonly TaskPriority Urgent = new("Urgent", 5);
    private static readonly Dictionary<string, TaskPriority> s_all = new(StringComparer.OrdinalIgnoreCase)
    { [nameof(Low)] = Low, [nameof(Normal)] = Normal, [nameof(High)] = High, [nameof(Critical)] = Critical, [nameof(Urgent)] = Urgent };
    /// <summary>Gets all known task priorities.</summary>
    public static IReadOnlyCollection<TaskPriority> All => s_all.Values;
    /// <summary>Returns the <see cref="TaskPriority"/> matching <paramref name="value"/>, or throws if unknown.</summary>
    public static TaskPriority From(string value) => s_all.TryGetValue(value, out var s) ? s : throw new ArgumentException($"Unknown TaskPriority: '{value}'", nameof(value));
    /// <summary>Tries to parse <paramref name="value"/> into a known <see cref="TaskPriority"/>.</summary>
    public static bool TryFrom(string? value, out TaskPriority? result) { if (value is not null && s_all.TryGetValue(value, out var f)) { result = f; return true; } result = null; return false; }
    /// <inheritdoc />
    public override string ToString() => Value;
    /// <summary>Implicitly converts a <see cref="TaskPriority"/> to its string value.</summary>
    public static implicit operator string(TaskPriority s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Value;
    }
}
