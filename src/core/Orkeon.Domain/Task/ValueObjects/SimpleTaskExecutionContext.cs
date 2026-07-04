using Orkeon.Domain.Agent;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>
/// Simple context for task execution containing variables, outputs, and memory.
/// </summary>
public sealed class SimpleTaskExecutionContext : IEquatable<SimpleTaskExecutionContext>
{
    /// <summary>Gets the execution variables.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; }
    /// <summary>Gets the previous task outputs.</summary>
    public IReadOnlyList<TaskOutput> PreviousOutputs { get; }
    /// <summary>Gets the agent memory, or <see langword="null"/> if none.</summary>
    public AgentMemory? Memory { get; }
    /// <summary>Gets the available agents, or <see langword="null"/> if none.</summary>
    public IReadOnlyList<DomainAgent?>? AvailableAgents { get; }

    private SimpleTaskExecutionContext(
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyList<TaskOutput> previousOutputs,
        AgentMemory? memory,
        IReadOnlyList<DomainAgent?>? availableAgents)
    {
        Variables = variables;
        PreviousOutputs = previousOutputs;
        Memory = memory;
        AvailableAgents = availableAgents;
    }

    /// <summary>
    /// Creates a new <see cref="SimpleTaskExecutionContext"/> instance.
    /// </summary>
    /// <param name="variables">Execution variables.</param>
    /// <param name="previousOutputs">Previous task outputs.</param>
    /// <param name="memory">The agent memory.</param>
    /// <param name="availableAgents">Available agents.</param>
    /// <returns>A new <see cref="SimpleTaskExecutionContext"/> instance.</returns>
    public static SimpleTaskExecutionContext Create(
        Dictionary<string, string> variables,
        IReadOnlyList<TaskOutput> previousOutputs,
        AgentMemory? memory,
        IReadOnlyList<DomainAgent?>? availableAgents = null)
    {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(previousOutputs);

        return new SimpleTaskExecutionContext(
            new Dictionary<string, string>(variables).AsReadOnly(),
            [.. previousOutputs],
            memory,
            availableAgents is null ? null : [.. availableAgents]);
    }

    /// <inheritdoc />
    public bool Equals(SimpleTaskExecutionContext? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Variables.Count == other.Variables.Count
            && Variables.All(kvp => other.Variables.TryGetValue(kvp.Key, out var v) && v == kvp.Value)
            && PreviousOutputs.SequenceEqual(other.PreviousOutputs)
            && Equals(Memory, other.Memory)
            && AgentsEqual(AvailableAgents, other.AvailableAgents);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SimpleTaskExecutionContext);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var kvp in Variables.OrderBy(k => k.Key))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value);
        }
        foreach (var output in PreviousOutputs)
        {
            hash.Add(output);
        }
        hash.Add(Memory);
        return hash.ToHashCode();
    }

    private static bool AgentsEqual(IReadOnlyList<DomainAgent?>? a, IReadOnlyList<DomainAgent?>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return a is null && b is null;
        return a.SequenceEqual(b);
    }
}

// TaskOutput is defined in Orkeon.Domain.Task.ValueObjects
