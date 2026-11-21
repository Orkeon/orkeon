namespace Orkeon.Application.Crew.Execution;

/// <summary>Why the decision circuit is off; when several hold, the first of this order is the one named.</summary>
public enum DecisionCircuitCause
{
    /// <summary>The run's <c>--no-decision</c> switch.</summary>
    Switch,

    /// <summary>The setting <c>Orkeon:Decision:Enabled</c> is false.</summary>
    Settings,

    /// <summary>No decision provider resolved: the host registers no <see cref="Interfaces.Ports.IDecisionModel"/>.</summary>
    NoProvider,

    /// <summary>
    /// The forge's trial pass without System One: the second pass of an <c>optional</c> crew's trial, run through
    /// the relays while the host's circuit is on (#431).
    /// </summary>
    Trial,
}
