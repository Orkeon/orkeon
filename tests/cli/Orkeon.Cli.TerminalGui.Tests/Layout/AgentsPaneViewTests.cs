using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Logging;
using Orkeon.Cli.TerminalGui.Tests.Doubles;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

/// <summary>Serialises every class that initialises the process-wide Terminal.Gui Application.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TerminalGuiApplicationCollection
{
    public const string Name = "TerminalGuiApplication";
}

[Collection(TerminalGuiApplicationCollection.Name)]
public class AgentsPaneViewTests
{
    private static TuiIntegration IntegrationWithOneRow(Action? interrupt = null) => new()
    {
        AgentRows = () => [new AgentRowInfo { Name = "worker@crew", Ticket = "T-1" }],
        InterruptCurrent = interrupt,
    };

    [Fact]
    [Trait("BR", "CH-05")]
    public void F4_enters_the_selection_and_Enter_raises_RowActivated()
    {
        using var pane = new AgentsPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance);
        pane.Bind(IntegrationWithOneRow());
        string? ticket = null;
        pane.RowActivated += (_, e) => ticket = e.Ticket;

        pane.EnterSelectionMode();
        Assert.True(pane.SelectionActive);
        pane.NewKeyDownEvent(Key.Enter);

        Assert.Equal("T-1", ticket);
    }

    [Fact]
    public void A_faulted_poll_reports_no_task_as_finished_nor_relaunched()
    {
        var fail = false;
        var lines = new List<string>();
        using var pane = new AgentsPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance) { TranscriptSink = lines.Add };
        pane.Bind(new TuiIntegration
        {
            AgentRows = () => fail
                ? throw new InvalidOperationException("registry gone")
                : [new AgentRowInfo { Name = "main", IsActive = true }, new AgentRowInfo { Name = "worker@crew", Ticket = "T-1" }],
        });
        lines.Clear();

        fail = true;
        pane.Refresh();
        fail = false;
        pane.Refresh();

        Assert.Empty(lines);
    }

    [Fact]
    public void A_task_finished_between_two_polls_is_written_from_the_finished_feed()
    {
        var finished = new List<AgentRowInfo>();
        var lines = new List<string>();
        using var pane = new AgentsPaneView(new TerminalGuiOptions { Glyphs = GlyphMode.Always }, InlineDispatcher.Instance) { TranscriptSink = lines.Add };
        pane.Bind(new TuiIntegration { AgentRows = () => [], FinishedAgentRows = () => finished });

        finished.Add(new AgentRowInfo { Name = "quick", Ticket = "T-9", Elapsed = TimeSpan.FromSeconds(2), State = TaskRowState.Failed });
        pane.Refresh();

        Assert.Equal(["● quick", "  ⎿  Backgrounded agent (↓ tasks)", "✗ Agent \"quick\" failed · 2s"], lines);
    }

    private static AgentRowInfo EndedRow(string name, string ticket, TaskRowState state = TaskRowState.Done, int seconds = 135)
        => new() { Name = name, Ticket = ticket, Elapsed = TimeSpan.FromSeconds(seconds), State = state };

    private static AgentsPaneView PaneWith(IReadOnlyList<AgentRowInfo> live, IReadOnlyList<AgentRowInfo> ended)
    {
        var pane = new AgentsPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance);
        pane.Bind(new TuiIntegration { AgentRows = () => live, FinishedAgentRows = () => ended });
        pane.Refresh();
        return pane;
    }

    [Fact]
    [Trait("BR", "BR-07")]
    [Trait("BR", "A5")]
    public void ShouldListEndedTaskAfterLiveOnes_WhenHostRetainsIt()
    {
        using var pane = PaneWith(
            [new AgentRowInfo { Name = "main", IsActive = true }, new AgentRowInfo { Name = "live", Ticket = "T-2" }],
            [EndedRow("done", "T-1")]);

        Assert.Equal(["main", "live", "done"], pane.Rows.Select(r => r.Name).ToArray());
    }

    [Fact]
    [Trait("BR", "BR-07")]
    [Trait("BR", "A5")]
    public void ShouldListMainRow_WhenOnlyEndedTasksRemain()
    {
        using var pane = PaneWith([], [EndedRow("done", "T-1")]);

        Assert.Equal(["main", "done"], pane.Rows.Select(r => r.Name).ToArray());
    }

    [Theory]
    [Trait("BR", "BR-07")]
    [InlineData(true, TaskRowState.Running)]
    [InlineData(false, TaskRowState.Idle)]
    public void ShouldGiveMainThePromptTurnState_WhenOnlyEndedTasksRemain(bool turnRunning, TaskRowState expected)
    {
        using var pane = new AgentsPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance);
        pane.Bind(new TuiIntegration
        {
            AgentRows = () => [],
            FinishedAgentRows = () => [EndedRow("done", "T-1")],
            PromptTurnRunning = () => turnRunning,
        });
        pane.Refresh();

        Assert.Equal(expected, pane.Rows.Single(r => r.Name == "main").State);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    [Trait("BR", "UC-05")]
    public void ShouldShowOutcomeAndFinalDuration_WhenRowHasEnded()
    {
        var segments = AgentsPaneModel.Segments(GlyphSet.Unicode, EndedRow("done", "T-1"), 60);

        var text = RowSegment.Concat(segments);
        Assert.Contains("✔ finished", text, StringComparison.Ordinal);
        Assert.Contains("2m 15s", text, StringComparison.Ordinal);
        Assert.Contains(segments, s => s.Text.Contains("✔ finished", StringComparison.Ordinal) && s.Role == PaletteRole.Dim);
    }

    [Fact]
    [Trait("BR", "BR-08")]
    public void ShouldKeepEndedRowsOfLastRead_WhenEndedFeedFaults()
    {
        var fail = false;
        using var pane = new AgentsPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance);
        pane.Bind(new TuiIntegration
        {
            AgentRows = () => [new AgentRowInfo { Name = "main", IsActive = true }],
            FinishedAgentRows = () => fail ? throw new InvalidOperationException("feed gone") : [EndedRow("done", "T-1")],
        });
        pane.Refresh();
        Assert.Equal(["main", "done"], pane.Rows.Select(r => r.Name).ToArray());

        fail = true;
        pane.Refresh();

        Assert.Equal(["main", "done"], pane.Rows.Select(r => r.Name).ToArray());

        using var unwired = new AgentsPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance);
        unwired.Bind(new TuiIntegration { AgentRows = () => [new AgentRowInfo { Name = "main", IsActive = true }] });
        unwired.Refresh();
        Assert.Equal(["main"], unwired.Rows.Select(r => r.Name).ToArray());
    }

    [Fact]
    [Trait("BR", "BR-07")]
    [Trait("BR", "UC-05")]
    public void ShouldListTaskAndKeepItsScreen_WhenItStartedAndEndedBetweenPolls()
    {
        using var pane = PaneWith([new AgentRowInfo { Name = "main", IsActive = true }], [EndedRow("quick", "T-9", seconds: 2)]);

        var row = Assert.Single(pane.Rows, r => r.Ticket == "T-9");
        Assert.Equal(TaskRowState.Done, row.State);
    }

    [Fact]
    public void The_rows_follow_the_width_of_the_pane_down_to_a_tiny_one()
    {
        using var pane = new AgentsPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance);
        pane.Bind(new TuiIntegration
        {
            AgentRows = () => [new AgentRowInfo { Name = "worker@crew", Ticket = "T-1", Elapsed = TimeSpan.FromSeconds(135), Tokens = 1200 }],
        });

        pane.Frame = new System.Drawing.Rectangle(0, 0, 50, 1);
        pane.Layout();
        Assert.Equal(50, TerminalText.Columns(pane.CurrentText));

        pane.Frame = new System.Drawing.Rectangle(0, 0, 12, 1);
        pane.Layout();
        Assert.Equal(12, TerminalText.Columns(pane.CurrentText));
    }

#pragma warning disable CS0618 // Terminal.Gui legacy static Application: the only route a headless key reaches Application.KeyDown handlers then the focused view
    [Fact]
    [Trait("BR", "CH-05")]
    public void Escape_in_selection_leaves_it_and_raises_no_interrupt()
    {
        // The key must travel the host's route: Application.KeyDown handlers, then the focused view.
        // That needs an initialised application with the toplevel running (ansi driver, headless).
        Application.Init(driverName: "ansi");
        try
        {
            var options = new TerminalGuiOptions();
            using var top = new SplitPaneToplevel(options, InlineDispatcher.Instance);
            var runner = new FakeInteractiveRunner { Running = true };
            top.Runner = runner;
            var interrupts = 0;
            var integration = IntegrationWithOneRow(() => interrupts++);
            using var find = new FindDialog(top.Logs);
            using var provider = new TerminalGuiLoggerProvider(top.Logs, options);
            GlobalKeyBindings.Install(top, provider, find, integration);
            Application.Begin(top);
            top.Agents.Bind(integration);
            top.Agents.EnterSelectionMode();
            Assert.True(top.Agents.SelectionActive);

            Application.RaiseKeyDownEvent(Key.Esc);

            Assert.False(top.Agents.SelectionActive);
            Assert.Equal(0, runner.CancelRequests);
            Assert.Equal(0, interrupts);
        }
        finally
        {
            Application.Shutdown();
        }
    }
}
#pragma warning restore CS0618
