using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Whether the decision circuit is on. Off, a run makes no <see cref="IDecisionModel"/> call: decision
/// tasks answer by their fallback, and every other consumer of the model (the <c>system_one_decide</c>
/// tool, the decision-profile stages, the LLM judge, the <c>decision</c> reranker) degrades the way
/// <c>docs/architecture/decision-models.md</c> lists. A consumer reads the circuit with
/// <see cref="Of(IServiceProvider)"/>, never <c>Orkeon:Decision:Enabled</c>, so <c>--no-decision</c>
/// and a run scope's <see cref="DecisionCircuitOverride"/> reach it.
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
    /// Gets what the cause alone does not say, ended into <see cref="OffReason"/>: for
    /// <see cref="DecisionCircuitCause.NoKey"/>, the provider and where its key is read
    /// (<c>no Liquid API key: set the environment variable LIQUID_API_KEY, or Orkeon:Liquid:ApiKey</c>); null otherwise.
    /// </summary>
    public string? Detail { get; init; }

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
            DecisionCircuitCause.Trial => "the decision circuit is off: the forge's trial pass without System One",
            DecisionCircuitCause.NoKey => "the decision circuit is off: " + (Detail ?? "the decision provider has no API key"),
            _ => "the decision circuit is off",
        };

    /// <summary>Builds an off circuit with its cause.</summary>
    /// <param name="cause">Why the circuit is off.</param>
    /// <returns>The off circuit.</returns>
    public static DecisionCircuit OffBy(DecisionCircuitCause cause) => new(false) { Cause = cause };

    /// <summary>
    /// The circuit a consumer of the decision model follows, the one seam they all read: off when the run in
    /// progress is off (<see cref="DecisionCircuitScope"/>, entered by the orchestrator from the scope's
    /// <see cref="DecisionCircuitOverride"/>), otherwise the container's (<see cref="OfHost(IServiceProvider)"/>).
    /// An off circuit never resolves the model. Read at call time, so a service built once per host follows
    /// each run.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The circuit the consumer follows.</returns>
    public static DecisionCircuit Of(IServiceProvider services) => Of(services, scopeCircuit: null);

    /// <summary>
    /// The circuit a consumer follows when it holds the circuit of its run scope
    /// (<see cref="DecisionCircuitOverride.Circuit"/>): the first off among that circuit, the run in progress
    /// (<see cref="DecisionCircuitScope"/>) and the container's (<see cref="OfHost(IServiceProvider)"/>);
    /// on when none is off. A scope can turn the circuit off, never on over an off container.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="scopeCircuit">The circuit of the run's scope; null when it holds none.</param>
    /// <returns>The circuit the consumer follows.</returns>
    public static DecisionCircuit Of(IServiceProvider services, DecisionCircuit? scopeCircuit)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (scopeCircuit is { Enabled: false })
            return scopeCircuit;
        if (DecisionCircuitScope.Current is { Enabled: false } running)
            return running;
        return OfHost(services);
    }

    /// <summary>
    /// The container's own circuit, whatever run is in progress: the registered <see cref="DecisionCircuit"/>
    /// when it is off; otherwise off by <see cref="DecisionCircuitCause.NoProvider"/> when no
    /// <see cref="IDecisionModel"/> is registered, and on when one is. What a service built once per host is
    /// built with; it reads <see cref="Of(IServiceProvider)"/> again at call time.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <returns>The container's circuit.</returns>
    public static DecisionCircuit OfHost(IServiceProvider services)
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
