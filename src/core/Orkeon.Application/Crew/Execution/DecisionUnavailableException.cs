using Orkeon.Domain.Crew;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// A crew with <c>decision.mode: required</c> refused before its first task because the decision circuit is
/// off (#429): <c>--no-decision</c>, <c>Orkeon:Decision:Enabled=false</c>, or no decision provider resolved.
/// The run fails with the <c>decision_unavailable</c> error code, and <c>orkeon run</c> exits
/// <c>RunExitCodes.DecisionUnavailable</c> (69).
/// </summary>
#pragma warning disable S3925
public sealed class DecisionUnavailableException : InvalidOperationException
#pragma warning restore S3925
{
    /// <summary>Initializes the refusal of crew <paramref name="crewName"/> under <paramref name="circuit"/>.</summary>
    /// <param name="crewName">The crew's name.</param>
    /// <param name="circuit">The decision circuit, off.</param>
    public DecisionUnavailableException(string crewName, DecisionCircuit circuit)
        : base(MessageFor(crewName, circuit))
    {
        ArgumentNullException.ThrowIfNull(circuit);
        CrewName = crewName;
        Cause = circuit.Cause;
    }

    /// <summary>Initializes a refusal with no crew named.</summary>
    public DecisionUnavailableException()
        : base("The crew requires System One (decision.mode: required) and the decision circuit is off.")
    {
        CrewName = string.Empty;
    }

    /// <summary>Initializes a refusal with its message.</summary>
    /// <param name="message">The message.</param>
    public DecisionUnavailableException(string message)
        : base(message)
    {
        CrewName = string.Empty;
    }

    /// <summary>Initializes a refusal with its message and inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public DecisionUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
        CrewName = string.Empty;
    }

    /// <summary>Gets the name of the crew refused.</summary>
    public string CrewName { get; }

    /// <summary>Gets why the circuit is off; null when unknown.</summary>
    public DecisionCircuitCause? Cause { get; }

    /// <summary>
    /// The refusal of a <see cref="CrewDecisionMode.Required"/> crew when <paramref name="circuit"/> is off, or
    /// null when it may start: the check every process makes before its first task (#429).
    /// </summary>
    /// <param name="crew">The crew about to run.</param>
    /// <param name="circuit">The run's decision circuit; null when the host registered none.</param>
    /// <returns>The refusal to throw, or null.</returns>
    public static DecisionUnavailableException? Refusing(Orkeon.Domain.Crew.Crew crew, DecisionCircuit? circuit)
    {
        ArgumentNullException.ThrowIfNull(crew);
        return crew.DecisionMode == CrewDecisionMode.Required && circuit is { Enabled: false }
            ? new DecisionUnavailableException(crew.Name ?? crew.Id.ToString(), circuit)
            : null;
    }

    private static string MessageFor(string crewName, DecisionCircuit circuit)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        return $"crew '{crewName}' requires System One (decision.mode: required) and {circuit.OffReason ?? "the decision circuit is off"}";
    }
}
