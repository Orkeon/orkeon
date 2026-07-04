namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>Represents the crew process type (execution model).</summary>
public sealed record ProcessType
{
    /// <summary>Gets the string value of this process type.</summary>
    public string Value { get; }
    private ProcessType(string value) => Value = value;

    /// <summary>Tasks execute one after another in sequence.</summary>
    public static readonly ProcessType Sequential = new("Sequential");
    /// <summary>Tasks are managed by a hierarchical manager agent.</summary>
    public static readonly ProcessType Hierarchical = new("Hierarchical");
    /// <summary>Tasks require consensus among agents before proceeding.</summary>
    public static readonly ProcessType Consensual = new("Consensual");
    /// <summary>Tasks execute concurrently in parallel.</summary>
    public static readonly ProcessType Parallel = new("Parallel");
    /// <summary>Tasks execute through a LangGraph-style typed state graph with controlled cycles.</summary>
    public static readonly ProcessType Graph = new("Graph");
    /// <summary>Agents operate autonomously with budget-controlled delegation, self-spawn, and A2A communication.</summary>
    public static readonly ProcessType Autonomous = new("Autonomous");
    private static readonly Dictionary<string, ProcessType> s_all = new(StringComparer.OrdinalIgnoreCase)
    { [nameof(Sequential)] = Sequential, [nameof(Hierarchical)] = Hierarchical, [nameof(Consensual)] = Consensual, [nameof(Parallel)] = Parallel, [nameof(Graph)] = Graph, [nameof(Autonomous)] = Autonomous };
    /// <summary>Gets all valid process types.</summary>
    public static IReadOnlyCollection<ProcessType> All => s_all.Values;
    /// <summary>Creates a <see cref="ProcessType"/> from its string representation.</summary>
    public static ProcessType From(string value) => s_all.TryGetValue(value, out var s) ? s : throw new ArgumentException($"Unknown ProcessType: '{value}'", nameof(value));
    /// <summary>Attempts to create a <see cref="ProcessType"/> from its string representation.</summary>
    public static bool TryFrom(string? value, out ProcessType? result) { if (value is not null && s_all.TryGetValue(value, out var f)) { result = f; return true; } result = null; return false; }
    /// <summary>Returns the string representation.</summary>
    public override string ToString() => Value;
    /// <summary>Implicitly converts to string.</summary>
    public static implicit operator string(ProcessType s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Value;
    }
}
