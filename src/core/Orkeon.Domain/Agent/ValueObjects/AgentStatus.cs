namespace Orkeon.Domain.Agent.ValueObjects;

/// <summary>Represents the operational status of an agent.</summary>
public sealed record AgentStatus
{
    /// <summary>Gets the string value of this status.</summary>
    public string Value { get; }
    private AgentStatus(string value) => Value = value;

    /// <summary>The agent has been created but not yet initialized.</summary>
    public static readonly AgentStatus Created = new("Created");
    /// <summary>The agent is initialized and waiting for work.</summary>
    public static readonly AgentStatus Idle = new("Idle");
    /// <summary>The agent is actively executing a task.</summary>
    public static readonly AgentStatus Busy = new("Busy");
    /// <summary>The agent is temporarily unavailable.</summary>
    public static readonly AgentStatus Unavailable = new("Unavailable");
    /// <summary>The agent has been deactivated.</summary>
    public static readonly AgentStatus Deactivated = new("Deactivated");
    /// <summary>The agent is in an error state.</summary>
    public static readonly AgentStatus Error = new("Error");
    private static readonly Dictionary<string, AgentStatus> s_all = new(StringComparer.OrdinalIgnoreCase)
    { [nameof(Created)] = Created, [nameof(Idle)] = Idle, [nameof(Busy)] = Busy, [nameof(Unavailable)] = Unavailable, [nameof(Deactivated)] = Deactivated, [nameof(Error)] = Error };
    /// <summary>Gets all known agent statuses.</summary>
    public static IReadOnlyCollection<AgentStatus> All => s_all.Values;
    /// <summary>Returns the <see cref="AgentStatus"/> matching <paramref name="value"/>, or throws if unknown.</summary>
    public static AgentStatus From(string value) => s_all.TryGetValue(value, out var s) ? s : throw new ArgumentException($"Unknown AgentStatus: '{value}'", nameof(value));
    /// <summary>Tries to parse <paramref name="value"/> into a known <see cref="AgentStatus"/>.</summary>
    public static bool TryFrom(string? value, out AgentStatus? result) { if (value is not null && s_all.TryGetValue(value, out var f)) { result = f; return true; } result = null; return false; }
    /// <inheritdoc />
    public override string ToString() => Value;
    /// <summary>Implicitly converts an <see cref="AgentStatus"/> to its string value.</summary>
    public static implicit operator string(AgentStatus s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Value;
    }
}
