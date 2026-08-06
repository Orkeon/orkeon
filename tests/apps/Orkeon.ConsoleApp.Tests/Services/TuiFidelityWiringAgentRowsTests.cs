using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.ConsoleApp.Services;
using Orkeon.Cli.Scripting.Dispatch;
using Orkeon.Infrastructure.Communication;

namespace Orkeon.ConsoleApp.Tests.Services;

public sealed class TuiFidelityWiringAgentRowsTests
{
    /// <summary>Hand-rolled clock (repo convention: no mocking framework).</summary>
    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static CommandDispatchService NewDispatch()
        => new(
            new InMemoryAgentChannel(NullLogger<InMemoryAgentChannel>.Instance),
            new AgentCommandDirectory(),
            new CommandInstanceRegistry(),
            NullLogger<CommandDispatchService>.Instance);

    [Fact]
    public void A_completed_instance_reads_done_not_idle()
    {
        // The exact bug of the capture: three finished crews all showing `idle`.
        var dispatch = NewDispatch();
        var instance = dispatch.Registry.Register(
            "assistant", CommandInstanceKind.Async, "crew:main-loop", "main-loop", Guid.NewGuid());
        instance.Complete(new CommandResponse("crew:main-loop", "main-loop", success: true, payload: "ok", error: null));

        var rows = TuiFidelityWiring.BuildAgentRows(dispatch, new FakeClock());

        var row = Assert.Single(rows, r => r.Ticket == instance.Ticket);
        Assert.Equal("done", row.Status);
        Assert.False(row.IsIdle);
        Assert.False(row.IsActive);
    }

    [Fact]
    public void A_failed_instance_carries_its_failure()
    {
        var dispatch = NewDispatch();
        var instance = dispatch.Registry.Register(
            "review", CommandInstanceKind.Async, "crew:code-review", "review", Guid.NewGuid());
        instance.Fail("boom");

        var rows = TuiFidelityWiring.BuildAgentRows(dispatch, new FakeClock());
        Assert.Equal("failed", Assert.Single(rows, r => r.Ticket == instance.Ticket).Status);
    }

    [Fact]
    public void Terminal_rows_age_out_of_the_pane_after_the_retention_window()
    {
        var dispatch = NewDispatch();
        var clock = new FakeClock();
        var instance = dispatch.Registry.Register(
            "assistant", CommandInstanceKind.Async, "crew:main-loop", "main-loop", Guid.NewGuid());
        instance.Complete(new CommandResponse("crew:main-loop", "main-loop", success: true, payload: "", error: null));

        clock.Now = DateTimeOffset.UtcNow.AddMinutes(1);
        Assert.Contains(TuiFidelityWiring.BuildAgentRows(dispatch, clock), r => r.Ticket == instance.Ticket);

        clock.Now = DateTimeOffset.UtcNow.AddMinutes(5);
        var rows = TuiFidelityWiring.BuildAgentRows(dispatch, clock);
        Assert.DoesNotContain(rows, r => r.Ticket == instance.Ticket);
        // `main` never ages out — the pane still describes the REPL itself.
        Assert.Contains(rows, r => r.Name == "main");
    }

    [Fact]
    public void A_running_instance_is_active_with_the_running_status()
    {
        var dispatch = NewDispatch();
        var instance = dispatch.Registry.Register(
            "assistant", CommandInstanceKind.Async, "crew:main-loop", "main-loop", Guid.NewGuid());

        var rows = TuiFidelityWiring.BuildAgentRows(dispatch, new FakeClock());

        var row = Assert.Single(rows, r => r.Ticket == instance.Ticket);
        Assert.Equal("running", row.Status);
        Assert.True(row.IsActive);
        // And `main` yields the active bullet while something is delegated.
        Assert.False(Assert.Single(rows, r => r.Name == "main").IsActive);
    }

    [Fact]
    public void Live_progress_replaces_the_intent_in_the_description()
    {
        var dispatch = NewDispatch();
        var instance = dispatch.Registry.Register(
            "compact", CommandInstanceKind.Async, "crew:session-compact", "compact the session", Guid.NewGuid());
        instance.ReportProgress(new CommandProgress(step: null, percent: 40.0, message: "synthesis"));

        var rows = TuiFidelityWiring.BuildAgentRows(dispatch, new FakeClock());
        Assert.Equal("synthesis · 40%", Assert.Single(rows, r => r.Ticket == instance.Ticket).Description);
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
