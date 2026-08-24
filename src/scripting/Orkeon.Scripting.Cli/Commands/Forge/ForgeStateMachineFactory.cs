using Orkeon.Domain.Common.StateMachine;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// Builds the forge cycle's state machine (SPEC-ORKEON-FORGE §4) over the Domain
/// <see cref="StateMachine{TState, TEvent}"/>. The machine is the map of legal moves and
/// nothing else: budget checks live in the engine (<see cref="ForgeBudget"/> is the real
/// bound), and stage work lives in the runners.
/// </summary>
internal static class ForgeStateMachineFactory
{
    /// <summary>
    /// The circuit-breaker policy of the forge machine. The Domain defaults assume an
    /// autonomous machine (5 minutes per state, 30 minutes total) — a human interview
    /// idles far longer than that, so every time-based limit is disabled and only the
    /// transition backstop remains, set far above what a bounded budget can produce.
    /// </summary>
    private static readonly CircuitBreakerPolicy Policy = new()
    {
        MaxTransitions = 200,
        StateTimeout = TimeSpan.Zero,
        MaxStateVisits = 0,
        MaxTotalDuration = TimeSpan.Zero,
    };

    /// <summary>All states an unfinished session may be resumed in.</summary>
    private static readonly ForgeState[] ActiveStates =
    [
        ForgeState.Brief,
        ForgeState.Blueprint,
        ForgeState.Render,
        ForgeState.Validate,
        ForgeState.Test,
        ForgeState.Diagnose,
        ForgeState.Verdict,
        ForgeState.Ready,
    ];

    /// <summary>
    /// Builds the machine starting at <paramref name="initialState"/> — the saved state of
    /// a resumed session, or <see cref="ForgeState.Brief"/> for a fresh one.
    /// </summary>
    public static StateMachine<ForgeState, ForgeTrigger> Create(ForgeState initialState = ForgeState.Brief)
    {
        var builder = new StateMachineBuilder<ForgeState, ForgeTrigger>()
            .WithInitialState(initialState)
            .WithTerminalStates(ForgeState.Promoted, ForgeState.Abandoned, ForgeState.Failed)
            .WithCircuitBreaker(Policy)

            // The nominal cycle.
            .AddTransition(ForgeState.Brief, ForgeTrigger.BriefSubmitted, ForgeState.Blueprint)
            .AddTransition(ForgeState.Blueprint, ForgeTrigger.BlueprintSubmitted, ForgeState.Render)
            .AddTransition(ForgeState.Render, ForgeTrigger.Rendered, ForgeState.Validate)
            .AddTransition(ForgeState.Validate, ForgeTrigger.Validated, ForgeState.Test)
            .AddTransition(ForgeState.Test, ForgeTrigger.TestCompleted, ForgeState.Diagnose)
            .AddTransition(ForgeState.Diagnose, ForgeTrigger.Diagnosed, ForgeState.Verdict)
            .AddTransition(ForgeState.Verdict, ForgeTrigger.Accepted, ForgeState.Ready)
            .AddTransition(ForgeState.Ready, ForgeTrigger.Promote, ForgeState.Promoted)

            // The two ways back to the blueprint: a failed validation being repaired,
            // and a non-conforming verdict being refined. Refine is a transition, not a
            // state — the milestone view shows it as a return to "Proposer".
            .AddTransition(ForgeState.Validate, ForgeTrigger.RepairNeeded, ForgeState.Blueprint)
            .AddTransition(ForgeState.Verdict, ForgeTrigger.RefineRequested, ForgeState.Blueprint)

            // The user's own hand: an amended blueprint goes straight back to Render —
            // deterministic, zero LLM tokens — and re-earns its verdict through the
            // unchanged Validate/Test/Diagnose path.
            .AddTransition(ForgeState.Verdict, ForgeTrigger.BlueprintEdited, ForgeState.Render)

            // The user may stop at the arbitration point.
            .AddTransition(ForgeState.Verdict, ForgeTrigger.Abandon, ForgeState.Abandoned);

        // Any active state can fail hard; the session records why.
        foreach (var state in ActiveStates)
            builder.AddTransition(state, ForgeTrigger.Fail, ForgeState.Failed);

        return builder.Build();
    }
}
