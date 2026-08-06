using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.ConsoleApp.Services;
using Orkeon.Cli.Scripting.Dispatch;
using Orkeon.Infrastructure.Communication;

namespace Orkeon.ConsoleApp.Tests.Services;

public sealed class TuiFidelityWiringAgentRowsTests
{
    private static CommandDispatchService NewDispatch()
        => new(
            new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
            new AgentCommandDirectory(),
            new CommandInstanceRegistry(),
            NullLogger<CommandDispatchService>.Instance);

    [Fact]
    public void With_nothing_delegated_the_pane_is_empty()
    {
        // capture_3 contract: `main` only appears alongside at least one other agent.
        // Sequential work by the primary loop shows no pane at all.
        Assert.Empty(TuiFidelityWiring.BuildAgentRows(NewDispatch()));
    }

    [Fact]
    public void A_running_instance_brings_main_in_filled_and_renders_hollow_itself()
    {
        var dispatch = NewDispatch();
        var instance = dispatch.Registry.Register(
            "assistant", CommandInstanceKind.Async, "crew:main-loop", "main-loop", Guid.NewGuid());

        var rows = TuiFidelityWiring.BuildAgentRows(dispatch);

        // `● main` first (the primary loop), then the hollow delegated agent.
        var main = Assert.Single(rows, r => r.Name == "main");
        Assert.True(main.IsActive);
        Assert.Equal("", main.Description);
        Assert.Null(main.Elapsed);
        Assert.Null(main.Tokens);

        var row = Assert.Single(rows, r => r.Ticket == instance.Ticket);
        Assert.False(row.IsActive);
    }

    [Fact]
    public void A_finished_instance_disappears_immediately()
    {
        // User ruling: `idle` means *waiting*, not *finished* — a finished agent
        // leaves the pane at once (ps/inspect stay the audit trail).
        var dispatch = NewDispatch();
        var instance = dispatch.Registry.Register(
            "assistant", CommandInstanceKind.Async, "crew:main-loop", "main-loop", Guid.NewGuid());
        instance.Complete(new CommandResponse("crew:main-loop", "main-loop", success: true, payload: "ok", error: null));

        var rows = TuiFidelityWiring.BuildAgentRows(dispatch);

        Assert.DoesNotContain(rows, r => r.Ticket == instance.Ticket);
        // And with it gone, nothing else runs: `main` withdraws too.
        Assert.Empty(rows);
    }

    [Fact]
    public void A_failed_instance_disappears_like_a_completed_one()
    {
        var dispatch = NewDispatch();
        var instance = dispatch.Registry.Register(
            "review", CommandInstanceKind.Async, "crew:code-review", "review", Guid.NewGuid());
        instance.Fail("boom");

        Assert.Empty(TuiFidelityWiring.BuildAgentRows(dispatch));
    }

    [Fact]
    public void Live_progress_replaces_the_intent_in_the_description()
    {
        var dispatch = NewDispatch();
        var instance = dispatch.Registry.Register(
            "compact", CommandInstanceKind.Async, "crew:session-compact", "compact the session", Guid.NewGuid());
        instance.ReportProgress(new CommandProgress(step: null, percent: 40.0, message: "synthesis"));

        var rows = TuiFidelityWiring.BuildAgentRows(dispatch);
        Assert.Equal("synthesis · 40%", Assert.Single(rows, r => r.Ticket == instance.Ticket).Description);
    }

    [Fact]
    public void Attributed_tokens_reach_the_row_and_zero_stays_unattributed()
    {
        var dispatch = NewDispatch();
        var credited = dispatch.Registry.Register(
            "assistant", CommandInstanceKind.Async, "crew:main-loop", "main-loop", Guid.NewGuid());
        var untouched = dispatch.Registry.Register(
            "review", CommandInstanceKind.Async, "crew:code-review", "review", Guid.NewGuid());
        credited.AddTokens(118_300);

        var rows = TuiFidelityWiring.BuildAgentRows(dispatch);

        Assert.Equal(118_300, Assert.Single(rows, r => r.Ticket == credited.Ticket).Tokens);
        // No usage observed ⇒ null, which the pane renders as the honest `—`.
        Assert.Null(Assert.Single(rows, r => r.Ticket == untouched.Ticket).Tokens);
    }

    // ── BuildProgressReader (staleness guard) ───────────────────────────────

    [Fact]
    public void Progress_of_a_live_instance_reads_back_as_from_live_instance()
    {
        var dispatch = NewDispatch();
        var broker = new Orkeon.Cli.Scripting.Progress.ProgressBroker();
        var instance = dispatch.Registry.Register(
            "compact", CommandInstanceKind.Async, "crew:session-compact", "compact", Guid.NewGuid());
        broker.Report(new Orkeon.Cli.Scripting.Progress.ProgressSnapshot
        {
            Label = "Compacting conversation", Step = 2, Total = 5, Ticket = instance.Ticket,
        });

        var info = TuiFidelityWiring.BuildProgressReader(broker, dispatch)();

        Assert.NotNull(info);
        Assert.True(info!.FromLiveInstance);
        Assert.Equal("Compacting conversation", info.Label);
    }

    [Fact]
    public void Progress_of_a_dead_instance_is_swept_not_rendered()
    {
        // A crew that dies between report and done must not park a bar forever
        // (design-review pitfall): the reader cross-checks the registry and sweeps.
        var dispatch = NewDispatch();
        var broker = new Orkeon.Cli.Scripting.Progress.ProgressBroker();
        var instance = dispatch.Registry.Register(
            "compact", CommandInstanceKind.Async, "crew:session-compact", "compact", Guid.NewGuid());
        broker.Report(new Orkeon.Cli.Scripting.Progress.ProgressSnapshot
        {
            Label = "Compacting conversation", Ticket = instance.Ticket,
        });
        instance.Fail("boom");

        Assert.Null(TuiFidelityWiring.BuildProgressReader(broker, dispatch)());
        Assert.Null(broker.Current); // swept, not just hidden
    }

    [Fact]
    public void Unticketed_progress_passes_through_as_foreground_only()
    {
        var broker = new Orkeon.Cli.Scripting.Progress.ProgressBroker();
        broker.Report(new Orkeon.Cli.Scripting.Progress.ProgressSnapshot { Label = "deploy", Percent = 50 });

        var info = TuiFidelityWiring.BuildProgressReader(broker, NewDispatch())();

        Assert.NotNull(info);
        Assert.False(info!.FromLiveInstance); // the view renders it only while running
    }
}
