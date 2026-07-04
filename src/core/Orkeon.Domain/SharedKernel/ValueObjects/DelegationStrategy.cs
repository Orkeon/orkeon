using System.Collections.Immutable;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Value object representing a delegation strategy.
/// </summary>
public record DelegationStrategy : ValueObjectRecord
{
    /// <summary>Gets the strategy name.</summary>
    public string Name { get; }
    /// <summary>Gets the delegation type.</summary>
    public DelegationType Type { get; }
    /// <summary>Gets the delegation parameters.</summary>
    public DelegationParameters Parameters { get; }
    /// <summary>Gets the priority weight for this strategy.</summary>
    public double Priority { get; }
    /// <summary>Gets the list of constraints applied to this strategy.</summary>
    public ImmutableList<string> Constraints { get; }

    /// <summary>Initializes a new <see cref="DelegationStrategy"/>.</summary>
    /// <param name="name">The strategy name.</param>
    /// <param name="type">The delegation type.</param>
    /// <param name="parameters">Optional delegation parameters.</param>
    /// <param name="priority">The priority weight (default 1.0).</param>
    /// <param name="constraints">Optional list of constraints.</param>
    private DelegationStrategy(
        string name,
        DelegationType type,
        DelegationParameters? parameters = null,
        double priority = 1.0,
        IEnumerable<string>? constraints = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Type = type;
        Parameters = parameters ?? DelegationParameters.Empty;
        Priority = priority;
        Constraints = constraints?.ToImmutableList() ?? [];
    }

    /// <summary>Creates a new <see cref="DelegationStrategy"/> with the given parameters.</summary>
    /// <param name="name">The strategy name.</param>
    /// <param name="type">The delegation type.</param>
    /// <param name="parameters">Optional delegation parameters.</param>
    /// <param name="priority">The priority weight (default 1.0).</param>
    /// <param name="constraints">Optional list of constraints.</param>
    /// <returns>A new <see cref="DelegationStrategy"/> instance.</returns>
    public static DelegationStrategy Create(
        string name,
        DelegationType type,
        DelegationParameters? parameters = null,
        double priority = 1.0,
        IEnumerable<string>? constraints = null)
        => new(name, type, parameters, priority, constraints);

    /// <summary>Creates a skill-based delegation strategy.</summary>
    /// <param name="minSkillMatch">The minimum skill match ratio (default 0.8).</param>
    /// <returns>A new skill-based <see cref="DelegationStrategy"/>.</returns>
    public static DelegationStrategy SkillBased(double minSkillMatch = 0.8)
    {
        return new DelegationStrategy(
            "SkillBased",
            DelegationType.SkillBased,
            DelegationParameters.ForSkillBased(minSkillMatch)
        );
    }

    /// <summary>Creates a capability-based delegation strategy (alias for <see cref="SkillBased"/>).</summary>
    /// <param name="minSkillMatch">The minimum skill match ratio (default 0.8).</param>
    /// <returns>A new capability-based <see cref="DelegationStrategy"/>.</returns>
    public static DelegationStrategy CapabilityBased(double minSkillMatch = 0.8)
    {
        // Alias for SkillBased to maintain backward compatibility
        return SkillBased(minSkillMatch);
    }

    /// <summary>Creates a workload-based delegation strategy.</summary>
    /// <param name="maxWorkload">The maximum concurrent task count (default 5).</param>
    /// <returns>A new workload-based <see cref="DelegationStrategy"/>.</returns>
    public static DelegationStrategy WorkloadBased(int maxWorkload = 5)
    {
        return new DelegationStrategy(
            "WorkloadBased",
            DelegationType.WorkloadBased,
            DelegationParameters.ForWorkloadBased(maxWorkload)
        );
    }

    /// <summary>Creates a round-robin delegation strategy.</summary>
    /// <returns>A new round-robin <see cref="DelegationStrategy"/>.</returns>
    public static DelegationStrategy RoundRobin()
    {
        return new DelegationStrategy(
            "RoundRobin",
            DelegationType.RoundRobin
        );
    }

    /// <summary>Creates a hierarchical delegation strategy.</summary>
    /// <param name="managerRole">The manager role name.</param>
    /// <returns>A new hierarchical <see cref="DelegationStrategy"/>.</returns>
    public static DelegationStrategy Hierarchical(string managerRole)
    {
        return new DelegationStrategy(
            "Hierarchical",
            DelegationType.Hierarchical,
            DelegationParameters.ForHierarchical(managerRole)
        );
    }

    /// <summary>Creates a custom delegation strategy.</summary>
    /// <param name="name">The strategy name.</param>
    /// <param name="parameters">The custom delegation parameters.</param>
    /// <returns>A new custom <see cref="DelegationStrategy"/>.</returns>
    public static DelegationStrategy Custom(string name, DelegationParameters parameters)
    {
        return new DelegationStrategy(
            name,
            DelegationType.Custom,
            parameters
        );
    }

    /// <inheritdoc />
    public virtual bool Equals(DelegationStrategy? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Name == other.Name &&
               Type == other.Type &&
               Parameters == other.Parameters &&
               Priority == other.Priority &&
               Constraints.SequenceEqual(other.Constraints);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Type);
        hash.Add(Parameters);
        hash.Add(Priority);
        hash.Add(Constraints.Count);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Types of delegation strategies.
/// </summary>
public sealed record DelegationType
{
    /// <summary>Gets the string value of this delegation type.</summary>
    public string Value { get; }
    private DelegationType(string value) => Value = value;

    /// <summary>Delegates based on agent skill match.</summary>
    public static readonly DelegationType SkillBased = new("SkillBased");
    /// <summary>Delegates based on current agent workload.</summary>
    public static readonly DelegationType WorkloadBased = new("WorkloadBased");
    /// <summary>Delegates in round-robin order across agents.</summary>
    public static readonly DelegationType RoundRobin = new("RoundRobin");
    /// <summary>Delegates through a hierarchical manager agent.</summary>
    public static readonly DelegationType Hierarchical = new("Hierarchical");
    /// <summary>Custom delegation using user-defined logic.</summary>
    public static readonly DelegationType Custom = new("Custom");

    private static readonly Dictionary<string, DelegationType> s_all = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(SkillBased)] = SkillBased,
        [nameof(WorkloadBased)] = WorkloadBased,
        [nameof(RoundRobin)] = RoundRobin,
        [nameof(Hierarchical)] = Hierarchical,
        [nameof(Custom)] = Custom,
    };

    /// <summary>Gets all valid delegation types.</summary>
    public static IReadOnlyCollection<DelegationType> All => s_all.Values;

    /// <summary>Creates a <see cref="DelegationType"/> from its string representation.</summary>
    public static DelegationType From(string value) =>
        s_all.TryGetValue(value, out var s)
            ? s
            : throw new ArgumentException($"Unknown DelegationType: '{value}'", nameof(value));

    /// <summary>Attempts to create a <see cref="DelegationType"/> from its string representation.</summary>
    public static bool TryFrom(string? value, out DelegationType? result)
    {
        if (value is not null && s_all.TryGetValue(value, out var f)) { result = f; return true; }
        result = null; return false;
    }

    /// <summary>Returns the string representation.</summary>
    public override string ToString() => Value;
    /// <summary>Implicitly converts to string.</summary>
    public static implicit operator string(DelegationType s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Value;
    }
}
