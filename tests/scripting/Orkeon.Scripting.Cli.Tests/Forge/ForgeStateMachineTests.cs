using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// The map of legal moves (SPEC-ORKEON-FORGE §4): the nominal cycle, the two ways back to
/// the blueprint, the terminal states, and a breaker tuned for a machine that waits on a
/// human — refine loops must never trip it.
/// </summary>
public class ForgeStateMachineTests
{
    [Fact]
    public void The_nominal_cycle_runs_from_brief_to_promoted()
    {
        var machine = ForgeStateMachineFactory.Create();

        Assert.Equal(ForgeState.Brief, machine.CurrentState);
        Assert.True(machine.TryFire(ForgeTrigger.BriefSubmitted, out _));
        Assert.True(machine.TryFire(ForgeTrigger.BlueprintSubmitted, out _));
        Assert.True(machine.TryFire(ForgeTrigger.Rendered, out _));
        Assert.True(machine.TryFire(ForgeTrigger.Validated, out _));
        Assert.True(machine.TryFire(ForgeTrigger.TestCompleted, out _));
        Assert.True(machine.TryFire(ForgeTrigger.Diagnosed, out _));
        Assert.True(machine.TryFire(ForgeTrigger.Accepted, out _));
        Assert.Equal(ForgeState.Ready, machine.CurrentState);
        Assert.True(machine.TryFire(ForgeTrigger.Promote, out _));

        Assert.Equal(ForgeState.Promoted, machine.CurrentState);
        Assert.True(machine.IsTerminal);
    }

    [Fact]
    public void An_illegal_move_is_refused_without_changing_the_state()
    {
        var machine = ForgeStateMachineFactory.Create();

        Assert.False(machine.TryFire(ForgeTrigger.Accepted, out _));
        Assert.Equal(ForgeState.Brief, machine.CurrentState);
    }

    [Fact]
    public void A_failed_validation_goes_back_through_the_blueprint()
    {
        var machine = ForgeStateMachineFactory.Create(ForgeState.Validate);

        Assert.True(machine.TryFire(ForgeTrigger.RepairNeeded, out _));
        Assert.Equal(ForgeState.Blueprint, machine.CurrentState);
    }

    [Fact]
    public void A_non_conforming_verdict_can_refine_or_abandon()
    {
        var refine = ForgeStateMachineFactory.Create(ForgeState.Verdict);
        Assert.True(refine.TryFire(ForgeTrigger.RefineRequested, out _));
        Assert.Equal(ForgeState.Blueprint, refine.CurrentState);

        var abandon = ForgeStateMachineFactory.Create(ForgeState.Verdict);
        Assert.True(abandon.TryFire(ForgeTrigger.Abandon, out _));
        Assert.Equal(ForgeState.Abandoned, abandon.CurrentState);
        Assert.True(abandon.IsTerminal);
    }

    [Fact]
    public void A_retry_goes_straight_back_to_the_test()
    {
        // W-09: «Refaire un essai» — same blueprint, same render, a fresh run.
        var machine = ForgeStateMachineFactory.Create(ForgeState.Verdict);

        Assert.True(machine.TryFire(ForgeTrigger.RetryRequested, out _));
        Assert.Equal(ForgeState.Test, machine.CurrentState);
    }

    [Fact]
    public void A_resumed_machine_starts_at_the_saved_state()
    {
        var machine = ForgeStateMachineFactory.Create(ForgeState.Test);

        Assert.Equal(ForgeState.Test, machine.CurrentState);
        Assert.True(machine.TryFire(ForgeTrigger.TestCompleted, out _));
        Assert.Equal(ForgeState.Diagnose, machine.CurrentState);
    }

    [Fact]
    public void Every_active_state_can_fail_hard()
    {
        foreach (var state in new[]
        {
            ForgeState.Brief, ForgeState.Blueprint, ForgeState.Render, ForgeState.Validate,
            ForgeState.Test, ForgeState.Diagnose, ForgeState.Verdict, ForgeState.Ready,
        })
        {
            var machine = ForgeStateMachineFactory.Create(state);
            Assert.True(machine.TryFire(ForgeTrigger.Fail, out _));
            Assert.Equal(ForgeState.Failed, machine.CurrentState);
            Assert.True(machine.IsTerminal);
        }
    }

    [Fact]
    public void Refine_loops_do_not_trip_the_circuit_breaker()
    {
        // The Domain default policy caps state visits at 10 and time in state at 5 minutes —
        // both wrong for a machine that waits on a human. Twenty full refine cycles must pass.
        var machine = ForgeStateMachineFactory.Create();
        Assert.True(machine.TryFire(ForgeTrigger.BriefSubmitted, out _));

        for (var cycle = 0; cycle < 20; cycle++)
        {
            Assert.True(machine.TryFire(ForgeTrigger.BlueprintSubmitted, out _));
            Assert.True(machine.TryFire(ForgeTrigger.Rendered, out _));
            Assert.True(machine.TryFire(ForgeTrigger.Validated, out _));
            Assert.True(machine.TryFire(ForgeTrigger.TestCompleted, out _));
            Assert.True(machine.TryFire(ForgeTrigger.Diagnosed, out _));
            Assert.True(machine.TryFire(ForgeTrigger.RefineRequested, out _), $"breaker tripped at cycle {cycle}");
        }

        Assert.False(machine.IsCircuitBroken);
    }
}
