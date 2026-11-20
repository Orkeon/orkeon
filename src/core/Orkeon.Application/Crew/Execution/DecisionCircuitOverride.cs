namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The decision circuit of one run scope, when it is not the container's (#431): a host that runs the same crew
/// twice, once with System One and once without (the forge's trial of an <c>optional</c> crew), sets it on the
/// scope before it resolves the crew factory and the orchestrator. Unset, the scope follows the registered
/// <see cref="DecisionCircuit"/>, as every run did before.
/// </summary>
public sealed class DecisionCircuitOverride
{
    /// <summary>Gets or sets the circuit the scope's run follows; null keeps the container's.</summary>
    public DecisionCircuit? Circuit { get; set; }
}
