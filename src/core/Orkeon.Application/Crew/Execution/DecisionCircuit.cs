using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Whether the decision circuit is on. Off, a run makes no <see cref="IDecisionModel"/> call: decision
/// tasks answer by their fallback, and every other consumer of the model (the <c>system_one_decide</c>
/// tool, the decision-profile stages, the LLM judge, the <c>decision</c> reranker) degrades the way
/// <c>docs/architecture/decision-models.md</c> lists. A consumer reads the circuit with
/// <see cref="Of(IServiceProvider)"/>, never <c>Orkeon:Decision:Enabled</c>, so <c>--no-decision</c>
/// reaches it.
/// </summary>
/// <param name="Enabled">True when the decision model is asked.</param>
public sealed record DecisionCircuit(bool Enabled)
{
    /// <summary>Gets the circuit that asks the decision model.</summary>
    public static DecisionCircuit On { get; } = new(true);

    /// <summary>Gets the circuit the <c>--no-decision</c> switch registers: off, by <see cref="DecisionCircuitCause.Switch"/>.</summary>
    public static DecisionCircuit Off { get; } = OffBy(DecisionCircuitCause.Switch);

    /// <summary>Gets why the circuit is off; null when it is on, or when an off circuit was built without a cause.</summary>
    public DecisionCircuitCause? Cause { get; init; }

    /// <summary>
    /// Gets the sentence a degraded consumer says, <c>the decision circuit is off: &lt;cause&gt;</c>; null when
    /// the circuit is on.
    /// </summary>
    public string? OffReason => Enabled
        ? null
        : Cause switch
        {
            DecisionCircuitCause.Switch => "the decision circuit is off: --no-decision",
            DecisionCircuitCause.Settings => "the decision circuit is off: Orkeon:Decision:Enabled=false",
            DecisionCircuitCause.NoProvider => "the decision circuit is off: no decision provider resolved",
            _ => "the decision circuit is off",
        };

    /// <summary>Builds an off circuit with its cause.</summary>
    /// <param name="cause">Why the circuit is off.</param>
    /// <returns>The off circuit.</returns>
    public static DecisionCircuit OffBy(DecisionCircuitCause cause) => new(false) { Cause = cause };

    /// <summary>
    /// The circuit of a container, the one seam every consumer of the decision model reads: the
    /// registered <see cref="DecisionCircuit"/> when it is off; otherwise off by
    /// <see cref="DecisionCircuitCause.NoProvider"/> when no <see cref="IDecisionModel"/> is registered,
    /// and on when one is. An off circuit never resolves the model.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The circuit the consumer follows.</returns>
    public static DecisionCircuit Of(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var registered = services.GetService<DecisionCircuit>();
        if (registered is { Enabled: false })
            return registered;
        return services.GetService<IDecisionModel>() is null
            ? OffBy(DecisionCircuitCause.NoProvider)
            : registered ?? On;
    }
}
