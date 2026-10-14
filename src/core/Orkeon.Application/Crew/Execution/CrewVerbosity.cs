using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Whether the crew run in progress is verbose (<c>crew.Verbose</c>). The orchestrator opens a scope
/// around every run and the execution log reads it per turn, so no signature carries the flag. Backed
/// by <see cref="AsyncLocal{T}"/> like <see cref="CrewPlanScope"/>: a run nested in a task sees its own
/// flag, and the enclosing one comes back when it ends.
/// </summary>
public static class CrewVerbosity
{
    private static readonly AsyncLocal<bool> Ambient = new();

    /// <summary>Opens the verbosity scope of a run.</summary>
    /// <param name="crewVerbose">Whether the run's crew is verbose.</param>
    /// <returns>The handle that restores the enclosing scope.</returns>
    public static IDisposable Begin(bool crewVerbose)
    {
        var enclosing = Ambient.Value;
        Ambient.Value = crewVerbose;
        return new Scope(enclosing);
    }

    /// <summary>True when the agent or the current run's crew is verbose.</summary>
    /// <param name="agent">The agent.</param>
    /// <returns>Whether its turn lines go under the verbose category.</returns>
    public static bool IsVerbose(DomainAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return agent.Verbose || Ambient.Value;
    }

    private sealed class Scope(bool enclosing) : IDisposable
    {
        public void Dispose() => Ambient.Value = enclosing;
    }
}
