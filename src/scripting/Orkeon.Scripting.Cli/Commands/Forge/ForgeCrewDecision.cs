using Orkeon.Application.Crew.Execution;
using Orkeon.Constants.Protocol;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// The decision mode of the crew a session designs (<c>--crew-decision</c>, #431): what the plan may hold, what
/// the rendered crew declares, which tools the designer is offered and how many passes a trial runs. The session
/// records it; the forge's own decision switch (#435) is another matter and never changes it.
/// </summary>
internal static class ForgeCrewDecision
{
    /// <summary>No decision task, no <c>system_one_decide</c>, no <c>decision:</c> block: the default.</summary>
    public const string None = RunDecisionModes.None;

    /// <summary>Decision tasks, each with a <c>without:</c> relay; the crew runs with System One or without it.</summary>
    public const string Optional = RunDecisionModes.Optional;

    /// <summary>The crew needs System One: a run with the circuit off is refused.</summary>
    public const string Required = RunDecisionModes.Required;

    /// <summary>The modes, in the order the help lists them.</summary>
    public static readonly IReadOnlyList<string> All = [None, Optional, Required];

    /// <summary>
    /// The warning a legacy session's resume, or the reopen of a crew that declares no mode, emits when it derives
    /// the mode from its plan.
    /// </summary>
    public const string DerivedWarning = "FORGE-DECISION-MODE-DERIVED";

    /// <summary>The warning an optional trial emits when its on pass cannot run.</summary>
    public const string PassSkippedWarning = "FORGE-DECISION-PASS-SKIPPED";

    /// <summary>The refusal of a required crew's trial when the host's decision circuit is off.</summary>
    public const string UnavailableCode = "FORGE-DECISION-UNAVAILABLE";

    /// <summary>The finding an optional trial adds when it ran without its on pass.</summary>
    public const string PassOnSkippedFinding = "F-PASS-ON-SKIPPED";

    /// <summary>The name of the tool that asks System One from an agent.</summary>
    public const string DecideTool = SystemOneUses.DecideToolName;

    /// <summary>Whether <paramref name="mode"/> spells one of the modes, case aside.</summary>
    public static bool IsKnown(string? mode) =>
        mode is not null && All.Contains(mode.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary><paramref name="mode"/> in its canonical spelling, <see cref="None"/> when absent or unknown.</summary>
    public static string Normalize(string? mode) =>
        mode is null ? None : All.FirstOrDefault(known => string.Equals(known, mode.Trim(), StringComparison.OrdinalIgnoreCase)) ?? None;

    /// <summary>
    /// The mode of a session written before the option (#431): <see cref="Required"/> when a task of the plan
    /// declares a decision or an agent or a task holds <c>system_one_decide</c>, else <see cref="None"/>.
    /// </summary>
    public static string Derive(ForgeBlueprint? blueprint)
    {
        if (blueprint is null)
            return None;

        var decides = (blueprint.Tasks ?? []).Any(static t => t.Decision is not null || HoldsDecideTool(t.Tools))
            || (blueprint.Agents ?? []).Any(static a => HoldsDecideTool(a.Tools));
        return decides ? Required : None;
    }

    /// <summary>The crew-level block the mode renders: none for <see cref="None"/>, which 1.3.0 would refuse.</summary>
    public static CrewDecisionYamlConfig? BlockOf(string? mode) => Normalize(mode) switch
    {
        Optional => new CrewDecisionYamlConfig { Mode = Optional },
        Required => new CrewDecisionYamlConfig { Mode = Required },
        _ => null,
    };

    /// <summary>
    /// Whether the designer is offered <c>system_one_decide</c>: in a <see cref="Required"/> crew whose trial
    /// circuit is on, only. The prompt's catalogue and the validation's known tools read this same answer.
    /// </summary>
    public static bool OffersDecideTool(string? mode, DecisionCircuit circuit) =>
        Normalize(mode) == Required && circuit.Enabled;

    /// <summary>
    /// The circuits a trial runs, in order: the host's for <see cref="None"/>, on for <see cref="Required"/>, on
    /// then off by <see cref="DecisionCircuitCause.Trial"/> for <see cref="Optional"/> (the host's off circuit alone
    /// when it is off).
    /// </summary>
    public static IReadOnlyList<DecisionCircuit> PassesOf(string? mode, DecisionCircuit host) => Normalize(mode) switch
    {
        Optional when host.Enabled => [DecisionCircuit.On, DecisionCircuit.OffBy(DecisionCircuitCause.Trial)],
        Optional => [host],
        Required => [host],
        _ => [host],
    };

    /// <summary>The spelling of a circuit in the stream and the run files: <c>on</c> or <c>off</c>.</summary>
    public static string CircuitName(DecisionCircuit circuit) =>
        circuit.Enabled ? DecisionCircuitStates.On : DecisionCircuitStates.Off;

    /// <summary>The cause of an off circuit as the stream spells it (<c>switch</c>, <c>settings</c>, <c>no-provider</c>, <c>trial</c>, <c>no-key</c>).</summary>
    public static string? CauseName(DecisionCircuit circuit) => circuit.Cause switch
    {
        DecisionCircuitCause.Switch => DecisionCircuitCauses.Switch,
        DecisionCircuitCause.Settings => DecisionCircuitCauses.Settings,
        DecisionCircuitCause.NoProvider => DecisionCircuitCauses.NoProvider,
        DecisionCircuitCause.Trial => DecisionCircuitCauses.Trial,
        DecisionCircuitCause.NoKey => DecisionCircuitCauses.NoKey,
        _ => null,
    };

    /// <summary>The line the launcher's header and <c>FORGE.md</c> give the mode, null for <see cref="None"/>.</summary>
    public static string? Statement(string? mode) => Normalize(mode) switch
    {
        Required => "Decision mode: required. A run needs System One (a decision model): with the circuit off "
            + "(Orkeon:Decision:Enabled=false, --no-decision, no model) it is refused.",
        Optional => "Decision mode: optional. A launch runs with System One when a decision model is configured, "
            + "else through each decision task's relay agent; setting Orkeon:Decision:Enabled=false or adding "
            + "--no-decision forces the relay.",
        _ => null,
    };

    /// <summary><see cref="Statement"/> in the card's language: French or English; null for <see cref="None"/>.</summary>
    public static string? StatementIn(string? mode, bool french) => (Normalize(mode), french) switch
    {
        (Required, true) => "Mode de décision : required. Une exécution a besoin de System One (un modèle de décision) : "
            + "circuit coupé (Orkeon:Decision:Enabled=false, --no-decision, aucun modèle), elle est refusée.",
        (Optional, true) => "Mode de décision : optional. Un lancement passe par System One quand un modèle de décision est "
            + "configuré, sinon par l'agent relais de chaque tâche de décision ; Orkeon:Decision:Enabled=false ou "
            + "--no-decision force le relais.",
        (var other, _) => Statement(other),
    };

    private static bool HoldsDecideTool(IReadOnlyList<string>? tools) =>
        tools?.Contains(DecideTool, StringComparer.Ordinal) ?? false;
}
