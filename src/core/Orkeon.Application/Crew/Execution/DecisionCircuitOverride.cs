namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The decision circuit of one run scope, when it is not the container's (#431): a host that runs the same crew
/// twice, once with System One and once without (the forge's trial of an <c>optional</c> crew), sets it on the
/// scope before it resolves the crew factory and the orchestrator. Unset, the scope follows the registered
/// <see cref="DecisionCircuit"/>, as every run did before.
/// <para>Every consumer follows it (#433): the scoped ones read it through
/// <see cref="DecisionCircuit.Of(System.IServiceProvider, DecisionCircuit?)"/>, and the crew orchestrator
/// enters it for the run as <see cref="DecisionCircuitScope"/>, which the consumers built once per host read at
/// each call. An off override wins; an on one never switches on a circuit the container has off.</para>
/// </summary>
public sealed class DecisionCircuitOverride
{
    /// <summary>Gets or sets the circuit the scope's run follows; null keeps the container's.</summary>
    public DecisionCircuit? Circuit { get; set; }
}
