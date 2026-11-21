namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The decision circuit of the run in progress, when its scope holds one that is not the container's
/// (<see cref="DecisionCircuitOverride"/>, #431). The orchestrator enters it for the run, so a service built
/// once per host - the LLM judge, a decision profile, the <c>decision</c> reranker, the skills pick - reads
/// it through <see cref="DecisionCircuit.Of(IServiceProvider)"/> at call time, as the scoped services read
/// the override itself.
/// </summary>
/// <remarks>
/// Backed by <see cref="AsyncLocal{T}"/>, like <see cref="CrewPlanScope"/>: the circuit follows the run's flow
/// across awaits and into the tasks it starts, and a scope opened inside an async method never leaks to its
/// caller. An off circuit wins: a scope opened inside an off one stays off.
/// </remarks>
public static class DecisionCircuitScope
{
    private static readonly AsyncLocal<DecisionCircuit?> Ambient = new();

    /// <summary>Gets the circuit of the run in progress; null outside a run whose scope holds one.</summary>
    public static DecisionCircuit? Current => Ambient.Value;

    /// <summary>Enters the circuit of a run; disposing the handle restores the enclosing one.</summary>
    /// <param name="circuit">The run scope's circuit; null leaves the enclosing one in place.</param>
    /// <returns>The handle that restores the enclosing circuit.</returns>
    public static IDisposable Begin(DecisionCircuit? circuit)
    {
        var enclosing = Ambient.Value;
        if (circuit is not null && enclosing is not { Enabled: false })
            Ambient.Value = circuit;
        return new Scope(enclosing);
    }

    private sealed class Scope(DecisionCircuit? enclosing) : IDisposable
    {
        public void Dispose() => Ambient.Value = enclosing;
    }
}
