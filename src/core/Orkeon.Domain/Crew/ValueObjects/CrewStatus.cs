namespace Orkeon.Domain.Crew.ValueObjects;

/// <summary>Represents the execution status of a crew.</summary>
public sealed record CrewStatus
{
    /// <summary>Gets the string value of this status.</summary>
    public string Value { get; }
    private CrewStatus(string value) => Value = value;

    /// <summary>The crew has been created but not yet started.</summary>
    public static readonly CrewStatus Created = new("Created");
    /// <summary>The crew is idle and waiting for work.</summary>
    public static readonly CrewStatus Idle = new("Idle");
    /// <summary>The crew is initializing its members and resources.</summary>
    public static readonly CrewStatus Initializing = new("Initializing");
    /// <summary>The crew is actively executing tasks.</summary>
    public static readonly CrewStatus Executing = new("Executing");
    /// <summary>The crew execution has been paused.</summary>
    public static readonly CrewStatus Paused = new("Paused");
    /// <summary>The crew has successfully completed all tasks.</summary>
    public static readonly CrewStatus Completed = new("Completed");
    /// <summary>The crew execution has failed.</summary>
    public static readonly CrewStatus Failed = new("Failed");
    /// <summary>The crew execution was cancelled.</summary>
    public static readonly CrewStatus Cancelled = new("Cancelled");
    /// <summary>The crew is in an error state.</summary>
    public static readonly CrewStatus Error = new("Error");
    private static readonly Dictionary<string, CrewStatus> s_all = new(StringComparer.OrdinalIgnoreCase)
    { [nameof(Created)] = Created, [nameof(Idle)] = Idle, [nameof(Initializing)] = Initializing, [nameof(Executing)] = Executing, [nameof(Paused)] = Paused, [nameof(Completed)] = Completed, [nameof(Failed)] = Failed, [nameof(Cancelled)] = Cancelled, [nameof(Error)] = Error };
    /// <summary>Gets all known crew statuses.</summary>
    public static IReadOnlyCollection<CrewStatus> All => s_all.Values;
    /// <summary>Returns the <see cref="CrewStatus"/> matching <paramref name="value"/>, or throws if unknown.</summary>
    public static CrewStatus From(string value) => s_all.TryGetValue(value, out var s) ? s : throw new ArgumentException($"Unknown CrewStatus: '{value}'", nameof(value));
    /// <summary>Tries to parse <paramref name="value"/> into a known <see cref="CrewStatus"/>.</summary>
    public static bool TryFrom(string? value, out CrewStatus? result) { if (value is not null && s_all.TryGetValue(value, out var f)) { result = f; return true; } result = null; return false; }
    /// <inheritdoc />
    public override string ToString() => Value;
    /// <summary>Implicitly converts a <see cref="CrewStatus"/> to its string value.</summary>
    public static implicit operator string(CrewStatus s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Value;
    }
}
