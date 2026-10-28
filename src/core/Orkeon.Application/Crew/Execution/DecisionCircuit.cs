namespace Orkeon.Application.Crew.Execution;

/// <summary>Whether the decision circuit is on: off, decision tasks answer by their fallback.</summary>
/// <param name="Enabled">True when the decision model is asked.</param>
public sealed record DecisionCircuit(bool Enabled)
{
    /// <summary>Gets the circuit that asks the decision model.</summary>
    public static DecisionCircuit On { get; } = new(true);

    /// <summary>Gets the circuit that answers by the fallback, with no request.</summary>
    public static DecisionCircuit Off { get; } = new(false);
}
