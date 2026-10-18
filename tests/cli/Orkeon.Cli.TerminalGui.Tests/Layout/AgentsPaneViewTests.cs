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
