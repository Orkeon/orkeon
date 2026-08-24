using Orkeon.Scripting.Cli.Events;
using Orkeon.Domain.Common.StateMachine;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>What one stage runner tells the engine to do next.</summary>
internal sealed record ForgeStageOutcome
{
    /// <summary>The trigger to fire; the machine says whether it is legal.</summary>
    public required ForgeTrigger Trigger { get; init; }

    /// <summary>LLM tokens the stage consumed, charged to the session budget.</summary>
    public long TokensConsumed { get; init; }

    /// <summary>Carried into the failure message when <see cref="Trigger"/> is <see cref="ForgeTrigger.Fail"/>.</summary>
    public string? Detail { get; init; }

    /// <summary>
    /// The public error code of a <see cref="ForgeTrigger.Fail"/> outcome (e.g.
    /// <c>FORGE-VALIDATION-FAILED</c>); the engine falls back to its generic
    /// <c>FORGE-STAGE-FAILED</c> when a stage does not name one.
    /// </summary>
    public string? FailureCode { get; init; }
}

/// <summary>
/// One stage of the cycle. The engine owns the transitions, the budget and the persistence;
/// a runner owns exactly one stage's work and reports the trigger that concludes it. The
/// slices F3–F7 provide the real runners; the engine never knows which build it is in.
/// </summary>
internal interface IForgeStageRunner
{
    /// <summary>The stage this runner implements.</summary>
    ForgeState Stage { get; }

    /// <summary>Does the stage's work and names the trigger that concludes it.</summary>
    Task<ForgeStageOutcome> RunAsync(ForgeSession session, ForgeEventWriter events, CancellationToken cancellationToken);
}

/// <summary>How a run of the engine ended.</summary>
internal enum ForgeEngineOutcome
{
    /// <summary>The run stopped at a deliberate boundary (<c>--dry</c>); the session resumes from there.</summary>
    Paused,

    /// <summary>A conforming crew is waiting to be promoted.</summary>
    Ready,

    /// <summary>The session was promoted (a Ready-stage runner was present and ran).</summary>
    Promoted,

    /// <summary>The user stopped the cycle.</summary>
    Abandoned,

    /// <summary>A budget dimension ran out; the session is resumable with a raised budget.</summary>
    BudgetExhausted,

    /// <summary>An unrecoverable error; the session records why.</summary>
    Failed,
}

/// <summary>The engine's verdict, with the CLI exit code it maps to.</summary>
internal sealed record ForgeEngineResult(ForgeEngineOutcome Outcome, int ExitCode);

/// <summary>
/// The forge cycle's deterministic spine (SPEC-ORKEON-FORGE §3.3): a loop over the state
/// machine that runs one stage at a time, charges the budget, appends the transition
/// history, saves the session after every step, and emits the event protocol. Nothing
/// here calls an LLM — that is the runners' business, and only through the triggers they
/// return can they influence the cycle.
/// </summary>
internal sealed class ForgeEngine
{
    /// <summary>A stage has no runner in this build — an engine invariant, not a user error.</summary>
    public const string CodeEngineIncomplete = "FORGE-ENGINE-INCOMPLETE";

    /// <summary>A runner returned a trigger the machine refuses from the current stage.</summary>
    public const string CodeInvalidTransition = "FORGE-ENGINE-INVALID-TRANSITION";

    /// <summary>A stage reported an unrecoverable error of its own.</summary>
    public const string CodeStageFailed = "FORGE-STAGE-FAILED";

    /// <summary>A budget dimension ran out (SPEC §4.2): hard stop, session resumable.</summary>
    public const string CodeBudgetExhausted = "FORGE-BUDGET-EXHAUSTED";

    private readonly ForgeSession _session;
    private readonly ForgeEventWriter _events;
    private readonly Dictionary<ForgeState, IForgeStageRunner> _runners;
    private readonly IOrkeonClock _clock;

    /// <summary>Builds the engine over a session and the runners this build carries.</summary>
    public ForgeEngine(
        ForgeSession session,
        ForgeEventWriter events,
        IEnumerable<IForgeStageRunner> runners,
        IOrkeonClock? clock = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _runners = (runners ?? throw new ArgumentNullException(nameof(runners))).ToDictionary(r => r.Stage);
        _clock = clock ?? SystemOrkeonClock.Instance;
    }

    /// <summary>
    /// Runs the cycle from the session's saved state until a stop: Ready with no promotion
    /// runner, a terminal state, an exhausted budget — or <paramref name="stopBefore"/>,
    /// the deliberate boundary of <c>--dry</c> (generate and validate, never execute):
    /// the session pauses there, resumable, and the run exits 0. Cancellation saves the
    /// session first and then propagates — the CLI's 130 contract is the caller's business.
    /// </summary>
    public async Task<ForgeEngineResult> RunAsync(
        bool resumed = false,
        ForgeState? stopBefore = null,
        CancellationToken cancellationToken = default)
    {
        var machine = ForgeStateMachineFactory.Create(_session.State);
        var budget = _session.Document.Budget;

        // The first pass is a cycle too: charge it once, resume included.
        if (budget.ConsumedIterations == 0)
            budget.RegisterIteration();
        _session.Document.Iteration = budget.ConsumedIterations;

        _events.SessionStarted(_session, resumed);

        // Wall time is charged as an absolute delta from the run's start on top of what
        // earlier runs consumed: per-checkpoint deltas would truncate sub-second stages
        // to zero and undercount a whole session one stage at a time.
        var runStarted = _clock.UtcNow;
        var wallBase = budget.ConsumedWallSeconds;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var state = machine.CurrentState;

                if (machine.IsTerminal)
                    return Finish(state);

                if (state == stopBefore)
                {
                    // The deliberate boundary: everything before it ran and was saved; the
                    // session waits exactly here for a resume without --dry.
                    Checkpoint();
                    _events.SessionFinished("paused", 0);
                    return new ForgeEngineResult(ForgeEngineOutcome.Paused, 0);
                }

                if (budget.ExhaustedDimension() is { } dimension)
                    return FinishBudgetExhausted(dimension);

                if (!_runners.TryGetValue(state, out var runner))
                {
                    // Ready without a promotion runner is the ordinary stop of a cycle:
                    // the crew waits; `forge promote` (or the client) takes over.
                    if (state == ForgeState.Ready)
                    {
                        _session.SetStatus(ForgeSessionStatus.Ready);
                        Checkpoint();
                        _events.SessionFinished("ready", 0);
                        return new ForgeEngineResult(ForgeEngineOutcome.Ready, 0);
                    }

                    return Fail(CodeEngineIncomplete,
                        $"No runner for stage '{ForgeEventWriter.Spell(state)}' in this build.");
                }

                _events.StageEntered(state, _session.Document.Iteration);

                var outcome = await runner.RunAsync(_session, _events, cancellationToken).ConfigureAwait(false);
                budget.RegisterTokens(outcome.TokensConsumed);

                // The cost lives (UX study §7): every stage that spent tokens tells the
                // client where the meter stands — cumulative, with the remaining allowance
                // when one is set. USD is the client's business (it knows the pricing).
                if (outcome.TokensConsumed > 0)
                {
                    _events.Emit("cost.updated", new
                    {
                        tokens = budget.ConsumedTokens,
                        budgetRemaining = budget.MaxTokens > 0
                            ? Math.Max(0, budget.MaxTokens - budget.ConsumedTokens)
                            : (long?)null,
                    });
                }

                // Looping back is what iterations meter: the budget arbitrates before the
                // machine moves, so a refused cycle costs nothing and the session stays
                // exactly where it was — resumable with a raised budget. A user edit loops
                // back too — no LLM turn, but its re-render/re-test is a cycle and its run
                // needs its own number, or runs/N would be silently overwritten.
                if (outcome.Trigger is ForgeTrigger.RepairNeeded or ForgeTrigger.RefineRequested or ForgeTrigger.BlueprintEdited)
                {
                    if (!budget.CanStartIteration)
                        return FinishBudgetExhausted(ForgeBudgetDimension.Iterations);

                    budget.RegisterIteration();
                    _session.Document.Iteration = budget.ConsumedIterations;
                }

                if (outcome.Trigger == ForgeTrigger.Fail)
                {
                    return Fail(outcome.FailureCode ?? CodeStageFailed,
                        outcome.Detail ?? "The stage reported an unrecoverable error.");
                }

                if (!machine.TryFire(outcome.Trigger, out _))
                {
                    return Fail(CodeInvalidTransition,
                        $"'{outcome.Trigger}' is not a legal move from '{ForgeEventWriter.Spell(state)}'.");
                }

                _session.AppendHistory(state, outcome.Trigger, machine.CurrentState, _clock.UtcNow);
                _session.SetState(machine.CurrentState);
                Checkpoint();
            }
        }
        catch (OperationCanceledException)
        {
            // Interrupted, not finished: the session stays Active and resumable; the
            // caller owns the 130 exit contract.
            Checkpoint();
            throw;
        }

        ForgeEngineResult Finish(ForgeState state)
        {
            var (status, wireStatus, outcome, exitCode) = state switch
            {
                ForgeState.Promoted => (ForgeSessionStatus.Promoted, "ready", ForgeEngineOutcome.Promoted, 0),
                ForgeState.Abandoned => (ForgeSessionStatus.Abandoned, "abandoned", ForgeEngineOutcome.Abandoned, 0),
                _ => (ForgeSessionStatus.Failed, "failed", ForgeEngineOutcome.Failed, 2),
            };

            _session.SetStatus(status);
            Checkpoint();
            _events.SessionFinished(wireStatus, exitCode);
            return new ForgeEngineResult(outcome, exitCode);
        }

        ForgeEngineResult FinishBudgetExhausted(ForgeBudgetDimension dimension)
        {
            _session.SetStatus(ForgeSessionStatus.BudgetExhausted);
            Checkpoint();
            _events.Error(CodeBudgetExhausted,
                $"The session's {Spell(dimension)} budget is exhausted; resume with a raised budget to continue.",
                recoverable: true);
            _events.SessionFinished("abandoned", 0);
            return new ForgeEngineResult(ForgeEngineOutcome.BudgetExhausted, 0);
        }

        ForgeEngineResult Fail(string code, string message)
        {
            machine.TryFire(ForgeTrigger.Fail, out _);
            _session.SetState(ForgeState.Failed);
            _session.SetStatus(ForgeSessionStatus.Failed);
            _session.Document.Error = $"{code}: {message}";
            Checkpoint();
            _events.Error(code, message, recoverable: false);
            _events.SessionFinished("failed", 2);
            return new ForgeEngineResult(ForgeEngineOutcome.Failed, 2);
        }

        // Charges wall time and saves — after every step, so a kill loses one stage at most.
        void Checkpoint()
        {
            var now = _clock.UtcNow;
            budget.ConsumedWallSeconds = wallBase + (long)(now - runStarted).TotalSeconds;
            _session.Save(now);
        }
    }

    private static string Spell(ForgeBudgetDimension dimension) => dimension switch
    {
        ForgeBudgetDimension.Iterations => "iteration",
        ForgeBudgetDimension.Tokens => "token",
        _ => "wall-time",
    };
}
